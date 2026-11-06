using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Modules.System.Bff;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Foundation.Bff.Tests;

/// <summary>
/// Exercises the actual BFF OIDC middleware callback with a local fake OIDC
/// provider and signed ID tokens. It is NOT a deployed Identity Server E2E.
/// </summary>
public sealed class BffOidcCallbackHttpTests
{
    [Fact]
    public async Task Valid_signed_oidc_callback_issues_only_opaque_browser_cookie()
    {
        using var issuer = new FakeOidcIssuer();
        await using var harness = await Harness.StartAsync(issuer);

        var challenge = await harness.BeginAsync();
        var callback = await harness.CallbackAsync(challenge);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/dashboard", callback.Headers.Location?.OriginalString);
        Assert.True(issuer.CodeWasExchanged);
        Assert.True(issuer.UserInfoWasRequested);

        var cookies = GetSetCookies(callback);
        var bffCookie = Assert.Single(cookies,
            value => value.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));

        Assert.Contains("httponly", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", bffCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id_token", bffCookie, StringComparison.OrdinalIgnoreCase);

        using var session = await harness.ReadSessionAsync(bffCookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var body = await session.Content.ReadAsStringAsync();
        Assert.Contains("\"authenticated\":true", body);
        Assert.Contains("\"tokensHeldByServer\":true", body);
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tampered_state_cannot_create_a_browser_session()
    {
        using var issuer = new FakeOidcIssuer();
        await using var harness = await Harness.StartAsync(issuer);
        var challenge = await harness.BeginAsync();

        var result = await harness.CallbackAsync(challenge, stateOverride: "tampered-invalid-state");

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.DoesNotContain(GetSetCookies(result),
            value => value.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
        Assert.False(issuer.CodeWasExchanged);
    }

    [Fact]
    public async Task Incorrect_signed_nonce_rejects_the_callback()
    {
        using var issuer = new FakeOidcIssuer { UseWrongNonce = true };
        await using var harness = await Harness.StartAsync(issuer);
        var challenge = await harness.BeginAsync();

        var result = await harness.CallbackAsync(challenge);

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.DoesNotContain(GetSetCookies(result),
            value => value.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tenant_principal_for_another_host_is_rejected_after_userinfo()
    {
        using var issuer = new FakeOidcIssuer { TenantSlug = "tenant-b" };
        await using var harness = await Harness.StartAsync(issuer);
        var challenge = await harness.BeginAsync();

        var result = await harness.CallbackAsync(challenge);

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.DoesNotContain(GetSetCookies(result),
            value => value.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replaying_callback_code_fails_even_with_original_correlation_cookies()
    {
        using var issuer = new FakeOidcIssuer();
        await using var harness = await Harness.StartAsync(issuer);
        var challenge = await harness.BeginAsync();

        var first = await harness.CallbackAsync(challenge);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var replay = await harness.CallbackAsync(challenge);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.DoesNotContain(GetSetCookies(replay),
            value => value.StartsWith(BffDefaults.CookieName + "=", StringComparison.Ordinal));
    }

    private static string[] GetSetCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToArray()
            : [];

    private sealed record LoginChallenge(
        string State,
        string BrowserCookies);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Harness(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Harness> StartAsync(FakeOidcIssuer issuer)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Bff:Enabled"] = "true",
                ["Identity:Bff:Authority"] = "https://identity.example.com",
                ["Identity:Bff:ClientId"] = "foundation-browser-bff",
                ["Identity:Bff:ClientSecret"] = FakeOidcIssuer.ClientSecret,
                ["Identity:Bff:RedirectUris:0"] = "https://tenant-a.shinera.localhost/signin-oidc",
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost"
            });

            builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);

            // Only a TestServer override: the real middleware runs its ordinary
            // discovery, signature/issuer/audience and callback validation.
            builder.Services.PostConfigure<OpenIdConnectOptions>(
                BffDefaults.OidcScheme,
                options => options.BackchannelHttpHandler = issuer);

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseAuthentication();
            app.MapBffEndpoints(builder.Configuration);
            app.MapGet("/__test/bff-ticket", async (HttpContext context) =>
            {
                var result = await context.AuthenticateAsync(BffDefaults.CookieScheme);
                return Results.Ok(new
                {
                    authenticated = result.Succeeded,
                    tokensHeldByServer =
                        !string.IsNullOrWhiteSpace(result.Properties?.GetTokenValue("access_token")) &&
                        !string.IsNullOrWhiteSpace(result.Properties?.GetTokenValue("id_token"))
                });
            });
            await app.StartAsync();

            var client = new HttpClient(app.GetTestServer().CreateHandler())
            {
                BaseAddress = new Uri("https://tenant-a.shinera.localhost")
            };
            return new Harness(app, client);
        }

        public async Task<LoginChallenge> BeginAsync()
        {
            using var response = await _client.GetAsync("/bff/auth/login?returnUrl=%2Fdashboard");
            Assert.True(response.StatusCode == HttpStatusCode.Redirect,
                $"Expected OIDC challenge 302 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var authorize = response.Headers.Location;
            Assert.NotNull(authorize);

            var query = QueryHelpers.ParseQuery(authorize!.Query);
            Assert.Equal("code", query["response_type"].ToString());
            Assert.Equal("S256", query["code_challenge_method"].ToString());
            Assert.Equal("tenant", query["login_context"].ToString());
            Assert.Equal("tenant-a", query["tenant"].ToString());

            // Relay the real OIDC challenge parameters to the fake issuer.
            // No OAuth token or code is ever created by browser test code.
            FakeIssuer.PrepareFromAuthorize(authorize);

            var cookie = string.Join("; ",
                GetSetCookies(response).Select(value => value.Split(';')[0]));

            Assert.Contains("Correlation", cookie, StringComparison.OrdinalIgnoreCase);
            return new LoginChallenge(query["state"].ToString(), cookie);
        }

        private FakeOidcIssuer FakeIssuer
            => (FakeOidcIssuer)_app.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(BffDefaults.OidcScheme).BackchannelHttpHandler!;

        public async Task<HttpResponseMessage> CallbackAsync(
            LoginChallenge challenge,
            string? stateOverride = null)
        {
            var callbackUrl = QueryHelpers.AddQueryString("/signin-oidc",
                new Dictionary<string, string?>
                {
                    ["code"] = FakeOidcIssuer.AuthorizationCode,
                    ["state"] = stateOverride ?? challenge.State
                });
            using var request = new HttpRequestMessage(HttpMethod.Get, callbackUrl);
            request.Headers.TryAddWithoutValidation("Cookie", challenge.BrowserCookies);
            return await _client.SendAsync(request);
        }

        public Task<HttpResponseMessage> ReadSessionAsync(string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/__test/bff-ticket");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            return _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeOidcIssuer : HttpMessageHandler
    {
        public const string AuthorizationCode = "fake-once-code";
        public const string ClientSecret = "this-test-secret-is-at-least-thirty-two-characters";
        private const string Issuer = "https://identity.example.com/";
        private const string ClientId = "foundation-browser-bff";

        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RsaSecurityKey _signingKey;
        private readonly string _subject = Guid.NewGuid().ToString();
        private readonly string _tenantId = Guid.NewGuid().ToString();
        private string? _nonce;
        private string? _challenge;
        private string? _redirectUri;
        private bool _used;

        public bool UseWrongNonce { get; set; }
        public string TenantSlug { get; set; } = "tenant-a";
        public bool CodeWasExchanged { get; private set; }
        public bool UserInfoWasRequested { get; private set; }

        public FakeOidcIssuer()
        {
            _signingKey = new RsaSecurityKey(_rsa) { KeyId = "fake-oidc-signing-key" };
        }

        public void PrepareFromAuthorize(Uri authorize)
        {
            var q = QueryHelpers.ParseQuery(authorize.Query);
            _nonce = q["nonce"].ToString();
            _challenge = q["code_challenge"].ToString();
            _redirectUri = q["redirect_uri"].ToString();
            Assert.False(string.IsNullOrWhiteSpace(_nonce));
            Assert.False(string.IsNullOrWhiteSpace(_challenge));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            switch (path)
            {
                case "/.well-known/openid-configuration":
                    return JsonResponse(new
                    {
                        issuer = Issuer,
                        authorization_endpoint = Issuer + "api/connect/authorize",
                        token_endpoint = Issuer + "api/system/Auth/login",
                        userinfo_endpoint = Issuer + "api/system/Auth/userinfo",
                        jwks_uri = Issuer + ".well-known/jwks",
                        response_types_supported = new[] { "code" },
                        response_modes_supported = new[] { "query" },
                        subject_types_supported = new[] { "public" },
                        id_token_signing_alg_values_supported = new[] { "RS256" },
                        token_endpoint_auth_methods_supported = new[] { "client_secret_post" }
                    });

                case "/.well-known/jwks":
                    var key = _rsa.ExportParameters(false);
                    return JsonResponse(new
                    {
                        keys = new[]
                        {
                            new
                            {
                                kty = "RSA", use = "sig",
                                kid = _signingKey.KeyId, alg = "RS256",
                                n = Base64UrlEncoder.Encode(key.Modulus!),
                                e = Base64UrlEncoder.Encode(key.Exponent!)
                            }
                        }
                    });

                case "/api/system/Auth/login":
                    if (request.Method != HttpMethod.Post || _used)
                        return JsonResponse(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);

                    var fields = QueryHelpers.ParseQuery(
                        await request.Content!.ReadAsStringAsync(cancellationToken));
                    var verifier = fields["code_verifier"].ToString();
                    var codeChallenge = WebEncoders.Base64UrlEncode(
                        SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                    if (fields["grant_type"] != "authorization_code" ||
                        fields["code"] != AuthorizationCode ||
                        fields["client_id"] != ClientId ||
                        fields["client_secret"] != ClientSecret ||
                        fields["redirect_uri"] != _redirectUri ||
                        !string.Equals(codeChallenge, _challenge, StringComparison.Ordinal))
                    {
                        return JsonResponse(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                    }

                    _used = true;
                    CodeWasExchanged = true;
                    var now = DateTime.UtcNow;
                    var token = new JwtSecurityToken(
                        issuer: Issuer,
                        audience: ClientId,
                        claims:
                        [
                            new Claim("sub", _subject),
                            new Claim("nonce", UseWrongNonce ? "wrong-nonce" : _nonce!)
                        ],
                        notBefore: now.AddSeconds(-10),
                        expires: now.AddMinutes(5),
                        signingCredentials: new SigningCredentials(
                            _signingKey, SecurityAlgorithms.RsaSha256));
                    return JsonResponse(new
                    {
                        access_token = "test-only-server-side-access-token",
                        refresh_token = "test-only-server-side-refresh-token",
                        id_token = new JwtSecurityTokenHandler().WriteToken(token),
                        token_type = "Bearer",
                        expires_in = 300
                    });

                case "/api/system/Auth/userinfo":
                    UserInfoWasRequested = true;
                    if (request.Headers.Authorization?.Scheme != "Bearer")
                        return JsonResponse(new { error = "unauthorized" }, HttpStatusCode.Unauthorized);
                    return JsonResponse(new
                    {
                        sub = _subject,
                        account_type = "tenant",
                        tenant = TenantSlug,
                        tenant_id = _tenantId,
                        username = "test-tenant-user",
                        given_name = "Test Tenant"
                    });

                default:
                    return JsonResponse(new { error = "not_found" }, HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage JsonResponse(
            object value,
            HttpStatusCode status = HttpStatusCode.OK)
            => new(status)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value),
                    Encoding.UTF8,
                    "application/json")
            };

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rsa.Dispose();
            base.Dispose(disposing);
        }
    }
}
