using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Modules.System.Bff;

/// <summary>
/// First cookie-only BFF write boundary: CSRF bootstrap and current-session
/// logout. Does not implement logout-all or Identity Provider signout.
/// </summary>
public static class BffSessionLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapBffSessionLifecycleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bff/auth/csrf", GetCsrf)
            .AllowAnonymous().WithTags("BFF");

        endpoints.MapPost("/bff/auth/logout", Logout)
            .AllowAnonymous().WithTags("BFF");

        return endpoints;
    }

    private static async Task<IResult> GetCsrf(
        HttpContext context,
        BffOidcSettings settings,
        IAntiforgery antiforgery)
    {
        SetNoStore(context);
        var accessFailure = await ValidateBffSessionAsync(context, settings);
        if (accessFailure is not null)
            return accessFailure;

        var tokens = antiforgery.GetAndStoreTokens(context);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        // The request token is safe for Angular to read; it is NOT an OAuth
        // credential. The actual antiforgery validation cookie stays HttpOnly.
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            });
        return Results.NoContent();
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        BffOidcSettings settings,
        IAntiforgery antiforgery)
    {
        SetNoStore(context);
        var accessFailure = await ValidateBffSessionAsync(context, settings);
        if (accessFailure is not null)
            return accessFailure;

        if (!Security.BffUnsafeRequestGuard.IsSameOrigin(context.Request))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "invalid_antiforgery_token" });
        }

        // CookieAuthenticationHandler removes the protected server-side
        // ITicketStore session on SignOutAsync. Merely expiring a cookie is not
        // sufficient and must never be used as a substitute for this call.
        await context.SignOutAsync(BffDefaults.CookieScheme);
        context.Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions
        {
            Path = "/",
            Secure = true,
            SameSite = SameSiteMode.Lax
        });
        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateBffSessionAsync(
        HttpContext context, BffOidcSettings settings)
    {
        // Deliberately ignore the host's default bearer authentication.
        var auth = await context.AuthenticateAsync(BffDefaults.CookieScheme);
        if (!auth.Succeeded || auth.Principal is null)
            return Results.Unauthorized();

        if (!BffSessionContextValidator.TryValidateActiveSession(
                auth.Principal, auth.Properties, context.Request, settings, out _))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // The host's default authentication scheme remains native/bearer.
        // Antiforgery must bind to the validated BFF principal instead of
        // implicitly treating this browser request as anonymous.
        context.User = auth.Principal;
        return null;
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
