using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Modules.System.Bff;

/// <summary>
/// Opt-in infrastructure for future BFF login/session endpoints.
/// Not registered by the Web host until the OIDC code-flow milestone is ready.
/// </summary>
public static class BffServiceCollectionExtensions
{
    public static IServiceCollection AddBffSessionInfrastructure(
        this IServiceCollection services)
    {
        services.AddDataProtection();

        // The consuming host MUST register IDistributedCache explicitly:
        // memory for single-instance local dev; shared Redis/SQL for multi-node.
        services.AddSingleton<ITicketStore, DistributedBffTicketStore>();

        // Existing OpenIddict default bearer scheme is not changed.
        services.AddAuthentication().AddCookie(BffDefaults.CookieScheme, options =>
        {
            options.Cookie.Name = BffDefaults.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);

            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            };
        });

        // Only store an opaque reference in the auth cookie, never the
        // user claims or bearer credentials.
        services.AddOptions<CookieAuthenticationOptions>(BffDefaults.CookieScheme)
            .Configure<ITicketStore>((options, tickets) =>
                options.SessionStore = tickets);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        return services;
    }
}
