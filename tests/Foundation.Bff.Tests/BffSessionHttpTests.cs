using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

/// <summary>
/// Isolated HTTP tests for the BFF-05.1 session read API. Ticket issuance is
/// test-only; it does not stand in for a real Identity Server OIDC E2E flow.
/// </summary>
public sealed class BffSessionHttpTests
{
    [Fact]
    public async Task Anonymous_or_bearer_only_requests_return_401_without_caching()
    {
        await using var harness = await Harness.StartAsync();

        using var anonymous = await harness.ReadSessionAsync(null, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("no-store", anonymous.Headers.CacheControl?.ToString());

        using var bearer = await harness.ReadSessionAsync(
            null, "tenant-a.shinera.localhost", bearer: true);
        Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);
    }

    [Fact]
    public async Task Valid_tenant_cookie_returns_minimal_safe_user_and_context()
    {
        await using var harness = await Harness.StartAsync();

        var cookie = await harness.IssueSessionAsync("tenant-a.shinera.localhost");
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-only-access-token", cookie);

        using var response = await harness.ReadSessionAsync(
            cookie.Split(';')[0], "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.True(root.GetProperty("authenticated").GetBoolean());
        Assert.Equal("operator", root.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal("Operator", root.GetProperty("user").GetProperty("displayName").GetString());
        Assert.Equal("tenant", root.GetProperty("context").GetProperty("type").GetString());
        Assert.Equal("tenant-a", root.GetProperty("context").GetProperty("tenantSlug").GetString());
        Assert.True(Guid.TryParse(
            root.GetProperty("context").GetProperty("tenantId").GetString(), out _));
        Assert.DoesNotContain("server-only-access-token", body);
        Assert.DoesNotContain("server-only-refresh-token", body);
        Assert.DoesNotContain("access_token", body);
        Assert.DoesNotContain("refresh_token", body);
        Assert.DoesNotContain("id_token", body);
    }

    [Fact]
    public async Task Tenant_cookie_cannot_be_replayed_on_another_tenant_or_system_host()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = (await harness.IssueSessionAsync("tenant-a.shinera.localhost")).Split(';')[0];

        using var anotherTenant = await harness.ReadSessionAsync(cookie, "tenant-b.shinera.localhost");
        Assert.Equal(HttpStatusCode.Forbidden, anotherTenant.StatusCode);
        Assert.Contains("no-store", anotherTenant.Headers.CacheControl?.ToString());

        using var system = await harness.ReadSessionAsync(cookie, "localhost");
        Assert.Equal(HttpStatusCode.Forbidden, system.StatusCode);

        using var unknown = await harness.ReadSessionAsync(cookie, "unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    }

    [Fact]
    public async Task System_cookie_is_accepted_only_on_the_registered_system_host()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = (await harness.IssueSessionAsync("localhost")).Split(';')[0];

        using var system = await harness.ReadSessionAsync(cookie, "localhost");
        Assert.Equal(HttpStatusCode.OK, system.StatusCode);
        using var json = JsonDocument.Parse(await system.Content.ReadAsStringAsync());
        Assert.Equal("system", json.RootElement.GetProperty("context").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("context").GetProperty("tenantId").ValueKind);

        using var tenant = await harness.ReadSessionAsync(cookie, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.Forbidden, tenant.StatusCode);
    }

    [Fact]
    public async Task Invalid_cookie_does_not_create_a_session()
    {
        await using var harness = await Harness.StartAsync();
        using var response = await harness.ReadSessionAsync(
            BffDefaults.CookieName + "=not-a-valid-protected-cookie",
            "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_bff_registers_no_session_endpoint()
    {
        await using var harness = await Harness.StartAsync(enabled: false);
        using var response = await harness.ReadSessionAsync(null, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Harness(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Harness> StartAsync(bool enabled = true)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = enabled.ToString(),
                ["Identity:Bff:Authority"] = "https://identity.example.com",
                ["Identity:Bff:ClientId"] = "foundation-browser-bff",
                ["Identity:Bff:ClientSecret"] = "test-secret-with-more-than-thirty-two-characters",
                ["Identity:Bff:RedirectUris:0"] = "https://tenant-a.shinera.localhost/signin-oidc",
                ["Identity:Bff:RedirectUris:1"] = "https://localhost/signin-oidc",
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost"
            });
            builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);

            var app = builder.Build();
            if (enabled)
                app.UseAuthentication();
            app.MapBffEndpoints(builder.Configuration);

            if (enabled)
            {
                app.MapGet("/__test/issue-bff-cookie", async (HttpContext context) =>
                {
                    var isSystem = context.Request.Host.Host == "localhost";
                    var host = isSystem ? "localhost" : "tenant-a.shinera.localhost";
                    var now = DateTimeOffset.UtcNow;
                    var claims = new List<Claim>
                    {
                        new("sub", Guid.NewGuid().ToString()),
                        new("account_type", isSystem ? "system" : "tenant"),
                        new("username", "operator"),
                        new("given_name", "Operator")
                    };
                    if (!isSystem)
                    {
                        claims.Add(new Claim("tenant", "tenant-a"));
                        claims.Add(new Claim("tenant_id", Guid.NewGuid().ToString()));
                    }

                    var properties = new AuthenticationProperties
                    {
                        IssuedUtc = now,
                        ExpiresUtc = now.AddMinutes(30),
                        IsPersistent = false
                    };
                    properties.Items[".bff.login_context"] = isSystem ? "system" : "tenant";
                    properties.Items[".bff.callback_uri"] = "https://" + host + "/signin-oidc";
                    if (!isSystem)
                        properties.Items[".bff.tenant"] = "tenant-a";

                    properties.StoreTokens(
                    [
                        new AuthenticationToken { Name = "access_token", Value = "server-only-access-token" },
                        new AuthenticationToken { Name = "refresh_token", Value = "server-only-refresh-token" }
                    ]);
                    await context.SignInAsync(
                        BffDefaults.CookieScheme,
                        new ClaimsPrincipal(new ClaimsIdentity(claims, BffDefaults.CookieScheme)),
                        properties);
                    return Results.NoContent();
                });
            }

            await app.StartAsync();
            return new Harness(app, app.GetTestClient());
        }

        public async Task<string> IssueSessionAsync(string host)
        {
            using var response = await _client.GetAsync("https://" + host + "/__test/issue-bff-cookie");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var setCookies = response.Headers.GetValues("Set-Cookie");
            return Assert.Single(setCookies,
                c => c.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
        }

        public async Task<HttpResponseMessage> ReadSessionAsync(
            string? cookie,
            string host,
            bool bearer = false)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, "https://" + host + "/bff/session");
            if (cookie is not null)
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (bearer)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "some-legacy-token");
            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
