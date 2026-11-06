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

        return endpoints;
    }

    private static IResult Login(
        HttpContext httpContext,
        BffOidcSettings settings)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

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
            return Results.Forbid();
        }

        var suppliedTenant = httpContext.Request.Query["tenant"].ToString();
        if (!string.IsNullOrEmpty(suppliedTenant) &&
            !string.Equals(suppliedTenant, hostContext.TenantSlug, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
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
}
