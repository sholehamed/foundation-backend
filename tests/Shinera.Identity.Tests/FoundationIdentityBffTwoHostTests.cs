using System.Net;
using HttpMethod = System.Net.Http.HttpMethod;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Server.Bff;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// Uses TWO actual ASP.NET Core TestServer instances: Foundation's real
/// OpenIddict Identity Server, and the real BFF OIDC client middleware.
/// No fake issuer, handcrafted authorization code, JWT or UserInfo response.
/// A test-only identity-cookie issuing endpoint replaces the interactive
/// CAPTCHA/password UI; that UI remains a distinct browser E2E gate.
/// </summary>
public sealed class FoundationIdentityBffTwoHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_openiddict_code_flow_creates_only_host_bound_opaque_bff_cookie(
        bool tenant)
    {
        await using var h = await Harness.StartAsync();
        var host = tenant ? Harness.TenantHost : Harness.SystemHost;
        var cookie = await h.IdentitySignInAsync(tenant);
        var challenge = await h.BeginBffAsync(host);
        var authorize = await h.AuthorizeAsync(challenge.Authorize, cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.Contains("code=", authorize.Headers.Location!.Query);

        using var callback = await h.CallbackAsync(authorize.Headers.Location!, challenge.BrowserCookies);
        Assert.True(callback.StatusCode == HttpStatusCode.Redirect,
            "Real issuer callback failed: " + (int)callback.StatusCode + " " +
            await callback.Content.ReadAsStringAsync() + " Backchannel: " + h.BackchannelSummary);
        Assert.Equal("/dashboard", callback.Headers.Location?.OriginalString);

        var bffCookie = Assert.Single(GetCookies(callback),
            x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("secure", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id_token", bffCookie, StringComparison.OrdinalIgnoreCase);

        var cookiePair = bffCookie.Split(';')[0];
        using var session = await h.ReadSessionAsync(host, cookiePair);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("no-store", session.Headers.CacheControl?.ToString());
        var body = await session.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(tenant ? "tenant" : "system",
            json.RootElement.GetProperty("context").GetProperty("type").GetString());
        if (tenant)
        {
            Assert.Equal("tenant-a",
                json.RootElement.GetProperty("context").GetProperty("tenantSlug").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null,
                json.RootElement.GetProperty("context").GetProperty("tenantId").ValueKind);
        }
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id_token", body, StringComparison.OrdinalIgnoreCase);

        // An attacker manually replaying the same opaque cookie on a different
        // host must not get an authenticated BFF session.
        using var wrongHost = await h.ReadSessionAsync(
            tenant ? Harness.SystemHost : Harness.TenantHost, cookiePair);
        Assert.Equal(HttpStatusCode.Forbidden, wrongHost.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_openiddict_callback_with_Web_validator_revokes_changed_identity_stamp(
        bool tenant)
    {
        await using var h = await Harness.StartAsync(withLiveWebValidator: true);
        var host = tenant ? Harness.TenantHost : Harness.SystemHost;
        var identityCookie = await h.IdentitySignInAsync(tenant);
        var challenge = await h.BeginBffAsync(host);
        using var authorization = await h.AuthorizeAsync(challenge.Authorize, identityCookie);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        Assert.Contains("code=", authorization.Headers.Location!.Query);

        using var callback = await h.CallbackAsync(
            authorization.Headers.Location!, challenge.BrowserCookies);
        Assert.True(callback.StatusCode == HttpStatusCode.Redirect,
            "Real OIDC+Web validation callback failed: " +
            (int)callback.StatusCode + " " + await callback.Content.ReadAsStringAsync() +
            " Backchannel: " + h.BackchannelSummary);

        var browserCookie = Assert.Single(GetCookies(callback),
            x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
        Assert.DoesNotContain("identity_stamp", browserCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("security_stamp", browserCookie, StringComparison.OrdinalIgnoreCase);
        var cookie = browserCookie.Split(';')[0];

        using var initiallyValid = await h.ReadSessionAsync(host, cookie);
        Assert.Equal(HttpStatusCode.OK, initiallyValid.StatusCode);

        // Check the actual OIDC-produced ticket, not a test-only seeded one.
        await h.ChangeSecurityStampAsync(tenant, "rotated-stamp");
        using var invalidated = await h.ReadSessionAsync(host, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidated.StatusCode);

        // Reverting DB state cannot resurrect a server-revoked ticket.
        await h.ChangeSecurityStampAsync(tenant, tenant ? "tenant-stamp" : "system-stamp");
        using var replay = await h.ReadSessionAsync(host, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Real_openiddict_authorize_rejects_system_cookie_for_tenant_callback()
    {
        await using var h = await Harness.StartAsync();
        var systemCookie = await h.IdentitySignInAsync(tenant: false);
        var challenge = await h.BeginBffAsync(Harness.TenantHost);
        using var result = await h.AuthorizeAsync(challenge.Authorize, systemCookie);
        Assert.False(result.Headers.Location?.Query.Contains("code=", StringComparison.Ordinal) == true);
        Assert.NotEqual(HttpStatusCode.OK, result.StatusCode);
        // Access denied is an OpenIddict response, never an authorization code.
        Assert.DoesNotContain("access_token", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Security_stamp_revocation_before_code_exchange_prevents_bff_session()
    {
        await using var h = await Harness.StartAsync();
        var cookie = await h.IdentitySignInAsync(tenant: true);
        var challenge = await h.BeginBffAsync(Harness.TenantHost);
        using var authorize = await h.AuthorizeAsync(challenge.Authorize, cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.Contains("code=", authorize.Headers.Location!.Query);

        await h.RevokeTenantSecurityStampAsync();

        using var callback = await h.CallbackAsync(authorize.Headers.Location!, challenge.BrowserCookies);
        Assert.NotEqual(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.DoesNotContain(GetCookies(callback),
            x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Real_authorization_code_cannot_be_exchanged_twice()
    {
        await using var h = await Harness.StartAsync();
        var cookie = await h.IdentitySignInAsync(tenant: true);
        var challenge = await h.BeginBffAsync(Harness.TenantHost);
        using var authorize = await h.AuthorizeAsync(challenge.Authorize, cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var callbackUri = authorize.Headers.Location!;

        using var first = await h.CallbackAsync(callbackUri, challenge.BrowserCookies);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        using var replay = await h.CallbackAsync(callbackUri, challenge.BrowserCookies);
        Assert.NotEqual(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.DoesNotContain(GetCookies(replay),
            x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_oidc_correlation_cookie_cannot_create_bff_session()
    {
        await using var h = await Harness.StartAsync();
        var cookie = await h.IdentitySignInAsync(tenant: true);
        var challenge = await h.BeginBffAsync(Harness.TenantHost);
        using var authorize = await h.AuthorizeAsync(challenge.Authorize, cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);

        using var callback = await h.CallbackAsync(authorize.Headers.Location!, browserCookies: "");
        Assert.NotEqual(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.DoesNotContain(GetCookies(callback),
            x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_identity_session_supports_csrf_logout_without_revoking_other_bff_tickets(bool tenant)
    {
        await using var h = await Harness.StartAsync();
        var host = tenant ? Harness.TenantHost : Harness.SystemHost;
        var identityCookie = await h.IdentitySignInAsync(tenant);

        async Task<string> SignInBffAsync()
        {
            var challenge = await h.BeginBffAsync(host);
            using var authorize = await h.AuthorizeAsync(challenge.Authorize, identityCookie);
            Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
            Assert.Contains("code=", authorize.Headers.Location!.Query);
            using var callback = await h.CallbackAsync(authorize.Headers.Location!, challenge.BrowserCookies);
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            return Assert.Single(GetCookies(callback),
                x => x.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
        }

        var first = await SignInBffAsync();
        var second = await SignInBffAsync();
        Assert.NotEqual(first, second);
        using var csrf = await h.SendBffAsync(host, "/bff/auth/csrf", HttpMethod.Get, first);
        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);
        var tokenCookie = Assert.Single(GetCookies(csrf),
            x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0];
        var token = Uri.UnescapeDataString(tokenCookie["XSRF-TOKEN=".Length..]);
        var antiCookie = Assert.Single(GetCookies(csrf),
            x => x.StartsWith("__Host-Foundation.Antiforgery=", StringComparison.Ordinal)).Split(';')[0];
        var cookies = first + "; " + antiCookie;

        using var rejected = await h.SendBffAsync(host, "/bff/auth/logout", HttpMethod.Post, cookies);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var stillActive = await h.ReadSessionAsync(host, first);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);

        using var logout = await h.SendBffAsync(host, "/bff/auth/logout", HttpMethod.Post, cookies, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("no-store", logout.Headers.CacheControl?.ToString());
        using var revoked = await h.ReadSessionAsync(host, first);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var replay = await h.SendBffAsync(host, "/bff/auth/logout", HttpMethod.Post, cookies, token);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var other = await h.ReadSessionAsync(host, second);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        // Local BFF logout deliberately preserves the Identity session:
        // a fresh code flow can still create a NEW ticket, never revive first.
        var third = await SignInBffAsync();
        Assert.NotEqual(first, third);
        using var original = await h.ReadSessionAsync(host, first);
        Assert.Equal(HttpStatusCode.Unauthorized, original.StatusCode);
    }

    private static string[] GetCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToArray()
            : [];

    private sealed class IssuerHttpProbe(HttpMessageHandler identityHandler)
        : DelegatingHandler(identityHandler)
    {
        private readonly ConcurrentQueue<string> _diagnostics = new();
        public string Summary => string.Join(" | ", _diagnostics);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var result = await base.SendAsync(request, cancellationToken);
            // HTTP status + media type only. Never log access tokens, secrets,
            // query contents, Authorization headers or response bodies.
            _diagnostics.Enqueue(request.Method.Method + " " +
                request.RequestUri?.AbsolutePath + " -> " + (int)result.StatusCode +
                " [" + result.Content.Headers.ContentType?.MediaType + "]");
            return result;
        }
    }

    private sealed record BffChallenge(Uri Authorize, string BrowserCookies);

    private sealed class Harness : IAsyncDisposable
    {
        public const string IdentityHost = "identity.example.com";
        public const string TenantHost = "tenant-a.shinera.localhost";
        public const string SystemHost = "localhost";
        private const string ClientId = "foundation-browser-bff";
        private const string ClientSecret = "test-only-bff-client-secret-minimum-32-characters";
        private readonly WebApplication _identity;
        private readonly WebApplication _bff;
        private readonly HttpClient _identityClient;
        private readonly HttpClient _bffClient;
        private readonly IssuerHttpProbe _identityBackchannel;
        public string BackchannelSummary => _identityBackchannel.Summary;

        private Harness(
            WebApplication identity, WebApplication bff,
            HttpClient identityClient, HttpClient bffClient,
            IssuerHttpProbe identityBackchannel)
        {
            _identity = identity;
            _bff = bff;
            _identityClient = identityClient;
            _bffClient = bffClient;
            _identityBackchannel = identityBackchannel;
        }

        public static async Task<Harness> StartAsync(bool withLiveWebValidator = false)
        {
            var identityBuilder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            identityBuilder.WebHost.UseTestServer();
            identityBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:Authority"] = "https://" + IdentityHost,
                ["Identity:Bff:ClientId"] = ClientId,
                ["Identity:Bff:ClientSecret"] = ClientSecret,
                ["Identity:Bff:RedirectUris:0"] = "https://" + TenantHost + BffDefaults.CallbackPath,
                ["Identity:Bff:RedirectUris:1"] = "https://" + SystemHost + BffDefaults.CallbackPath,
                ["Identity:LoginUrls:System"] = "/system/auth/login",
                ["Identity:LoginUrls:Tenant"] = "/auth/login"
            });

            // A stable unique DB name is required across seeding and HTTP
            // request scopes. Generating it inside AddDbContext's options
            // callback would create a different database for every scope.
            var databaseName = Guid.NewGuid().ToString("N");
            // One explicitly shared InMemory root across two independent DI
            // containers: the real issuer owns writes, the BFF host reads
            // current user/tenant/stamp state through the Web composition bridge.
            var databaseRoot = new InMemoryDatabaseRoot();
            identityBuilder.Services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot).UseOpenIddict());
            identityBuilder.Services.AddScoped<ITenantContext, TenantContext>();
            identityBuilder.Services.AddScoped<IIdentityDbContext>(sp =>
                sp.GetRequiredService<IdentityDbContext>());
            identityBuilder.Services.AddSingleton<ISystemTenantScope, DefaultSystemTenantScope>();
            identityBuilder.Services.AddScoped<IIdentityPrincipalFactory, IdentityPrincipalFactory>();
            identityBuilder.Services.AddScoped<IIdentitySessionValidator, IdentitySessionValidator>();

            identityBuilder.Services.AddOpenIddict()
                .AddCore(options => options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>())
                .AddServer(options =>
                {
                    options.SetIssuer(new Uri("https://" + IdentityHost + "/"));
                    options.SetAuthorizationEndpointUris("/api/connect/authorize");
                    options.SetTokenEndpointUris("/api/system/Auth/login");
                    options.SetUserInfoEndpointUris("/api/system/Auth/userinfo");
                    options.SetConfigurationEndpointUris("/.well-known/openid-configuration");
                    options.SetJsonWebKeySetEndpointUris("/.well-known/jwks");
                    options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                    options.AllowRefreshTokenFlow();
                    options.RegisterScopes("api", "profile", "email", "roles", "permissions",
                        OpenIddictConstants.Scopes.OfflineAccess);
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                    options.UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableTokenEndpointPassthrough()
                        .EnableUserInfoEndpointPassthrough();
                    options.DisableAccessTokenEncryption();
                });
            identityBuilder.Services.AddAuthentication(
                    IdentityServerCookieDefaults.AuthenticationScheme)
                .AddCookie(IdentityServerCookieDefaults.AuthenticationScheme, options =>
                {
                    options.Cookie.Name = IdentityServerCookieDefaults.CookieName;
                    options.Cookie.Path = "/";
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.Domain = null;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                });
            identityBuilder.Services.AddAuthorization();

            var identityApp = identityBuilder.Build();
            identityApp.UseDeveloperExceptionPage();
            identityApp.UseAuthentication();
            identityApp.UseAuthorization();
            new Connect().Map(identityApp);
            identityApp.MapPost("/api/system/Auth/login",
                new Auth().ExchangeAuthorizationCodeAsync).AllowAnonymous();
            // Explicit Delegate overload is essential: passing Task<IResult>
            // directly as a RequestDelegate would silently discard Results.Ok
            // and emit 200 with an empty body / no Content-Type.
            identityApp.MapGet("/api/system/Auth/userinfo",
                (Delegate)new Func<HttpContext, Task<IResult>>(
                    new Auth().GetUserInfo)).AllowAnonymous();

            // Test-only Identity cookie issuance via the real scoped principal
            // factory. This deliberately excludes the CAPTCHA/password UI.
            identityApp.MapGet("/__test/sign-in", async (
                HttpContext ctx, IdentityDbContext db,
                IIdentityPrincipalFactory factory) =>
            {
                var tenantUser = ctx.Request.Query.ContainsKey("tenant");
                var user = await db.Users.FirstAsync(x =>
                    x.UserType == (tenantUser ? UserType.Tenant : UserType.System));
                var tenant = tenantUser
                    ? await db.Tenants.FirstAsync(x => x.Id == user.TenantId)
                    : null;
                var principal = await factory.CreateAsync(
                    user,
                    tenantUser ? IdentityLoginContext.Tenant : IdentityLoginContext.System,
                    tenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await ctx.SignInAsync(IdentityServerCookieDefaults.AuthenticationScheme,
                    principal, new AuthenticationProperties
                    {
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15)
                    });
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            }).AllowAnonymous();

            await identityApp.StartAsync();
            using (var scope = identityApp.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.EnsureCreatedAsync();

                var systemUser = new User(null, "system-user", "system@test.local",
                    "System", "User", UserType.System)
                {
                    NormalizedUserName = "SYSTEM-USER",
                    NormalizedEmail = "SYSTEM@TEST.LOCAL",
                    SecurityStamp = "system-stamp",
                    PasswordHash = "test-only",
                    RowVersion = [1]
                };
                var tenant = new Tenant(Guid.NewGuid(), null,
                    "Tenant A", "tenant-a", "tenant-a.shinera.localhost")
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
                db.AddRange(systemUser, tenant, tenantUser);
                await db.SaveChangesAsync();

                await OpenIddictBffClientSeeder.SeedAsync(
                    scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(),
                    identityApp.Configuration, identityApp.Environment);
            }

            var identityClient = new HttpClient(identityApp.GetTestServer().CreateHandler())
            {
                BaseAddress = new Uri("https://" + IdentityHost)
            };

            var backchannel = new IssuerHttpProbe(identityApp.GetTestServer().CreateHandler());
            var bffBuilder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            bffBuilder.WebHost.UseTestServer();
            bffBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:Authority"] = "https://" + IdentityHost,
                ["Identity:Bff:ClientId"] = ClientId,
                ["Identity:Bff:ClientSecret"] = ClientSecret,
                ["Identity:Bff:RedirectUris:0"] = "https://" + TenantHost + BffDefaults.CallbackPath,
                ["Identity:Bff:RedirectUris:1"] = "https://" + SystemHost + BffDefaults.CallbackPath,
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
                ["Identity:Bff:SystemHosts:0"] = SystemHost
            });
            bffBuilder.Services.AddBffAuthentication(
                bffBuilder.Configuration, bffBuilder.Environment);
            if (withLiveWebValidator)
            {
                bffBuilder.Services.AddDbContext<IdentityDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName, databaseRoot).UseOpenIddict());
                bffBuilder.Services.AddScoped<ITenantContext, TenantContext>();
                bffBuilder.Services.AddScoped<IIdentityDbContext>(sp =>
                    sp.GetRequiredService<IdentityDbContext>());
                bffBuilder.Services.AddSingleton<ISystemTenantScope, DefaultSystemTenantScope>();
                bffBuilder.Services.AddScoped<IIdentityPrincipalFactory, IdentityPrincipalFactory>();
                // Register the actual production Web host bridge, including
                // OIDC ticket snapshot capture and BFF cookie revalidation.
                bffBuilder.Services.AddBffIdentitySessionValidation();
            }
            // Only the test HTTP transport is overridden. Metadata discovery,
            // certificate/JWKS signature validation, PKCE, token exchange and
            // UserInfo are handled by the real production middleware.
            bffBuilder.Services.Configure<OpenIdConnectOptions>(
                BffDefaults.OidcScheme,
                options => options.BackchannelHttpHandler = backchannel);

            // Test-only diagnostic. Never expose an OIDC exception to a
            // production browser; keep the real handler's generic 401.
            bffBuilder.Services.PostConfigure<OpenIdConnectOptions>(
                BffDefaults.OidcScheme,
                options =>
                {
                    var original = options.Events.OnRemoteFailure;
                    options.Events.OnRemoteFailure = async context =>
                    {
                        await original(context);
                        await context.HttpContext.Response.WriteAsync(
                            "\nTEST_ONLY_OIDC_FAILURE: " + context.Failure);
                    };
                });

            var bffApp = bffBuilder.Build();
            bffApp.UseAuthentication();
            bffApp.MapBffEndpoints(bffBuilder.Configuration);
            await bffApp.StartAsync();
            var bffClient = new HttpClient(bffApp.GetTestServer().CreateHandler())
            {
                BaseAddress = new Uri("https://" + TenantHost)
            };
            return new Harness(identityApp, bffApp, identityClient, bffClient, backchannel);
        }

        public async Task<string> IdentitySignInAsync(bool tenant)
        {
            using var response = await _identityClient.GetAsync(
                "/__test/sign-in" + (tenant ? "?tenant=1" : ""));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent,
                "Test-only Identity sign-in returned " + (int)response.StatusCode +
                ": " + await response.Content.ReadAsStringAsync());
            return Assert.Single(GetCookies(response),
                x => x.StartsWith(IdentityServerCookieDefaults.CookieName + "=",
                    StringComparison.Ordinal)).Split(';')[0];
        }

        public async Task<BffChallenge> BeginBffAsync(string host)
        {
            using var challenge = await _bffClient.GetAsync(
                "https://" + host + "/bff/auth/login?returnUrl=%2Fdashboard");
            Assert.True(challenge.StatusCode == HttpStatusCode.Redirect,
                "BFF challenge: " + (int)challenge.StatusCode + " " +
                await challenge.Content.ReadAsStringAsync());
            var authorize = challenge.Headers.Location!;
            Assert.Equal(IdentityHost, authorize.Host);
            var q = QueryHelpers.ParseQuery(authorize.Query);
            Assert.Equal("code", q["response_type"].ToString());
            Assert.Equal("S256", q["code_challenge_method"].ToString());
            Assert.Equal(host == TenantHost ? "tenant" : "system", q["login_context"].ToString());
            return new BffChallenge(authorize,
                string.Join("; ", GetCookies(challenge).Select(x => x.Split(';')[0])));
        }

        public async Task<HttpResponseMessage> AuthorizeAsync(Uri url, string identityCookie)
        {
            using var request = new HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cookie", identityCookie);
            return await _identityClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> CallbackAsync(Uri callback, string browserCookies)
        {
            using var request = new HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, callback);
            if (!string.IsNullOrWhiteSpace(browserCookies))
                request.Headers.TryAddWithoutValidation("Cookie", browserCookies);
            return await _bffClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> ReadSessionAsync(string host, string cookie)
        {
            using var request = new HttpRequestMessage(
                global::System.Net.Http.HttpMethod.Get, "https://" + host + "/bff/session");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            return await _bffClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> SendBffAsync(
            string host, string path, HttpMethod method, string cookies, string? token = null)
        {
            using var request = new HttpRequestMessage(method, "https://" + host + path);
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
            request.Headers.TryAddWithoutValidation("Origin", "https://" + host);
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            if (token is not null)
                request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);
            return await _bffClient.SendAsync(request);
        }

        public async Task RevokeTenantSecurityStampAsync()
            => await ChangeSecurityStampAsync(tenant: true,
                "revoked-" + Guid.NewGuid().ToString("N"));

        public async Task ChangeSecurityStampAsync(bool tenant, string stamp)
        {
            using var scope = _identity.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.IgnoreQueryFilters(["tenant"]).FirstAsync(x =>
                x.UserType == (tenant ? UserType.Tenant : UserType.System));
            user.SecurityStamp = stamp;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _bffClient.Dispose();
            _identityClient.Dispose();
            await _bff.StopAsync();
            await _bff.DisposeAsync();
            await _identity.StopAsync();
            await _identity.DisposeAsync();
            _identityBackchannel.Dispose();
        }
    }
}
