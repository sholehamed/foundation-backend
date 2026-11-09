using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.SharedKernel.Attributes;

namespace Modules.System.Identity.Web.Endpoints;

public sealed record InteractiveLoginRequest(
    string Username,
    string Password,
    string CaptchaId,
    string CaptchaCode,
    string LoginContext,
    string? Tenant);

public class Auth : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)
            .MapPost(Login, "login", configure: x => x.AllowAnonymous())
            .MapGet(NewCaptcha, "/captcha/new", configure: x => x.AllowAnonymous())
            .MapGet(GetCaptchaImage, "/captcha/image", configure: x => x.AllowAnonymous())
            .MapGet(GetAntiforgeryToken, "csrf", configure: x => x.AllowAnonymous())
            .MapPost(InteractiveLogin, "interactive-login",
                configure: x => x.AllowAnonymous().RequireRateLimiting("identity-login"))
            .MapPost(CompleteInteractiveFirstLogin, "interactive-first-login/complete",
                configure: x => x.AllowAnonymous().RequireRateLimiting("tenant-first-login"))
            .MapGet(GetUserInfo, "/userinfo", configure: x => x.RequireAuthorization())
            .MapMethods(Logout, ["GET", "POST"], "logout");
    }

    public async Task<IResult> GetUserInfo(HttpContext context)
    {
        var result = await context.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!result.Succeeded || result.Principal is null)
            return Results.Unauthorized();

        var user = result.Principal;
        return Results.Ok(new
        {
            sub = user.GetClaim(OpenIddictConstants.Claims.Subject),
            username = user.GetClaim(OpenIddictConstants.Claims.Username),
            given_name = user.GetClaim(OpenIddictConstants.Claims.GivenName),
            picture = user.GetClaim(OpenIddictConstants.Claims.Picture),
            tenant = user.GetClaim("tenant"),
            tenant_id = user.GetClaim("tenant_id"),
            account_type = user.GetClaim("account_type"),
            roles = user.GetScopes().Contains("roles")
                ? user.FindAll(OpenIddictConstants.Claims.Role).Select(claim => claim.Value).Distinct().ToArray()
                : [],
            permissions = user.GetScopes().Contains("permissions")
                ? user.FindAll("permission").Select(claim => claim.Value).Distinct().ToArray()
                : []
        });
    }

    [Resouce("", "newcaptcha")]
    public IResult NewCaptcha(CaptchaService captchaService)
    {
        var challenge = captchaService.CreateChallenge();
        var imageUrl = $"/System/Auth/captcha/image?token={Uri.EscapeDataString(challenge.Token)}";

        return Results.Ok(new
        {
            token = challenge.Token,
            imageUrl
        });
    }

    [Resouce("", "newcaptcha")]
    public IResult GetCaptchaImage(string token, CaptchaService captchaService)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest();

        try
        {
            var bytes = captchaService.GenerateImage(token);
            return Results.File(bytes, "image/png");
        }
        catch
        {
            return Results.BadRequest();
        }
    }

    /// <summary>
    /// Issues the JavaScript-readable request token expected by Angular's XSRF
    /// interceptor. The actual ASP.NET Core antiforgery cookie remains HttpOnly.
    /// </summary>
    public IResult GetAntiforgeryToken(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
            return Results.Problem("Unable to issue an anti-forgery token.");

        httpContext.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                IsEssential = true
            });
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    /// <summary>
    /// Interactive sign-in used by the Authorization Server login UI.
    /// This creates only the Identity Server session; it never returns OAuth tokens.
    /// </summary>
    public async Task<IResult> InteractiveLogin(
        InteractiveLoginRequest input,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        CaptchaService captchaService,
        IUserCredentialAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (input is null)
            return Results.BadRequest(new { error = "invalid_request" });

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "invalid_antiforgery_token" });
        }

        if (input.LoginContext is not ("system" or "tenant"))
            return Results.BadRequest(new { error = "invalid_login_context" });

        if (!captchaService.Validate(input.CaptchaId, input.CaptchaCode))
            return Results.Unauthorized();

        var context = input.LoginContext == "system"
            ? IdentityLoginContext.System
            : IdentityLoginContext.Tenant;

        var principal = await authenticator.AuthenticateAsync(
            input.Username,
            input.Password,
            context,
            input.Tenant,
            cancellationToken);

        if (principal is null)
        {
            // Only Tenant credentials proven against an unused SQL temp hash
            // may enter the restricted branch. Never sign in the Identity
            // cookie, issue an OIDC code, or reveal an inactive account here.
            var settings = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (context == IdentityLoginContext.Tenant &&
                FirstLoginIdentityHostAllowed(httpContext, settings) &&
                TenantFirstLoginWriter.LooksLikeIssuedPassword(input.Password) &&
                !string.IsNullOrWhiteSpace(input.Tenant))
            {
                var db = httpContext.RequestServices.GetRequiredService<
                    global::Modules.System.Identity.Infrastructure.Persistence.Contexts.IdentityDbContext>();
                var passwordHasher = httpContext.RequestServices.GetRequiredService<IPasswordHasher<User>>();
                var resolved = await TenantFirstLoginWriter.ResolveForLoginAsync(
                    db, passwordHasher,
                    new TenantFirstLoginWriter.StartRequest(
                        input.Tenant, input.Username, input.Password),
                    cancellationToken);
                if (resolved.Status == "password_change_required")
                {
                    // A previously authenticated Identity user must NOT be
                    // able to continue an earlier OIDC request while a
                    // different inactive account changes its first password.
                    await httpContext.SignOutAsync(
                        IdentityServerCookieDefaults.AuthenticationScheme);
                    return Results.Ok(new
                    {
                        status = resolved.Status,
                        challenge = resolved.Challenge,
                        expiresAtUtc = resolved.ExpiresAtUtc
                    });
                }
            }
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        await httpContext.SignInAsync(
            IdentityServerCookieDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IssuedUtc = now,
                ExpiresUtc = now.AddHours(8),
                IsPersistent = false,
                AllowRefresh = true
            });

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    /// <summary>
    /// Only the separately deployed Identity origin may perform an
    /// interactive BFF first-password completion. Tenant app hosts must
    /// continue to use their already-reviewed isolated endpoint instead.
    /// An explicit authority, when configured, further pins the exact host.
    /// </summary>
    public static bool FirstLoginIdentityHostAllowed(
        HttpContext http, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled") ||
            !http.Request.IsHttps)
            return false;

        var baseDomain = configuration["Tenant:WorkspaceBaseDomain"]?
            .Trim().TrimEnd('.').ToLowerInvariant();
        var actualHost = http.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseDomain) ||
            actualHost is not { Length: > 0 } ||
            (actualHost != "identity." + baseDomain &&
             actualHost != "auth." + baseDomain))
            return false;

        var authority = configuration["Identity:Bff:Authority"];
        if (string.IsNullOrWhiteSpace(authority)) return true;
        return Uri.TryCreate(authority, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
            uri.AbsolutePath == "/" && uri.Query.Length == 0 &&
            uri.Fragment.Length == 0 &&
            string.Equals(uri.Host, actualHost, StringComparison.OrdinalIgnoreCase) &&
            uri.Port == (http.Request.Host.Port ?? 443);
    }

    /// <summary>
    /// This is NOT a login: a verified restricted challenge can only set
    /// the employee's permanent password using SQL's atomic one-use CAS.
    /// Antiforgery validates the Identity-host cookie/XSRF header again.
    /// In no outcome does this endpoint mint an Identity or BFF session.
    /// </summary>
    public async Task<IResult> CompleteInteractiveFirstLogin(
        TenantFirstLoginWriter.CompleteRequest input,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!FirstLoginIdentityHostAllowed(httpContext, configuration))
            return Results.Problem(statusCode: 400, title: "Secure Identity host required");
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "invalid_antiforgery_token" });
        }
        try
        {
            var db = httpContext.RequestServices.GetRequiredService<
                global::Modules.System.Identity.Infrastructure.Persistence.Contexts.IdentityDbContext>();
            var passwordHasher = httpContext.RequestServices.GetRequiredService<IPasswordHasher<User>>();
            await TenantFirstLoginWriter.CompleteAsync(
                db, passwordHasher, input, cancellationToken);
            await httpContext.SignOutAsync(IdentityServerCookieDefaults.AuthenticationScheme);
            return Results.Ok(new { passwordChanged = true, requiresRelogin = true });
        }
        catch (FirstLoginValidationException)
        {
            return Results.Problem(statusCode: 400,
                title: "Invalid or expired password-change challenge");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: 400,
                title: "Invalid or expired password-change challenge");
        }
    }

    [Resouce("", "logout")]
    public async Task<IResult> Logout(HttpContext httpContext)
    {
        var request = httpContext.GetOpenIddictServerRequest();
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.InvalidRequest,
                error_description = "OpenIddict request is missing."
            });
        }

        await httpContext.SignOutAsync(
            IdentityServerCookieDefaults.AuthenticationScheme);

        return Results.SignOut(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties
            {
                RedirectUri = request.PostLogoutRedirectUri ?? "/"
            });
    }

    [Resouce("", "login")]
    public async Task<IResult> Login(
        HttpContext httpContext,
        CaptchaService captchaService,
        RefreshGrantValidator refreshValidator,
        IUserCredentialAuthenticator authenticator,
        IIdentitySessionValidator sessionValidator,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest();
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.InvalidRequest,
                error_description = "OpenIddict request is missing."
            });
        }

        if (request.IsAuthorizationCodeGrantType())
        {
            return await ExchangeAuthorizationCodeAsync(
                httpContext, sessionValidator, cancellationToken);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var result = await httpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            if (!result.Succeeded || result.Principal is null ||
                !await refreshValidator.IsStillAuthorizedAsync(
                    result.Principal, cancellationToken))
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The session is no longer valid."
                    }));
            }

            return Results.SignIn(
                result.Principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!request.IsPasswordGrantType())
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.UnsupportedGrantType,
                error_description = "The specified grant type is not supported."
            });
        }

        var loginContext = request.GetParameter("login_context")?.ToString();
        if (loginContext is not ("system" or "tenant"))
        {
            return Results.BadRequest(new
            {
                error = OpenIddictConstants.Errors.InvalidRequest,
                error_description = "Invalid login context."
            });
        }

        var token = httpContext.Request.Form["captchaid"].ToString();
        var input = httpContext.Request.Form["userEnteredCaptchaCode"].ToString();
        if (!captchaService.Validate(token, input))
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid or expired CAPTCHA."
                }));
        }

        var context = loginContext == "system"
            ? IdentityLoginContext.System
            : IdentityLoginContext.Tenant;

        var principal = await authenticator.AuthenticateAsync(
            request.Username ?? string.Empty,
            request.Password ?? string.Empty,
            context,
            request.GetParameter("tenant")?.ToString(),
            cancellationToken);

        if (principal is null)
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid credentials or account context."
                }));
        }

        principal.SetScopes(request.GetScopes().Intersect(new[]
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            "api"
        }));
        principal.SetResources("resource_server");

        foreach (var claim in principal.Claims)
        {
            if (claim.Type is
                OpenIddictConstants.Claims.Subject or
                OpenIddictConstants.Claims.Username or
                OpenIddictConstants.Claims.Email or
                OpenIddictConstants.Claims.Picture or
                OpenIddictConstants.Claims.GivenName or
                OpenIddictConstants.Claims.Role or
                "tenant_id" or "tenant" or "permission" or "account_type")
            {
                claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
            }
        }

        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    /// <summary>
    /// Handles authorization-code exchanges after OpenIddict has validated the
    /// client secret, one-time code, redirect URI and PKCE verifier.
    /// Revalidates current user/tenant/security-stamp state before issuing tokens.
    /// </summary>
    public async Task<IResult> ExchangeAuthorizationCodeAsync(
        HttpContext httpContext,
        IIdentitySessionValidator sessionValidator,
        CancellationToken cancellationToken)
    {
        var result = await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!result.Succeeded || result.Principal is null)
            return InvalidGrant();

        var scopes = result.Principal.GetScopes();
        var resources = result.Principal.GetResources();

        var principal = await sessionValidator.ValidateAsync(
            result.Principal, cancellationToken);

        if (principal is null)
            return InvalidGrant();

        principal.SetScopes(scopes);
        principal.SetResources(resources);
        IdentityPrincipalDestinations.Apply(principal, scopes);

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult InvalidGrant()
        => Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization session is no longer valid."
            }));

}
