using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.SharedKernel.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// OpenIddict Authorization Code endpoint. Identity Server cookie authentication
/// is explicit; the global API default remains bearer validation.
/// </summary>
public sealed class Connect : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapMethods("/api/connect/authorize", ["GET", "POST"], Authorize)
            .AllowAnonymous()
            .WithTags("OpenIddict");
    }

    public async Task<IResult> Authorize(
        HttpContext httpContext,
        IIdentitySessionValidator sessionValidator,
        IOpenIddictApplicationManager applicationManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var request = httpContext.GetOpenIddictServerRequest();
        if (request is null)
            return Results.BadRequest(new { error = "invalid_request" });

        if (request.ResponseType != OpenIddictConstants.ResponseTypes.Code ||
            !request.GetScopes().Contains(OpenIddictConstants.Scopes.OpenId))
        {
            return OAuthError(
                OpenIddictConstants.Errors.InvalidRequest,
                "The authorization endpoint requires response_type=code and the openid scope.");
        }

        var requestedContext = request.GetParameter("login_context")?.ToString();
        if (requestedContext is not ("system" or "tenant"))
        {
            return OAuthError(
                OpenIddictConstants.Errors.InvalidRequest,
                "The login_context parameter must be system or tenant.");
        }

        var application = await applicationManager.FindByClientIdAsync(
            request.ClientId ?? string.Empty, cancellationToken);
        if (application is null)
        {
            return OAuthError(
                OpenIddictConstants.Errors.InvalidClient,
                "The client application is not registered.");
        }

        // This slice only supports the explicitly enabled first-party confidential BFF.
        // An arbitrary client's implicit-consent setting must not grant access to
        // a privileged existing Identity Server session.
        if (!configuration.GetValue<bool>("Identity:Bff:Enabled") ||
            !string.Equals(
                request.ClientId,
                configuration["Identity:Bff:ClientId"],
                StringComparison.Ordinal) ||
            await applicationManager.GetClientTypeAsync(application, cancellationToken)
                != OpenIddictConstants.ClientTypes.Confidential)
        {
            return OAuthError(
                OpenIddictConstants.Errors.UnauthorizedClient,
                "The client is not an enabled first-party BFF client.");
        }

        var consentType = await applicationManager.GetConsentTypeAsync(
            application, cancellationToken);
        if (consentType != OpenIddictConstants.ConsentTypes.Implicit)
        {
            return OAuthError(
                OpenIddictConstants.Errors.ConsentRequired,
                "This client requires an explicit consent flow, which is not enabled yet.");
        }

        var prompt = (request.GetParameter("prompt")?.ToString() ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (prompt.Contains("consent", StringComparer.Ordinal))
        {
            return OAuthError(
                OpenIddictConstants.Errors.ConsentRequired,
                "Interactive consent is not available for this client.");
        }

        if (prompt.Contains("login", StringComparer.Ordinal))
        {
            await httpContext.SignOutAsync(IdentityServerCookieDefaults.AuthenticationScheme);
            return RedirectToLogin(httpContext, request, configuration, requestedContext);
        }

        var cookie = await httpContext.AuthenticateAsync(
            IdentityServerCookieDefaults.AuthenticationScheme);

        if (!cookie.Succeeded || cookie.Principal is null)
        {
            return prompt.Contains("none", StringComparer.Ordinal)
                ? OAuthError(OpenIddictConstants.Errors.LoginRequired, "An active Identity Server session is required.")
                : RedirectToLogin(httpContext, request, configuration, requestedContext);
        }

        var principal = await sessionValidator.ValidateAsync(
            cookie.Principal, cancellationToken);

        if (principal is null)
        {
            await httpContext.SignOutAsync(IdentityServerCookieDefaults.AuthenticationScheme);
            return prompt.Contains("none", StringComparer.Ordinal)
                ? OAuthError(OpenIddictConstants.Errors.LoginRequired, "The Identity Server session is no longer valid.")
                : RedirectToLogin(httpContext, request, configuration, requestedContext);
        }

        var accountType = principal.FindFirst("account_type")?.Value;
        if (!string.IsNullOrWhiteSpace(requestedContext) &&
            !string.Equals(requestedContext, accountType, StringComparison.Ordinal))
        {
            return OAuthError(
                OpenIddictConstants.Errors.AccessDenied,
                "The authenticated account does not match the requested login context.");
        }

        var requestedTenant = request.GetParameter("tenant")?.ToString();
        if (accountType == "system" && !string.IsNullOrWhiteSpace(requestedTenant))
        {
            return OAuthError(
                OpenIddictConstants.Errors.AccessDenied,
                "A System account cannot be authorized with a Tenant context.");
        }

        if (accountType == "tenant" &&
            (string.IsNullOrWhiteSpace(requestedTenant) ||
            !string.Equals(
                requestedTenant,
                principal.FindFirst("tenant")?.Value,
                StringComparison.OrdinalIgnoreCase)))
        {
            return OAuthError(
                OpenIddictConstants.Errors.AccessDenied,
                "The authenticated Tenant session does not match the requested tenant.");
        }

        var requestedScopes = request.GetScopes();
        var allowedScopes = requestedScopes.Intersect(new[]
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            "api"
        }).ToArray();

        principal.SetScopes(allowedScopes);
        principal.SetResources("resource_server");
        SetClaimDestinations(principal, allowedScopes);

        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult RedirectToLogin(
        HttpContext httpContext,
        OpenIddictRequest request,
        IConfiguration configuration,
        string? requestedContext)
    {
        var loginContext = requestedContext!;


        var setting = loginContext == "system"
            ? "Identity:LoginUrls:System"
            : "Identity:LoginUrls:Tenant";

        var fallback = loginContext == "system"
            ? "/system/auth/login"
            : "/auth/login";

        var loginUrl = configuration[setting] ?? fallback;
        if (!IsSafeLocalPath(loginUrl))
        {
            return OAuthError(
                OpenIddictConstants.Errors.ServerError,
                "The configured Identity login URL must be a same-origin absolute path.");
        }

        // This return URL is built from the already validated OpenIddict endpoint
        // request. It is not copied from a caller-controlled returnUrl parameter.
        var returnUrl = $"{httpContext.Request.PathBase}{httpContext.Request.Path}{httpContext.Request.QueryString}";
        var query = new Dictionary<string, string?>
        {
            ["login_context"] = loginContext,
            ["returnUrl"] = returnUrl
        };

        var tenant = request.GetParameter("tenant")?.ToString();
        if (!string.IsNullOrWhiteSpace(tenant))
            query["tenant"] = tenant;

        return Results.Redirect(QueryHelpers.AddQueryString(loginUrl, query));
    }

    private static bool IsSafeLocalPath(string value)
        => value.StartsWith("/", StringComparison.Ordinal) &&
           !value.StartsWith("//", StringComparison.Ordinal) &&
           !value.Contains('\\') &&
           !value.Contains('\r') &&
           !value.Contains('\n') &&
           !value.Contains('#');

    private static void SetClaimDestinations(
        global::System.Security.Claims.ClaimsPrincipal principal,
        IReadOnlyCollection<string> scopes)
    {
        var profileRequested = scopes.Contains(OpenIddictConstants.Scopes.Profile);
        var emailRequested = scopes.Contains(OpenIddictConstants.Scopes.Email);

        foreach (var claim in principal.Claims)
        {
            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Subject:
                    claim.SetDestinations(
                        OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken);
                    break;

                case OpenIddictConstants.Claims.Username:
                    claim.SetDestinations(profileRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case OpenIddictConstants.Claims.Email:
                    claim.SetDestinations(emailRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case OpenIddictConstants.Claims.GivenName:
                case OpenIddictConstants.Claims.Picture:
                    claim.SetDestinations(profileRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case "auth_time":
                    claim.SetDestinations(OpenIddictConstants.Destinations.IdentityToken);
                    break;

                case OpenIddictConstants.Claims.Role:
                case "tenant_id":
                case "tenant":
                case "permission":
                case "account_type":
                    claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                    break;
            }
        }
    }

    private static IResult OAuthError(string error, string description)
        => Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }));
}
