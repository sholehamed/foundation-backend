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
                "FEATURE-REGISTRY.APPLY", "Apply Tenant permission catalog"))
            .MapGet(TenantNavigationReadiness, "tenant-navigation/readiness",
                configure: e => e.WithFeatureEndpoint(
                    "SYSTEM.FEATURE-REGISTRY.TENANT-NAVIGATION-READINESS",
                    "feature-registry", "FEATURE-REGISTRY.READ",
                    "Read-only central Tenant navigation readiness"))
            .MapGet(SystemEndpointIntegrity, "system-endpoints/integrity",
                configure: e => e.WithFeatureEndpoint(
                    "SYSTEM.FEATURE-REGISTRY.SYSTEM-ENDPOINT-INTEGRITY",
                    "feature-registry", "FEATURE-REGISTRY.READ",
                    "Read-only runtime System endpoint Registry integrity"));
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

    public sealed record SystemFeatureApplyRequest(string Fingerprint);

    public static async Task<IResult> Apply(
        FeatureRegistrySyncService registry,
        ITenantContext current,
        IdentityDbContext db,
        IConfiguration configuration,
        HttpContext http,
        SystemFeatureApplyRequest request,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        // Keep an independent persisted System SuperAdmin check, in addition
        // to the live SystemAdministrator policy and optional granular ACTION.
        if (!await IsLiveSystemAdministratorAsync(current, db, cancellationToken))
            return TypedResults.Forbid();

        // Default OFF in every environment, including Development.
        if (!configuration.GetValue<bool>("Identity:FeatureRegistry:EnableApply"))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Feature Registry apply is disabled");

        if (request?.Fingerprint is not { Length: 64 } fingerprint ||
            fingerprint.Any(c => !Uri.IsHexDigit(c)))
            return TypedResults.BadRequest();

        try
        {
            var preview = await registry.ApplyReviewedAsync(fingerprint, cancellationToken);
            return preview.CanApply &&
                   string.Equals(preview.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? TypedResults.Ok(preview)
                : TypedResults.Conflict(preview);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Feature Registry changed during reviewed apply",
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

    // No Apply companion. This inspects the single System-defined Menu tree
    // for the six reviewed Tenant routes; it never creates pages or grants.
    public static async Task<IResult> TenantNavigationReadiness(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(current, db, ct))
            return TypedResults.Forbid();
        return TypedResults.Ok(await CentralTenantNavigationReadiness.ReadAsync(db, ct));
    }

    // Reviewed runtime descriptors vs persisted System API/Resource/Permission
    // links. No Apply handler; this cannot mutate global definitions or grants.
    public static async Task<IResult> SystemEndpointIntegrity(
        ITenantContext current, IdentityDbContext db,
        Microsoft.AspNetCore.Routing.EndpointDataSource endpoints,
        HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(current, db, ct))
            return TypedResults.Forbid();
        return TypedResults.Ok(await
            SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints, ct));
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
