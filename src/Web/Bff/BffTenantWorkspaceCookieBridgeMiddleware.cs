using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Modules.System.Bff;
using Modules.System.Bff.Security;

namespace Server.Bff;

/// <summary>
/// FR-12G opt-in browser-only bridge for existing Tenant workspace APIs.
///
/// The default OpenIddict bearer scheme is deliberately left untouched.
/// A browser can authenticate these exact Tenant workspace endpoints with
/// its opaque BFF cookie only if no Authorization header was supplied.
/// The cookie is revalidated against the browser host and current Identity
/// database by the configured BFF cookie handler before any endpoint runs.
/// Unsafe cookie requests additionally require same-origin + antiforgery.
///
/// This bridge does not authenticate Platform/System routes, public signup,
/// arbitrary API prefixes or native/Bearer clients.
/// </summary>
public sealed class BffTenantWorkspaceCookieBridgeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PathString _tenantWorkspacePrefix;

    public BffTenantWorkspaceCookieBridgeMiddleware(
        RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var backendPrefix = (configuration["BackendPrefix"] ?? "api/").Trim('/');
        if (string.IsNullOrWhiteSpace(backendPrefix))
            throw new InvalidOperationException("BackendPrefix must be configured.");
        _tenantWorkspacePrefix = new PathString("/" + backendPrefix + "/tenant/workspace");
    }

    public async Task InvokeAsync(
        HttpContext context, BffOidcSettings settings, IAntiforgery antiforgery)
    {
        // A bearer Authorization header takes precedence even if a BFF cookie
        // is present. This preserves the existing native/API security contract
        // and prevents a stale or forged bearer token being silently rescued
        // by a browser cookie.
        if (!context.Request.Path.StartsWithSegments(_tenantWorkspacePrefix) ||
            context.Request.Headers.ContainsKey("Authorization") ||
            !context.Request.Cookies.ContainsKey(BffDefaults.CookieName))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";

        var authenticated = await context.AuthenticateAsync(BffDefaults.CookieScheme);
        if (!authenticated.Succeeded || authenticated.Principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!BffSessionContextValidator.TryValidateActiveSession(
                authenticated.Principal, authenticated.Properties,
                context.Request, settings, out _) ||
            authenticated.Principal.FindFirst("account_type")?.Value != "tenant")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Only the cookie-authenticated requests need the anti-forgery header:
        // signed bearer requests still use their existing explicit scheme.
        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method) &&
            !HttpMethods.IsTrace(context.Request.Method))
        {
            if (!BffUnsafeRequestGuard.IsSameOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.User = authenticated.Principal;
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }

        // Default bearer authentication middleware will not overwrite this
        // principal when no Authorization header is present. TenantResolution
        // and all endpoint-level live permissions still execute normally.
        context.User = authenticated.Principal;
        await _next(context);
    }
}
