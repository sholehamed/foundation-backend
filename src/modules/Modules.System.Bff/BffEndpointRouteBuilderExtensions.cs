using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Modules.System.Bff;

public static class BffEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapBffEndpoints(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Identity:Bff:Enabled"))
            return endpoints;

        endpoints.MapGet("/bff/auth/login", Login)
            .AllowAnonymous()
            .WithTags("BFF");

        // Authenticate explicitly with the BFF scheme: existing bearer
        // credentials must not grant a browser session.
        endpoints.MapGet("/bff/session", GetSession)
            .AllowAnonymous()
            .WithTags("BFF");

        return endpoints;
    }

    private static IResult Login(
        HttpContext httpContext,
        BffOidcSettings settings)
    {
        SetNoStore(httpContext);

        var returnUrl = httpContext.Request.Query["returnUrl"].ToString();
        if (!Security.BffLocalReturnUrl.TryValidate(returnUrl, out var validatedReturnUrl))
            return Results.BadRequest(new { error = "invalid_return_url" });

        if (!BffHostContextResolver.TryResolve(
                httpContext.Request.Host.Host,
                settings,
                out var hostContext))
        {
            return Results.BadRequest(new { error = "unrecognized_bff_host" });
        }

        var suppliedContext = httpContext.Request.Query["login_context"].ToString();
        if (!string.IsNullOrEmpty(suppliedContext) &&
            !string.Equals(suppliedContext, hostContext.LoginContext, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var suppliedTenant = httpContext.Request.Query["tenant"].ToString();
        if (!string.IsNullOrEmpty(suppliedTenant) &&
            !string.Equals(suppliedTenant, hostContext.TenantSlug, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var callbackUri = UriHelper.BuildAbsolute(
            httpContext.Request.Scheme,
            httpContext.Request.Host,
            httpContext.Request.PathBase,
            BffDefaults.CallbackPath);

        if (!settings.IsRegisteredRedirectUri(callbackUri))
            return Results.BadRequest(new { error = "unregistered_bff_callback" });

        var properties = new AuthenticationProperties
        {
            RedirectUri = validatedReturnUrl,
            IsPersistent = false,
            AllowRefresh = true
        };
        properties.Items[BffDefaults.LoginContextProperty] = hostContext.LoginContext;
        properties.Items[BffDefaults.CallbackUriProperty] = callbackUri;
        if (hostContext.TenantSlug is not null)
            properties.Items[BffDefaults.TenantProperty] = hostContext.TenantSlug;

        return Results.Challenge(properties, [BffDefaults.OidcScheme]);
    }

    private static async Task<IResult> GetSession(
        HttpContext httpContext,
        BffOidcSettings settings)
    {
        SetNoStore(httpContext);

        // Do not use HttpContext.User: the host's default scheme is intended
        // for legacy/native bearer clients, not a BFF cookie.
        var authentication = await httpContext.AuthenticateAsync(BffDefaults.CookieScheme);
        if (!authentication.Succeeded || authentication.Principal is null)
            return Results.Unauthorized();

        if (!BffSessionContextValidator.TryValidateActiveSession(
                authentication.Principal,
                authentication.Properties,
                httpContext.Request,
                settings,
                out _))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var user = authentication.Principal;
        var accountType = user.FindFirst("account_type")!.Value;
        var username = user.FindFirst("username")?.Value;
        var displayName = user.FindFirst("given_name")?.Value ?? username;

        return Results.Ok(new
        {
            authenticated = true,
            user = new
            {
                id = user.FindFirst("sub")!.Value,
                username,
                displayName
            },
            context = new
            {
                type = accountType,
                tenantId = accountType == "tenant" ? user.FindFirst("tenant_id")?.Value : null,
                tenantSlug = accountType == "tenant" ? user.FindFirst("tenant")?.Value : null
            }
        });
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
