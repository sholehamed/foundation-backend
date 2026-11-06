using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed class BffOidcChallengeHttpTests
{
    [Fact]
    public async Task Tenant_login_challenges_with_code_pkce_and_host_bound_context()
    {
        await using var harness = await Harness.StartAsync();

        var response = await harness.Client.GetAsync(
            "https://tenant-a.shinera.localhost/bff/auth/login?returnUrl=%2Fdashboard");

        await AssertResponseStatusAsync(response, HttpStatusCode.Redirect);
        Assert.NotNull(response.Headers.Location);

        var location = response.Headers.Location!;
        Assert.Equal("identity.example.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);

        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("tenant", query["login_context"].ToString());
        Assert.Equal("tenant-a", query["tenant"].ToString());
        Assert.Equal(
            "https://tenant-a.shinera.localhost/signin-oidc",
            query["redirect_uri"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(query["state"].ToString()));
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"].ToString()));
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"].ToString()));
        Assert.Contains("permissions", query["scope"].ToString());
        Assert.Contains("roles", query["scope"].ToString());

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, value => value.Contains("Correlation", StringComparison.OrdinalIgnoreCase));
        Assert.All(cookies, value => Assert.Contains("secure", value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task System_login_uses_system_context_without_tenant_parameter()
    {
        await using var harness = await Harness.StartAsync();

        var response = await harness.Client.GetAsync(
            "https://localhost/bff/auth/login?returnUrl=%2Fsystem%2Fdashboard");

        await AssertResponseStatusAsync(response, HttpStatusCode.Redirect);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);

        Assert.Equal("system", query["login_context"].ToString());
        Assert.False(query.ContainsKey("tenant"));
        Assert.Equal(
            "https://localhost/signin-oidc",
            query["redirect_uri"].ToString());
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example/path")]
    [InlineData("/x/../admin")]
    [InlineData("/x%2f%2fevil.example")]
    public async Task Unsafe_return_url_is_rejected(string returnUrl)
    {
        await using var harness = await Harness.StartAsync();
        var encoded = Uri.EscapeDataString(returnUrl);

        var response = await harness.Client.GetAsync(
            "https://tenant-a.shinera.localhost/bff/auth/login?returnUrl=" + encoded);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_parameter_cannot_override_the_host_derived_tenant()
    {
        await using var harness = await Harness.StartAsync();

        var response = await harness.Client.GetAsync(
            "https://tenant-a.shinera.localhost/bff/auth/login?returnUrl=%2Fdashboard&tenant=tenant-b");

        await AssertResponseStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_host_cannot_start_a_bff_session()
    {
        await using var harness = await Harness.StartAsync();

        var response = await harness.Client.GetAsync(
            "https://unrelated.example/bff/auth/login?returnUrl=%2Fdashboard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task AssertResponseStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}. TestServer diagnostic: {body}");
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        private Harness(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public static async Task<Harness> StartAsync()
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
                ["Identity:Bff:ClientSecret"] = "test-secret-with-more-than-thirty-two-characters",
                ["Identity:Bff:RedirectUris:0"] = "https://tenant-a.shinera.localhost/signin-oidc",
                ["Identity:Bff:RedirectUris:1"] = "https://localhost/signin-oidc",
                ["Identity:Bff:SystemHosts:0"] = "localhost"
            });

            builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);
            builder.Services.PostConfigure<OpenIdConnectOptions>(
                BffDefaults.OidcScheme,
                options =>
                {
                    // An isolated TestServer must never contact the fake IdP hostname.
                    // Setting Configuration alone in PostConfigure is too late:
                    // OpenIdConnectPostConfigureOptions has already built the dynamic
                    // metadata manager. Override the manager itself with fixed test metadata.
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            Issuer = "https://identity.example.com/",
                            AuthorizationEndpoint = "https://identity.example.com/api/connect/authorize",
                            TokenEndpoint = "https://identity.example.com/api/system/Auth/login",
                            UserInfoEndpoint = "https://identity.example.com/api/system/Auth/userinfo"
                        });
                });

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseAuthentication();
            app.MapBffEndpoints(builder.Configuration);
            await app.StartAsync();

            return new Harness(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
