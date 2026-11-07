using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Reviewed code-owned catalog synchronization. Both endpoints inherit live
/// SystemAdministrator authorization, independent of per-resource permissions.
/// </summary>
public sealed class FeatureRegistry : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)
            .RequireAuthorization("SystemAdministrator")
            .MapGet(Preview, "preview")
            .MapPost(Apply, "apply");
    }

    public static async Task<IResult> Preview(
        FeatureRegistrySyncService registry,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await registry.PreviewAsync(cancellationToken));
    }

    public static async Task<IResult> Apply(
        FeatureRegistrySyncService registry,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        // Default OFF in every environment, including Development.
        // Operators must explicitly enable reviewed catalog registration.
        if (!configuration.GetValue<bool>("Identity:FeatureRegistry:EnableApply"))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Feature Registry apply is disabled",
                detail: "Set Identity:FeatureRegistry:EnableApply only for an approved catalog sync.");

        try
        {
            var preview = await registry.ApplyAsync(cancellationToken);
            return preview.CanApply ? TypedResults.Ok(preview)
                : TypedResults.Conflict(preview);
        }
        catch (DbUpdateException)
        {
            // Concurrency, FK or uniqueness drift may invalidate preview.
            // Do not expose target SQL schema diagnostics to API consumers.
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Feature Registry changed during apply",
                detail: "Re-run preview and review the target database.");
        }
    }
}
