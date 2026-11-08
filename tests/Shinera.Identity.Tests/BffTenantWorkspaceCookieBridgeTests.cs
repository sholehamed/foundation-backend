using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Modules.System.Bff;
using Server.Bff;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class BffTenantWorkspaceCookieBridgeTests
{
    private const string TenantHost = "tenant-a.shinera.localhost";
    private const string OtherHost = "tenant-b.shinera.localhost";

    [Fact]
    public async Task Cookie_is_accepted_only_on_tenant_workspace_and_not_on_other_routes_or_hosts()
    {
        await using var fixture = await Fixture.StartAsync();
        var cookie = await fixture.IssueCookieAsync();

        using var tenantRead = await fixture.SendAsync("GET", "/api/tenant/workspace/probe",
            TenantHost, cookie);
        Assert.Equal(HttpStatusCode.OK, tenantRead.StatusCode);
        Assert.Contains("fixture-tenant-subject", await tenantRead.Content.ReadAsStringAsync());
        Assert.Contains("no-store", tenantRead.Headers.CacheControl?.ToString());

        using var otherApi = await fixture.SendAsync("GET", "/api/system/private",
            TenantHost, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, otherApi.StatusCode);

        using var wrongHost = await fixture.SendAsync("GET", "/api/tenant/workspace/probe",
            OtherHost, cookie);
        Assert.Equal(HttpStatusCode.Forbidden, wrongHost.StatusCode);

        using var wrongPrefix = await fixture.SendAsync("GET", "/api/tenant/workspace-other",
            TenantHost, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPrefix.StatusCode);
    }

    [Fact]
    public async Task Cookie_write_must_pass_antiforgery_and_same_origin_validation()
    {
        await using var fixture = await Fixture.StartAsync();
        var cookie = await fixture.IssueCookieAsync();

        using var missing = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, cookie, origin: "https://" + TenantHost);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var (xsrf, antiCookie) = await fixture.CsrfAsync(cookie);

        using var wrongOrigin = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, cookie + "; " + antiCookie, xsrf, "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);

        using var wrongFetchSite = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, cookie + "; " + antiCookie, xsrf,
            "https://" + TenantHost, "same-site");
        Assert.Equal(HttpStatusCode.Forbidden, wrongFetchSite.StatusCode);

        using var forged = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, cookie + "; " + antiCookie, "forged-xsrf",
            "https://" + TenantHost);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        using var valid = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, cookie + "; " + antiCookie, xsrf,
            "https://" + TenantHost, "same-origin");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Bearer_explicitly_takes_precedence_over_cookie_without_csrf()
    {
        await using var fixture = await Fixture.StartAsync();
        var cookie = await fixture.IssueCookieAsync();
        using var directBearer = await fixture.SendAsync("POST", "/api/tenant/workspace/probe",
            TenantHost, bearer: "valid");
        Assert.Equal(HttpStatusCode.OK, directBearer.StatusCode);

        using var forgedBearerWithCookie = await fixture.SendAsync(
            "GET", "/api/tenant/workspace/probe", TenantHost, cookie, bearer: "forged");
        Assert.Equal(HttpStatusCode.Unauthorized, forgedBearerWithCookie.StatusCode);

        using var validBearerAndCookie = await fixture.SendAsync(
            "POST", "/api/tenant/workspace/probe", TenantHost, cookie, bearer: "valid");
        Assert.Equal(HttpStatusCode.OK, validBearerAndCookie.StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly HttpClient client;

        private Fixture(WebApplication app)
        {
            this.app = app;
            client = app.GetTestClient();
        }

        public static async Task<Fixture> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:Authority"] = "https://identity.example.com",
                ["Identity:Bff:ClientId"] = "foundation-browser-bff",
                ["Identity:Bff:ClientSecret"] = "fixture-secret-longer-than-thirty-two-characters",
                ["Identity:Bff:RedirectUris:0"] = "https://" + TenantHost + "/signin-oidc",
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
                ["BackendPrefix"] = "api/"
            });
            builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);
            builder.Services.AddAuthentication("fixture-bearer")
                .AddScheme<AuthenticationSchemeOptions, FixtureBearerHandler>(
                    "fixture-bearer", _ => {});
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseMiddleware<BffTenantWorkspaceCookieBridgeMiddleware>();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapBffEndpoints(builder.Configuration);

            app.MapGet("/__test/issue", async (HttpContext context) =>
            {
                var principal = TenantPrincipal();
                var props = new AuthenticationProperties
                {
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                };
                props.Items[".bff.login_context"] = "tenant";
                props.Items[".bff.tenant"] = "tenant-a";
                props.Items[".bff.callback_uri"] =
                    "https://" + TenantHost + "/signin-oidc";
                await context.SignInAsync(BffDefaults.CookieScheme, principal, props);
                return Results.NoContent();
            }).AllowAnonymous();

            app.MapGet("/api/tenant/workspace/probe",
                (HttpContext http) => Results.Ok(new
                {
                    subject = http.User.FindFirst("sub")?.Value
                })).RequireAuthorization();
            app.MapPost("/api/tenant/workspace/probe",
                (HttpContext http) => Results.Ok(new
                {
                    subject = http.User.FindFirst("sub")?.Value
                })).RequireAuthorization();
            app.MapGet("/api/system/private", () => Results.Ok())
                .RequireAuthorization();
            app.MapGet("/api/tenant/workspace-other", () => Results.Ok())
                .RequireAuthorization();

            await app.StartAsync();
            return new Fixture(app);
        }

        private static ClaimsPrincipal TenantPrincipal()
            => new(new ClaimsIdentity(
                [
                    new Claim("sub", "fixture-tenant-subject"),
                    new Claim("account_type", "tenant"),
                    new Claim("tenant", "tenant-a"),
                    new Claim("tenant_id", "0a111111-1111-4111-8111-111111111111"),
                    new Claim("username", "fixture-user")
                ], BffDefaults.CookieScheme));

        public async Task<string> IssueCookieAsync()
        {
            using var response = await client.GetAsync(
                "https://" + TenantHost + "/__test/issue");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            return Assert.Single(response.Headers.GetValues("Set-Cookie"),
                v => v.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal))
                .Split(';')[0];
        }

        public async Task<(string Token, string AntiforgeryCookie)> CsrfAsync(string cookie)
        {
            using var response = await SendAsync("GET", "/bff/auth/csrf",
                TenantHost, cookie);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
            var readable = Assert.Single(cookies,
                v => v.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
            var antiforgery = Assert.Single(cookies,
                v => v.StartsWith("__Host-Foundation.Antiforgery=", StringComparison.Ordinal));
            return (Uri.UnescapeDataString(readable.Split(';')[0]["XSRF-TOKEN=".Length..]),
                antiforgery.Split(';')[0]);
        }

        public async Task<HttpResponseMessage> SendAsync(
            string method, string path, string host, string? cookie = null,
            string? csrf = null, string? origin = null, string? fetchSite = null,
            string? bearer = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method),
                "https://" + host + path);
            if (cookie is not null)
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (csrf is not null)
                request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrf);
            if (origin is not null)
                request.Headers.TryAddWithoutValidation("Origin", origin);
            if (fetchSite is not null)
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
            if (bearer is not null)
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class FixtureBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());
            if (value != "Bearer valid")
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token"));
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", "native-subject")], "fixture-bearer"));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, "fixture-bearer")));
        }
    }
}
