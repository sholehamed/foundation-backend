using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Bff;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Server.Bff;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class BffLiveCookieHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_cookie_revalidation_rejects_stale_stamp_and_removes_session(bool tenant)
    {
        await using var h = await Harness.StartAsync(tenant);
        var cookie = await h.IssueTicketAsync();
        using var first = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await h.MutateUserAsync(u => u.SecurityStamp = "rotated");
        using var rejected = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        // Restoring the old stamp must not resurrect the revoked ticket.
        await h.MutateUserAsync(u => u.SecurityStamp = "initial-stamp");
        using var replay = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Live_cookie_revalidation_rejects_disabled_tenant()
    {
        await using var h = await Harness.StartAsync(tenant: true);
        var cookie = await h.IssueTicketAsync();
        using var valid = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        await h.DisableTenantAsync();
        using var denied = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Tickets_without_live_validation_baseline_are_rejected()
    {
        await using var h = await Harness.StartAsync(tenant: false);
        var cookie = await h.IssueTicketAsync(includeBaseline: false);
        using var rejected = await h.GetSessionAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly bool _tenant;
        private string Host => _tenant ? "tenant-a.shinera.localhost" : "localhost";

        private Harness(WebApplication app, HttpClient client, bool tenant)
        {
            _app = app;
            _client = client;
            _tenant = tenant;
        }

        public static async Task<Harness> StartAsync(bool tenant)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            var dbName = Guid.NewGuid().ToString("N");
            builder.Services.AddDbContext<IdentityDbContext>(
                o => o.UseInMemoryDatabase(dbName));
            builder.Services.AddScoped<ITenantContext, TenantContext>();
            builder.Services.AddScoped<IIdentityDbContext>(sp =>
                sp.GetRequiredService<IdentityDbContext>());
            builder.Services.AddSingleton<ISystemTenantScope, DefaultSystemTenantScope>();
            builder.Services.AddScoped<IIdentityPrincipalFactory, IdentityPrincipalFactory>();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:Authority"] = "https://identity.example.com",
                ["Identity:Bff:ClientId"] = "foundation-browser-bff",
                ["Identity:Bff:ClientSecret"] = "test-secret-longer-than-thirty-two-characters",
                ["Identity:Bff:RedirectUris:0"] = "https://localhost/signin-oidc",
                ["Identity:Bff:RedirectUris:1"] = "https://tenant-a.shinera.localhost/signin-oidc",
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost"
            });
            builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);
            builder.Services.AddBffIdentitySessionValidation();

            var app = builder.Build();
            app.UseAuthentication();
            app.MapBffEndpoints(builder.Configuration);

            // Test-only sign-in: build the actual principal, capture current
            // stamp/grants with the real Web adapter, issue an opaque BFF ticket.
            app.MapGet("/__test/issue", async (HttpContext ctx,
                IIdentityPrincipalFactory factory, IdentityDbContext db,
                IBffSessionAccessValidator validator) =>
            {
                var isTenant = ctx.Request.Host.Host.StartsWith("tenant-a.");
                var user = await db.Users.FirstAsync(x =>
                    x.UserType == (isTenant ? UserType.Tenant : UserType.System));
                var owningTenant = isTenant
                    ? await db.Tenants.FirstAsync(x => x.Id == user.TenantId)
                    : null;
                var principal = await factory.CreateAsync(user,
                    isTenant ? IdentityLoginContext.Tenant : IdentityLoginContext.System,
                    owningTenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var baseline = await validator.CaptureAsync(principal);
                Assert.NotNull(baseline);
                var props = new AuthenticationProperties
                {
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                    IsPersistent = false
                };
                props.Items[".bff.login_context"] = isTenant ? "tenant" : "system";
                props.Items[".bff.callback_uri"] =
                    "https://" + ctx.Request.Host.Host + "/signin-oidc";
                if (isTenant)
                    props.Items[".bff.tenant"] = "tenant-a";
                if (!ctx.Request.Query.ContainsKey("missing"))
                {
                    props.Items[".bff.identity_stamp"] = baseline!.SecurityStamp;
                    props.Items[".bff.identity_grants"] = baseline.GrantsFingerprint;
                }

                await ctx.SignInAsync(BffDefaults.CookieScheme, principal, props);
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            }).AllowAnonymous();

            await app.StartAsync();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.EnsureCreatedAsync();
                var t = new Tenant(Guid.NewGuid(), null, "Tenant A", "tenant-a",
                    "tenant-a.shinera.localhost") { RowVersion = [1] };
                var system = new User(null, "system-user", "system@example.test",
                    "System", "User", UserType.System)
                    { SecurityStamp = "initial-stamp", RowVersion = [1] };
                var tenantUser = new User(t.Id, "tenant-user", "tenant@example.test",
                    "Tenant", "User", UserType.Tenant)
                    { SecurityStamp = "initial-stamp", RowVersion = [1] };
                db.AddRange(t, system, tenantUser);
                await db.SaveChangesAsync();
            }

            return new Harness(app,
                new HttpClient(app.GetTestServer().CreateHandler()), tenant);
        }

        public async Task<string> IssueTicketAsync(bool includeBaseline = true)
        {
            using var response = await _client.GetAsync(
                "https://" + Host + "/__test/issue" +
                (includeBaseline ? "" : "?missing=1"));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            return Assert.Single(response.Headers.GetValues("Set-Cookie"),
                c => c.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal))
                .Split(';')[0];
        }

        public async Task<HttpResponseMessage> GetSessionAsync(string cookie)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, "https://" + Host + "/bff/session");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            return await _client.SendAsync(request);
        }

        public async Task MutateUserAsync(Action<User> update)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.IgnoreQueryFilters(["tenant"]).FirstAsync(x =>
                x.UserType == (_tenant ? UserType.Tenant : UserType.System));
            update(user);
            await db.SaveChangesAsync();
        }

        public async Task DisableTenantAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = await db.Tenants.FirstAsync(x => x.Slug == "tenant-a");
            tenant.IsActive = false;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
