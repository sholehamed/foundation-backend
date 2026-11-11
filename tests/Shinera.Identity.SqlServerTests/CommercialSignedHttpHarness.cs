using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Isolated signed-JWT HTTP server hosted ONLY in test assembly. Nothing here
/// is registered in Web/Program.cs or a production EndpointGroup. It checks
/// real ASP.NET authentication/authorization + source-owned operation + SQL
/// commercial execution; it does NOT impersonate full production OpenIddict,
/// BFF Cookie/CSRF, or a real shipped Commercial business module.
/// </summary>
internal static class CommercialSignedHttpHarness
{
    private sealed record TestRequest(string IdempotencyKey, string? ClientTenantId);

    public static async Task VerifyAsync(
        DbContextOptions<IdentityDbContext> dbOptions,
        ResourceContract[] resources,
        CapabilityContract[] capabilities,
        ResourceOperationContract[] operations,
        Guid licensedUser, Guid licensedTenant,
        Guid unlicensedUser, Guid unlicensedTenant,
        Guid ungrantedUser)
    {
        var jwtSecret = RandomNumberGenerator.GetBytes(64);
        var key = new SymmetricSecurityKey(jwtSecret);
        const string issuer = "foundation-fr17c3c-test-only";
        const string audience = "foundation-commercial-test-only";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true, ValidIssuer = issuer,
                    ValidateAudience = true, ValidAudience = audience,
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                    RequireSignedTokens = true
                };
            });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // ONLY isolated TestServer maps this route. In the real product the
        // source-owned module controls its own EndpointKey and domain adapter.
        app.MapPost("/test-only/commercial/create", async (
            HttpContext http, TestRequest request, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.User.FindFirst("uid")?.Value, out var user) ||
                !Guid.TryParse(http.User.FindFirst("tid")?.Value, out var tenant) ||
                user == Guid.Empty || tenant == Guid.Empty)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Do not read request.ClientTenantId! Tenant comes exclusively
            // from a cryptographically validated JWT claim in this fixture.
            var scoped = new TenantContext();
            scoped.InitializeTenant(user, tenant, [tenant], [tenant], tenant);
            await using var identity = new IdentityDbContext(dbOptions, scoped);
            var execution = await new TenantCommercialOperationExecutor(
                identity, scoped, resources, capabilities, operations)
                .ExecuteModuleAsync(
                    "APPOINTMENTS.BOOKINGS.CREATE", request.IdempotencyKey,
                    [],
                    [new CommercialQuotaRequirement("appointments.monthly", 1)],
                    async (unit, token) =>
                    {
                        await using var command = new SqlCommand(
                            "INSERT dbo.FR17C3B_TestWrites(Id,TenantId,IsActive) " +
                            "VALUES (@id,@tenant,0)",
                            unit.Connection, unit.Transaction);
                        command.Parameters.AddWithValue("@id", Guid.NewGuid());
                        command.Parameters.AddWithValue("@tenant", tenant);
                        await command.ExecuteNonQueryAsync(token);
                    }, ct: ct);
            if (execution.Committed)
                return Results.Ok(new { execution.Code });
            if (execution.Code is
                "permission_not_granted" or "module_not_entitled" or
                "writable_tenant_context_required" or
                "tenant_context_invalid" or
                "inactive_or_unregistered_resource")
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Conflict(new { execution.Code });
        }).RequireAuthorization();

        await app.StartAsync();
        using var client = app.GetTestClient();

        string CreateToken(Guid uid, Guid tid, byte[]? secret = null)
        {
            var credentials = new SigningCredentials(
                secret is null ? key : new SymmetricSecurityKey(secret),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(issuer, audience,
                [
                    new Claim("uid", uid.ToString()),
                    new Claim("tid", tid.ToString()),
                ],
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        async Task<HttpResponseMessage> Post(
            string? bearer, string idempotency, string? clientTenant = null)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, "/test-only/commercial/create")
            {
                Content = JsonContent.Create(new TestRequest(
                    idempotency, clientTenant))
            };
            if (bearer is not null)
                req.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearer);
            return await client.SendAsync(req);
        }

        using (var anonymous = await Post(null, "http-anonymous-01"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using (var tampered = await Post(
                   CreateToken(licensedUser, licensedTenant,
                       RandomNumberGenerator.GetBytes(64)), "http-tampered-01"))
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);

        // Validly signed Token for a user with no delegated CREATE still fails.
        using (var noPermission = await Post(
                   CreateToken(ungrantedUser, licensedTenant), "http-no-grant-01"))
            Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);

        // A valid user's CREATE permission cannot license an unpaid Tenant.
        using (var noLicense = await Post(
                   CreateToken(unlicensedUser, unlicensedTenant), "http-no-plan-01"))
            Assert.Equal(HttpStatusCode.Forbidden, noLicense.StatusCode);

        var licensed = CreateToken(licensedUser, licensedTenant);
        // Forged request body tenant is ignored; the signed HomeTenant wins.
        using (var success = await Post(
                   licensed, "http-tenant-approved-01", unlicensedTenant.ToString()))
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        // A replay of committed quota must not re-run the business callback.
        using (var replay = await Post(licensed, "http-tenant-approved-01"))
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        using (var exhausted = await Post(licensed, "http-over-quota-01"))
            Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);

        await using var assertion = new IdentityDbContext(dbOptions,
            new TenantContext());
        var licensedUsage = await assertion.Database.SqlQueryRaw<long>(
            "SELECT ConsumedUnits AS [Value] FROM dbo.TenantUsageBalances " +
            "WHERE TenantId={0} AND CapabilityCode={1}",
            licensedTenant, "appointments.monthly").SingleAsync();
        Assert.Equal(5L, licensedUsage);

        var foreignUsage = await assertion.Database.SqlQueryRaw<long>(
            "SELECT COUNT_BIG(*) AS [Value] FROM dbo.TenantUsageEvents " +
            "WHERE TenantId={0}", unlicensedTenant).SingleAsync();
        Assert.Equal(0L, foreignUsage);
    }
}
