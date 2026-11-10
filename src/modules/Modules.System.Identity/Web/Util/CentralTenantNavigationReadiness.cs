using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-14C: operator-only, read-only readiness check for the System-owned
/// navigation catalog. This does not create menus, repair grants, or assert
/// authorization on behalf of users. An empty/invalid catalog is NOT ready.
/// </summary>
public static class CentralTenantNavigationReadiness
{
    public sealed record RouteStatus(string Route, string? RequiredPermission,
        string Status, IReadOnlyList<string> Issues);

    public sealed record Report(bool ReadyForDynamicPilot, int RequiredRoutes,
        int ReadyRoutes, IReadOnlyList<RouteStatus> Routes,
        IReadOnlyList<string> UnregisteredTenantRoutes,
        int UngatedExternalMenuCount);

    public static async Task<Report> ReadAsync(
        IdentityDbContext db, CancellationToken ct = default)
    {
        // All input comes from the reviewed code catalog and System-owned DB.
        // No user/tenant/role IDs are accepted from callers or returned.
        var routes = TenantWorkspaceNavigationService.RoutePermissions;
        var categories = await db.MenuCategories.AsNoTracking().ToListAsync(ct);
        var menus = await db.Menus.AsNoTracking().ToListAsync(ct);
        var permissions = await db.Permissions.AsNoTracking().ToListAsync(ct);
        var resources = await db.Resources.AsNoTracking().ToListAsync(ct);
        var modules = await db.Modules.AsNoTracking().ToListAsync(ct);
        var ui = await db.UiResources.AsNoTracking().ToListAsync(ct);
        var links = await db.PermissionUiResources.AsNoTracking().ToListAsync(ct);

        var definitions = TenantWorkspacePermissionCatalog.Definitions;
        var checks = new List<RouteStatus>(routes.Count);
        foreach (var route in routes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var issues = new List<string>();
            var matches = menus.Where(x => string.Equals(x.Route, route.Key,
                StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
                issues.Add("menu_missing");
            else if (matches.Count > 1)
                issues.Add("menu_route_duplicate");

            var menu = matches.Count == 1 ? matches[0] : null;
            if (menu is not null)
            {
                if (!string.Equals(menu.Route, route.Key, StringComparison.Ordinal))
                    issues.Add("menu_route_not_canonical");
                if (!menu.IsActive || menu.IsHidden)
                    issues.Add("menu_inactive_or_hidden");
                if (!string.IsNullOrWhiteSpace(menu.ExternalUrl))
                    issues.Add("menu_external_url");
                if (categories.Count(x => x.Id == menu.CategoryId && x.IsActive) != 1)
                    issues.Add("category_missing_or_inactive");
                if (!ValidAncestors(menu, menus, routes))
                    issues.Add("parent_chain_invalid");
            }

            if (route.Value is string key)
            {
                var candidates = permissions.Where(x => x.Code == key).ToList();
                if (candidates.Count == 0)
                    issues.Add("page_permission_missing");
                else if (candidates.Count != 1)
                    issues.Add("page_permission_duplicate");

                var permission = candidates.Count == 1 ? candidates[0] : null;
                if (permission is not null)
                {
                    var ownedResource = resources.SingleOrDefault(x =>
                        x.Id == permission.ResourceId &&
                        x.Code == TenantPermissionCatalogSync.ResourceCode &&
                        x.IsActive);
                    if (!permission.IsActive || ownedResource is null ||
                        modules.Count(x => ownedResource is not null &&
                            x.Id == ownedResource.ModuleId &&
                            x.Code == TenantPermissionCatalogSync.ModuleCode &&
                            x.IsActive) != 1)
                        issues.Add("page_permission_not_centrally_owned");

                    if (menu is not null && menu.PermissionId != permission.Id)
                        issues.Add("menu_page_permission_mismatch");

                    var def = definitions.SingleOrDefault(x => x.Key == key &&
                        x.Kind == "PAGE" && x.Route == route.Key);
                    if (def is null)
                    {
                        issues.Add("page_contract_missing");
                    }
                    else
                    {
                        var pageMatches = ui.Where(x => x.Key == def.UiKey).ToList();
                        if (pageMatches.Count != 1 || !pageMatches[0].IsActive ||
                            pageMatches[0].Type != UiResourceType.PAGE ||
                            pageMatches[0].Description != route.Key ||
                            pageMatches[0].ResourceId != permission.ResourceId)
                            issues.Add("page_ui_not_registered");
                        else if (links.Count(x => x.PermissionId == permission.Id &&
                                x.UiResourceId == pageMatches[0].Id) != 1)
                            issues.Add("page_ui_permission_link_missing");
                    }
                }
            }
            else if (menu is not null && menu.PermissionId is not null)
            {
                // Base routes are deliberately unprivileged. A stale System or
                // unrelated permission would silently hide them from staff.
                issues.Add("baseline_menu_has_unreviewed_permission");
            }

            checks.Add(new RouteStatus(route.Key, route.Value,
                issues.Count == 0 ? "ready" : matches.Count == 0 ? "missing" : "conflict",
                issues));
        }

        // Routes outside the reviewed Tenant route contract are excluded by
        // the live projector. Report them so operators do not accidentally
        // believe those pages have working navigation.
        var unknown = menus.Where(x => x.IsActive && !x.IsHidden &&
                x.Route is not null &&
                x.Route.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase) &&
                !routes.ContainsKey(x.Route))
            .Select(x => x.Route!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        // Global Menu has no Tenant scope column. Ungated external links may
        // otherwise render for every Tenant, so block the pilot on this drift.
        var ungatedExternal = menus.Count(x => x.IsActive && !x.IsHidden &&
            !string.IsNullOrWhiteSpace(x.ExternalUrl) && x.PermissionId is null);
        var readyCount = checks.Count(x => x.Status == "ready");
        return new Report(readyCount == routes.Count &&
                unknown.Length == 0 && ungatedExternal == 0,
            routes.Count, readyCount, checks, unknown, ungatedExternal);
    }

    private static bool ValidAncestors(
        Menu menu, IReadOnlyList<Menu> all,
        IReadOnlyDictionary<string, string?> allowed)
    {
        var seen = new HashSet<Guid> { menu.Id };
        var parentId = menu.ParentId;
        var depth = 0;
        while (parentId is Guid id)
        {
            if (++depth > 32 || !seen.Add(id))
                return false;
            var parent = all.SingleOrDefault(x => x.Id == id);
            if (parent is null || parent.CategoryId != menu.CategoryId ||
                !parent.IsActive || parent.IsHidden ||
                (!string.IsNullOrEmpty(parent.Route) &&
                 !allowed.ContainsKey(parent.Route)) ||
                !string.IsNullOrEmpty(parent.ExternalUrl))
                return false;
            parentId = parent.ParentId;
        }
        return true;
    }
}
