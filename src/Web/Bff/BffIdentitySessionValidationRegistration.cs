using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.System.Bff;

namespace Server.Bff;

/// <summary>
/// Host-only integration: BFF remains independent of Identity persistence.
/// Registration is explicit and fail-closed when BFF is actually enabled.
/// </summary>
public static class BffIdentitySessionValidationRegistration
{
    private const string StampProperty = ".bff.identity_stamp";
    private const string GrantsProperty = ".bff.identity_grants";

    public static IServiceCollection AddBffIdentitySessionValidation(
        this IServiceCollection services)
    {
        services.AddScoped<IBffSessionAccessValidator,
            IdentityBffSessionAccessValidator>();

        services.PostConfigure<OpenIdConnectOptions>(BffDefaults.OidcScheme, options =>
        {
            var original = options.Events.OnTicketReceived;
            options.Events.OnTicketReceived = async context =>
            {
                await original(context);
                if (context.Response.StatusCode >= 400)
                    return;

                var validator = context.HttpContext.RequestServices
                    .GetRequiredService<IBffSessionAccessValidator>();
                var snapshot = context.Principal is null
                    ? null
                    : await validator.CaptureAsync(
                        context.Principal, context.HttpContext.RequestAborted);
                if (snapshot is null || context.Properties is null)
                {
                    context.HandleResponse();
                    context.Response.StatusCode = 401;
                    context.Response.Headers.CacheControl = "no-store";
                    return;
                }

                context.Properties.Items[StampProperty] = snapshot.SecurityStamp;
                context.Properties.Items[GrantsProperty] = snapshot.GrantsFingerprint;
            };
        });

        services.PostConfigure<CookieAuthenticationOptions>(BffDefaults.CookieScheme, options =>
        {
            var original = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                await original(context);
                if (context.Principal?.Identity?.IsAuthenticated != true)
                    return;

                if (!context.Properties.Items.TryGetValue(StampProperty, out var stamp) ||
                    !context.Properties.Items.TryGetValue(GrantsProperty, out var grants))
                {
                    await RevokeInvalidTicketAsync(context);
                    return;
                }

                var validator = context.HttpContext.RequestServices
                    .GetRequiredService<IBffSessionAccessValidator>();
                var valid = await validator.IsCurrentAsync(context.Principal,
                    new BffSessionAccessSnapshot(stamp ?? "", grants ?? ""),
                    context.HttpContext.RequestAborted);
                if (!valid)
                    await RevokeInvalidTicketAsync(context);
            };
        });
        return services;
    }

    private static async Task RevokeInvalidTicketAsync(
        CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(BffDefaults.CookieScheme);
    }
}
