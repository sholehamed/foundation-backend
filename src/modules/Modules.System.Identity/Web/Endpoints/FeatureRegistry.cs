using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Domain.Entities;
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
            .MapGet(Manifest, "manifest", configure: e => e.WithFeatureEndpoint("SYSTEM.FEATURE-REGISTRY.MANIFEST", "feature-registry", "FEATURE-REGISTRY.READ", "Manifest"))
            .MapGet(Preview, "preview", configure: e => e.WithFeatureEndpoint("SYSTEM.FEATURE-REGISTRY.PREVIEW", "feature-registry", "FEATURE-REGISTRY.READ", "Preview"))
            .MapPost(Apply, "apply", configure: e => e.WithFeatureEndpoint("SYSTEM.FEATURE-REGISTRY.APPLY", "feature-registry", "FEATURE-REGISTRY.APPLY", "Apply"))
            .MapGet(TenantPermissionPreview, "tenant-permissions/preview", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.FEATURE-REGISTRY.TENANT-PERMISSIONS-PREVIEW", "feature-registry",
                "FEATURE-REGISTRY.READ", "Preview Tenant permission catalog"))
            .MapPost(TenantPermissionApply, "tenant-permissions/apply", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.FEATURE-REGISTRY.TENANT-PERMISSIONS-APPLY", "feature-registry",
                "FEATURE-REGISTRY.APPLY", "Apply Tenant permission catalog"));
    }

    public static IResult Manifest(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(FeatureRegistryDefinitions.ExportManifest());
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
    // FR-12A: Same SystemAdministrator route policy as the existing registry,
    // plus a live persisted System SuperAdmin check, before any Tenant catalog read/apply.
    public sealed record TenantPermissionApplyRequest(string Fingerprint);

    private static async Task<bool> IsLiveSystemAdministratorAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!current.IsSystemContext || !current.IsSuperAdmin ||
            current.UserId is not Guid actor)
            return false;
        if (!await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == actor && x.UserType == UserType.System && x.IsActive, ct))
            return false;
        var master = DefaultSeedData.MasterTenant.Id;
        return await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(ur => ur.UserId == actor && ur.TenantId == master &&
                ur.Role.TenantId == master && ur.Role.NormalizedName == "SUPERADMIN" &&
                ur.Role.IsActive, ct);
    }

    public static async Task<IResult> TenantPermissionPreview(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(current, db, ct))
            return TypedResults.Forbid();
        return TypedResults.Ok(await new TenantPermissionCatalogSync(db).PreviewAsync(ct));
    }

    public static async Task<IResult> TenantPermissionApply(
        ITenantContext current, IdentityDbContext db, IConfiguration configuration,
        HttpContext http, TenantPermissionApplyRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(current, db, ct))
            return TypedResults.Forbid();
        // OFF by default in every environment. A reviewed enablement and preview
        // fingerprint are both required, not merely an admin UI button.
        if (!configuration.GetValue<bool>("Identity:TenantPermissions:EnableCatalogApply"))
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Tenant Permission catalog apply is disabled");
        if (request is null || request.Fingerprint?.Length != 64)
            return TypedResults.BadRequest();

        try
        {
            var result = await new TenantPermissionCatalogSync(db)
                .ApplyAsync(request.Fingerprint, ct);
            return result.CanApply &&
                   string.Equals(result.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                ? TypedResults.Ok(result) : TypedResults.Conflict(result);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Tenant Permission catalog changed; preview again");
        }
    }

}
