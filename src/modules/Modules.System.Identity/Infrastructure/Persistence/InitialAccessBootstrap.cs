using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// First-install ONLY bootstrap. A restart, partial import, or existing catalog
/// is never permission to recreate manually revoked/disabled privileges.
/// The Master SuperAdmin role has System permissions; each Tenant's Owner role
/// receives Tenant permissions during its own server-side signup.
/// </summary>
public static class InitialAccessBootstrap
{
    public static async Task<bool> SeedFreshInstallAsync(
        IdentityDbContext db, CancellationToken ct = default)
    {
        // Master Tenant is created by DefaultIdentitySeedContributor before
        // this method. No other global catalog or roles must exist.
        // Deliberately fail CLOSED for a partial/legacy database: migration
        // and privileges require the separate reviewed reconciliation path.
        var fresh = !await db.Modules.AnyAsync(ct) &&
            !await db.Resources.AnyAsync(ct) &&
            !await db.Permissions.AnyAsync(ct) &&
            !await db.UiResources.AnyAsync(ct) &&
            !await db.ApiResources.AnyAsync(ct) &&
            !await db.MenuCategories.AnyAsync(ct) &&
            !await db.Menus.AnyAsync(ct) &&
            !await db.Roles.IgnoreQueryFilters(["tenant"]).AnyAsync(ct) &&
            !await db.RolePermissions.IgnoreQueryFilters(["tenant"]).AnyAsync(ct) &&
            !await db.UserPermissions.IgnoreQueryFilters(["tenant"]).AnyAsync(ct);

        if (!fresh) return false;

        var master = DefaultSeedData.MasterTenant.Id;
        if (await db.Tenants.CountAsync(ct) != 1 ||
            !await db.Tenants.AnyAsync(x => x.Id == master && x.IsActive, ct))
            return false;

        // The System pages, subordinate PAGE/ACTION resources and exact
        // Permission/UI links come from the existing reviewed code catalog.
        // At this early startup point endpoint mapping has not completed:
        // no API Resource metadata is guessed or fabricated.
        var sys = new FeatureRegistrySyncService(db,
            new DefaultEndpointDataSource([]));
        var system = await sys.PreviewAsync(ct);
        if (!system.CanApply || !system.HasPending)
            throw new InvalidOperationException(
                "First-install System catalog is not a clean additive preview.");
        await sys.ApplyAsync(ct);

        var tenant = new TenantPermissionCatalogSync(db);
        var tenantPreview = await tenant.PreviewAsync(ct);
        if (!tenantPreview.CanApply || !tenantPreview.HasPending)
            throw new InvalidOperationException(
                "First-install Tenant catalog is not a clean additive preview.");
        await tenant.ApplyAsync(tenantPreview.Fingerprint, ct);

        // All six menus are GLOBAL, authored by the System bootstrapping
        // catalog. They are never Tenant-editable and have exact PAGE links.
        var routes = TenantWorkspaceNavigationService.RoutePermissions;
        if (routes.Count != 6)
            throw new InvalidOperationException("Reviewed Tenant route contract changed.");
        var category = new MenuCategory(Guid.NewGuid())
        {
            Title = "فضای کاری", IsActive = true, Order = 50, RowVersion = [1]
        };
        db.MenuCategories.Add(category);

        var tenantCodes = routes.Values.OfType<string>().ToArray();
        var tenantPermissions = await db.Permissions.AsNoTracking()
            .Where(x => tenantCodes.Contains(x.Code) && x.IsActive &&
                x.Resource != null &&
                x.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                x.Resource.IsActive && x.Resource.Module != null &&
                x.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                x.Resource.Module.IsActive)
            .Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        if (tenantPermissions.Count != tenantCodes.Length ||
            tenantPermissions.Select(x => x.Code).Distinct().Count() != tenantCodes.Length)
            throw new InvalidOperationException(
                "First-install Tenant PAGE ownership is incomplete.");

        short order = 0;
        foreach (var (route, code) in routes)
        {
            db.Menus.Add(new Menu(Guid.NewGuid())
            {
                CategoryId = category.Id,
                Title = route switch
                {
                    "/dashboard" => "داشبورد",
                    "/dashboard/business" => "اطلاعات کسب‌وکار",
                    "/dashboard/branches" => "شعبه‌ها",
                    "/dashboard/members" => "اعضا",
                    "/dashboard/roles" => "نقش‌ها",
                    "/dashboard/groups" => "گروه‌ها",
                    _ => throw new InvalidOperationException("Unknown Tenant route.")
                },
                Icon = "dashboard",
                Route = route,
                PermissionId = code is null ? null :
                    tenantPermissions.Single(x => x.Code == code).Id,
                Order = order++,
                IsActive = true, IsHidden = false, RowVersion = [1]
            });
        }

        var role = new Role(master, "superadmin", "Initial System administrator")
        {
            Scope = RoleScope.System, IsActive = true
        };
        db.Roles.Add(role);

        // Grant ONLY code-owned System capabilities to the Master role.
        // Never grant a Tenant Permission to a System role, or vice versa.
        var systemCodes = FeatureRegistryDefinitions.Features
            .SelectMany(x => x.PermissionCodes).Distinct(StringComparer.Ordinal).ToArray();
        var ownedSystemPermissions = await db.Permissions.AsNoTracking()
            .Where(x => systemCodes.Contains(x.Code) && x.IsActive &&
                x.Resource != null && x.Resource.IsActive &&
                x.Resource.Module != null &&
                x.Resource.Module.Code == "system" && x.Resource.Module.IsActive)
            .Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        if (ownedSystemPermissions.Count != systemCodes.Length ||
            ownedSystemPermissions.Select(x => x.Code).Distinct().Count() != systemCodes.Length)
            throw new InvalidOperationException(
                "First-install System permission catalog is incomplete.");

        foreach (var permission in ownedSystemPermissions)
            db.RolePermissions.Add(new RolePermission(master, role.Id, permission.Id));
        await db.SaveChangesAsync(ct);

        // No production administrator with a predictable password is created.
        // Development's existing trusted administrator-account bootstrap
        // links its user to this role. Production requires separately
        // provisioned System credentials and reviewed UserRole assignment.
        var readiness = await CentralTenantNavigationReadiness.ReadAsync(db, ct);
        if (!readiness.ReadyForDynamicPilot ||
            readiness.ReadyRoutes != 6 || readiness.RequiredRoutes != 6)
            throw new InvalidOperationException(
                "First-install central Tenant navigation did not pass readiness.");

        return true;
    }
}
