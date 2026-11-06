using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

public sealed class OpenIddictAuthorizationHttpTests
{
    private const string RedirectUri = "https://localhost/signin-oidc";
    private const string ClientId = "test-confidential-bff";
    private const string ClientSecret = "test-only-secret-should-not-be-used-in-production-1234";

    [Fact]
    public async Task Authenticated_system_user_receives_one_time_code_with_pkce()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsSystemAsync();

        var response = await harness.AuthorizeAsync(cookie);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Expected 302 but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var destination = response.Headers.Location;
        Assert.NotNull(destination);
        Assert.Equal(new Uri(RedirectUri).GetLeftPart(UriPartial.Path),
            destination!.GetLeftPart(UriPartial.Path));

        var code = QueryHelpers.ParseQuery(destination.Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        var token = await harness.ExchangeAsync(code, harness.Verifier);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);

        using var body = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("access_token", out var access));
        Assert.False(string.IsNullOrWhiteSpace(access.GetString()));

        var replay = await harness.ExchangeAsync(code, harness.Verifier);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Authorization_code_exchange_revalidates_security_stamp()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsSystemAsync();
        var response = await harness.AuthorizeAsync(cookie);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var code = QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        await harness.ChangeSystemSecurityStampAsync();

        var token = await harness.ExchangeAsync(code, harness.Verifier);
        Assert.Equal(HttpStatusCode.BadRequest, token.StatusCode);
        Assert.Contains("invalid_grant", await token.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_pkce_verifier_rejects_token_exchange()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsSystemAsync();
        var response = await harness.AuthorizeAsync(cookie);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Expected 302 but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var code = QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();

        var token = await harness.ExchangeAsync(code, "definitely-wrong-code-verifier-but-long-enough");
        Assert.Equal(HttpStatusCode.BadRequest, token.StatusCode);
    }

    [Fact]
    public async Task Anonymous_authorization_redirects_only_to_local_login()
    {
        await using var harness = await Harness.StartAsync();
        var response = await harness.AuthorizeAsync(cookie: null);

        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Expected 302 but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/system/auth/login?", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("https://evil.example", response.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task Post_authorization_request_is_rejected_without_losing_form_parameters()
    {
        await using var harness = await Harness.StartAsync();
        var response = await harness.AuthorizePostAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var parameters = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, parameters["error"].ToString());
        Assert.False(parameters.ContainsKey("code"));
    }

    [Fact]
    public async Task Unregistered_redirect_uri_is_rejected_before_login()
    {
        await using var harness = await Harness.StartAsync();
        var response = await harness.AuthorizeAsync(
            cookie: null, redirectUri: "https://evil.example/callback");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_client_must_not_receive_an_authorization_code()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsSystemAsync();
        var response = await harness.AuthorizeAsync(cookie, clientId: "unregistered-client");

        AssertOAuthRejected(response);
    }

    [Fact]
    public async Task Missing_pkce_challenge_must_not_proceed_to_login()
    {
        await using var harness = await Harness.StartAsync();
        var response = await harness.AuthorizeAsync(cookie: null, includePkce: false);

        AssertOAuthRejected(response);
    }

    [Fact]
    public async Task Unknown_scope_must_not_receive_an_authorization_code()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsSystemAsync();
        var response = await harness.AuthorizeAsync(cookie, scopes: "openid unauthorized_scope");

        AssertOAuthRejected(response);
    }

    private static void AssertOAuthRejected(HttpResponseMessage response)
    {
        if (response.Headers.Location is { } location)
        {
            var query = QueryHelpers.ParseQuery(location.Query);
            Assert.True(query.ContainsKey("error"),
                $"OAuth request should be rejected, not redirected to login or completed: {location}");
            Assert.False(query.ContainsKey("code"));
            return;
        }

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Unexpected OAuth response: {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Tenant_cookie_cannot_authorize_system_context()
    {
        await using var harness = await Harness.StartAsync();
        var cookie = await harness.SignInAsTenantAsync();
        var response = await harness.AuthorizeAsync(cookie);

        // OpenIddict may redirect a protocol-level access_denied error to the
        // previously validated callback. Either way, no code may be issued.
        var location = response.Headers.Location;
        if (location is not null)
        {
            var parameters = QueryHelpers.ParseQuery(location.Query);
            Assert.True(parameters.ContainsKey("error"),
                $"Cross-context request unexpectedly redirected without an OAuth error: {location}");
            Assert.False(parameters.ContainsKey("code"));
        }
        else
        {
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
                $"Cross-context request unexpectedly succeeded: {(int)response.StatusCode}");
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly Guid _tenantId;
        public string Verifier { get; } =
            "example-fully-random-enough-pkce-verifier-0123456789abcdef";

        private Harness(WebApplication app, HttpClient client, Guid tenantId)
        {
            _app = app;
            _client = client;
            _tenantId = tenantId;
        }

        public static async Task<Harness> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();

            var databaseName = Guid.NewGuid().ToString("N");
            builder.Services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase(databaseName).UseOpenIddict());
            builder.Services.AddScoped<ITenantContext, TenantContext>();
            builder.Services.AddScoped<IIdentityDbContext>(sp =>
                sp.GetRequiredService<IdentityDbContext>());
            builder.Services.AddSingleton<ISystemTenantScope, DefaultSystemTenantScope>();
            builder.Services.AddScoped<IIdentityPrincipalFactory, IdentityPrincipalFactory>();
            builder.Services.AddScoped<IIdentitySessionValidator, IdentitySessionValidator>();

            builder.Services.AddOpenIddict()
                .AddCore(options => options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>())
                .AddServer(options =>
                {
                    options.SetAuthorizationEndpointUris("/api/connect/authorize");
                    options.SetTokenEndpointUris("/api/connect/token");
                    options.AllowAuthorizationCodeFlow()
                        .RequireProofKeyForCodeExchange();
                    options.AllowRefreshTokenFlow();
                    options.RegisterScopes("api", "profile", "email",
                        OpenIddictConstants.Scopes.OfflineAccess);
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                    options.UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableTokenEndpointPassthrough();
                });

            builder.Services.AddAuthentication(
                    IdentityServerCookieDefaults.AuthenticationScheme)
                .AddCookie(IdentityServerCookieDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.Cookie.Name = "__Host-Foundation.Identity";
                        options.Cookie.Path = "/";
                        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                        options.Cookie.HttpOnly = true;
                        options.Cookie.Domain = null;
                        options.Cookie.SameSite = SameSiteMode.Lax;
                    });
            builder.Services.AddAuthorization();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:ClientId"] = ClientId,
                ["Identity:Bff:ClientSecret"] = ClientSecret,
                ["Identity:Bff:RedirectUris:0"] = RedirectUri,
                ["Identity:LoginUrls:System"] = "/system/auth/login",
                ["Identity:LoginUrls:Tenant"] = "/auth/login"
            });

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseAuthentication();
            app.UseAuthorization();

            new Connect().Map(app);
            app.MapPost("/api/connect/token",
                new Auth().ExchangeAuthorizationCodeAsync).AllowAnonymous();
            app.MapGet("/__test/sign-in", async (
                HttpContext context, IdentityDbContext db,
                IIdentityPrincipalFactory principalFactory) =>
            {
                var asTenant = context.Request.Query.ContainsKey("tenant");
                var user = await db.Users.FirstAsync(x =>
                    x.UserType == (asTenant ? UserType.Tenant : UserType.System));
                var tenant = asTenant
                    ? await db.Tenants.FirstAsync(x => x.Id == user.TenantId)
                    : null;

                var principal = await principalFactory.CreateAsync(
                    user,
                    asTenant ? IdentityLoginContext.Tenant : IdentityLoginContext.System,
                    tenant,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await context.SignInAsync(
                    IdentityServerCookieDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15) });
                return Results.NoContent();
            }).AllowAnonymous();

            await app.StartAsync();
            using(var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.EnsureCreatedAsync();

                var system = new User(null, "system-user", "system@test.local",
                    "System", "User", UserType.System)
                {
                    NormalizedUserName = "SYSTEM-USER",
                    NormalizedEmail = "SYSTEM@TEST.LOCAL",
                    SecurityStamp = "system-stamp",
                    PasswordHash = "test-only",
                    RowVersion = [1]
                };
                var tenant = new Tenant(Guid.NewGuid(), null,
                    "Tenant A", "tenant-a", "tenant-a.example.test")
                    { RowVersion = [1] };
                var tenantUser = new User(tenant.Id, "tenant-user", "tenant@test.local",
                    "Tenant", "User", UserType.Tenant)
                {
                    NormalizedUserName = "TENANT-USER",
                    NormalizedEmail = "TENANT@TEST.LOCAL",
                    SecurityStamp = "tenant-stamp",
                    PasswordHash = "test-only",
                    RowVersion = [1]
                };
                db.AddRange(system, tenant, tenantUser);
                await db.SaveChangesAsync();

                var manager = scope.ServiceProvider
                    .GetRequiredService<IOpenIddictApplicationManager>();
                await OpenIddictBffClientSeeder.SeedAsync(
                    manager, app.Configuration, app.Environment);
            }

            var server = app.GetTestServer();
            // A raw TestServer message handler does not follow redirects,
            // allowing us to inspect OIDC Location headers directly.
            var client = new HttpClient(server.CreateHandler());
            client.BaseAddress = new Uri("https://localhost");
            return new Harness(app, client, Guid.Empty);
        }

        public async Task<string> SignInAsSystemAsync()
            => await SignInAsync("/__test/sign-in");

        public async Task<string> SignInAsTenantAsync()
            => await SignInAsync("/__test/sign-in?tenant=1");

        private async Task<string> SignInAsync(string url)
        {
            var response = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            return response.Headers.GetValues("Set-Cookie")
                .First(x => x.StartsWith("__Host-Foundation.Identity=", StringComparison.Ordinal))
                .Split(';')[0];
        }

        public async Task ChangeSystemSecurityStampAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.FirstAsync(x => x.UserType == UserType.System);
            user.SecurityStamp = "revoked-" + Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }

        public async Task<HttpResponseMessage> AuthorizeAsync(
            string? cookie, string redirectUri = RedirectUri,
            string clientId = ClientId, string scopes = "openid profile api",
            bool includePkce = true)
        {
            var challenge = WebEncoders.Base64UrlEncode(
                SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = scopes,
                ["state"] = "teststate123",
                ["nonce"] = "testnonce123",
                ["login_context"] = "system"
            };
            if (includePkce)
            {
                query["code_challenge"] = challenge;
                query["code_challenge_method"] = "S256";
            }

            using var request = new HttpRequestMessage(
                global::System.Net.Http.HttpMethod.Get,
                QueryHelpers.AddQueryString("/api/connect/authorize", query));
            if (!string.IsNullOrWhiteSpace(cookie))
                request.Headers.Add("Cookie", cookie);
            return await _client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> AuthorizePostAsync()
        {
            var challenge = WebEncoders.Base64UrlEncode(
                SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
            return await _client.PostAsync("/api/connect/authorize",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["response_type"] = "code",
                    ["client_id"] = ClientId,
                    ["redirect_uri"] = RedirectUri,
                    ["scope"] = "openid profile api",
                    ["state"] = "teststate123",
                    ["nonce"] = "testnonce123",
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                    ["login_context"] = "system"
                }));
        }

        public async Task<HttpResponseMessage> ExchangeAsync(string code, string verifier)
        {
            return await _client.PostAsync("/api/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = ClientId,
                    ["client_secret"] = ClientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["code_verifier"] = verifier
                }));
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
