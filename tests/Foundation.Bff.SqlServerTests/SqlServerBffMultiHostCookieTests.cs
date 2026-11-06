using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Bff;
using Server.Bff;
using Xunit;

namespace Foundation.Bff.SqlServerTests;

/// <summary>
/// Two independent ASP.NET Core TestServers with separate DI/Data Protection
/// providers, a shared persisted keyring and a real shared SQL Server DB.
/// This is not a deployed multi-node browser/proxy/failover test.
/// </summary>
public sealed class SqlServerBffMultiHostCookieTests
{
    [Fact]
    public async Task Opaque_cookie_issued_on_host_A_is_valid_on_B_then_revocation_on_B_denies_A_and_restarted_C()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async connectionString =>
        {
            var keyPath = Path.Combine(Path.GetTempPath(),
                "foundation-bff-dp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(keyPath);
            try
            {
                string cookie;
                await using (var first = await StartAsync(connectionString, keyPath))
                {
                    using var clientA = new HttpClient(first.GetTestServer().CreateHandler());
                    using var issue = await clientA.GetAsync("https://localhost/__test/issue");
                    Assert.Equal(HttpStatusCode.NoContent, issue.StatusCode);
                    cookie = Assert.Single(issue.Headers.GetValues("Set-Cookie"),
                        value => value.StartsWith(
                            BffDefaults.CookieName + "=", StringComparison.Ordinal))
                        .Split(';')[0];

                    await using (var second = await StartAsync(connectionString, keyPath))
                    {
                        Assert.NotSame(
                            first.Services.GetRequiredService<IDataProtectionProvider>(),
                            second.Services.GetRequiredService<IDataProtectionProvider>());

                        using var clientB =
                            new HttpClient(second.GetTestServer().CreateHandler());
                        using var before = await SendWithCookieAsync(
                            clientB, HttpMethod.Get, "/__test/me", cookie);
                        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
                        Assert.Equal("alice", await before.Content.ReadAsStringAsync());

                        using var logout = await SendWithCookieAsync(
                            clientB, HttpMethod.Post, "/__test/logout", cookie);
                        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

                        using var replayA = await SendWithCookieAsync(
                            clientA, HttpMethod.Get, "/__test/me", cookie);
                        Assert.Equal(HttpStatusCode.Unauthorized, replayA.StatusCode);
                        using var replayB = await SendWithCookieAsync(
                            clientB, HttpMethod.Get, "/__test/me", cookie);
                        Assert.Equal(HttpStatusCode.Unauthorized, replayB.StatusCode);
                    }
                }

                // Simulate a new web node after both previous hosts stopped.
                await using (var restarted = await StartAsync(connectionString, keyPath))
                {
                    using var clientC =
                        new HttpClient(restarted.GetTestServer().CreateHandler());
                    using var replayC = await SendWithCookieAsync(
                        clientC, HttpMethod.Get, "/__test/me", cookie);
                    Assert.Equal(HttpStatusCode.Unauthorized, replayC.StatusCode);
                }
            }
            finally
            {
                Directory.Delete(keyPath, recursive: true);
            }
        });

    private static Task<HttpResponseMessage> SendWithCookieAsync(
        HttpClient client, HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, "https://localhost" + path);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }

    private static async Task<WebApplication> StartAsync(
        string connectionString, string keyPath)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
            ["Identity:Bff:Enabled"] = "true",
            ["Identity:Bff:Authority"] = "https://identity.example.com",
            ["Identity:Bff:ClientId"] = "foundation-browser-bff",
            ["Identity:Bff:ClientSecret"] =
                "test-only-secret-with-more-than-thirty-two-characters",
            ["Identity:Bff:RedirectUris:0"] = "https://localhost/signin-oidc",
            ["ConnectionStrings:BffSessions"] = connectionString
        });
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName("Foundation-Bff-SqlServer-Shared-Test");
        builder.Services.AddSqlServerBffAtomicTicketStore(builder.Configuration);
        builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseAuthentication();
        app.MapGet("/__test/issue", async (HttpContext context) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "alice"),
                 new Claim("sub", Guid.NewGuid().ToString()),
                 new Claim("account_type", "system")],
                BffDefaults.CookieScheme));
            var properties = new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(45)
            };
            await context.SignInAsync(BffDefaults.CookieScheme, principal, properties);
            return Results.NoContent();
        });
        app.MapGet("/__test/me", async (HttpContext context) =>
        {
            var result = await context.AuthenticateAsync(BffDefaults.CookieScheme);
            return result.Succeeded
                ? Results.Text(result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value)
                : Results.Unauthorized();
        });
        // Test-only endpoint verifies actual CookieAuthentication sign-out and
        // ITicketStore.RemoveAsync. Production logout also enforces CSRF/Origin.
        app.MapPost("/__test/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(BffDefaults.CookieScheme);
            return Results.NoContent();
        });
        await app.StartAsync();
        return app;
    }
}
