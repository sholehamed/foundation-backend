using System.Security.Claims;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-14C-1: Tenant navigation is a projection of the global Menu catalog,
/// NOT a permission grant or an alternate authorization engine.
/// The live TenantWorkspacePermissionEvaluator is authoritative for the
/// three privileged Tenant PAGE roots. Unknown internal Tenant routes are
/// deliberately omitted until they have a reviewed code-owned policy.
/// </summary>
public static class TenantWorkspaceNavigationService
{
    // Exact, canonical paths from the Angular Tenant Workspace route tree.
    // Dynamic entity-detail links are intentionally NOT menu roots.
    private static readonly IReadOnlyDictionary<string, string?> KnownRoutes =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["/dashboard"] = null,
            ["/dashboard/business"] = null,
            ["/dashboard/branches"] = null,
            ["/dashboard/members"] = TenantWorkspacePermissionCatalog.MembersRead,
            ["/dashboard/roles"] = TenantWorkspacePermissionCatalog.RolesRead,
            ["/dashboard/groups"] = TenantWorkspacePermissionCatalog.GroupsRead
        };

    public static IReadOnlyDictionary<string, string?> RoutePermissions => KnownRoutes;

    public static async Task<List<CurrentNavigationCategory>?> GetAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            principal.FindFirstValue("account_type") != "tenant" ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId) ||
            principal.HasClaim(c => c.Type == "system") ||
            current.IsSystemContext || current.UserId != userId ||
            current.HomeTenantId != tenantId || current.ActiveTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return null;

        // CurrentUserNavigationService validates the exact live System/Tenant
        // account and Tenant slug, and resolves direct-deny/role/group grants
        // against the DB. No RoleId/TenantId comes from the request.
        var raw = await new CurrentUserNavigationService(db).GetAsync(principal, ct);
        if (raw is null) return null;

        // Apply the EXACT existing live tenant PAGE evaluator, which checks
        // active local memberships, role scope, direct deny and Owner fallback.
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in KnownRoutes)
        {
            if (entry.Value is null ||
                await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    current, db, entry.Value, ct))
                allowed.Add(entry.Key);
        }

        // The System Admin's single canonical Menu catalog is the only source.
        // Tenant users receive a live, permission-filtered projection.
        List<CurrentNavigationItem> Project(IReadOnlyList<CurrentNavigationItem> source)
        {
            var items = new List<CurrentNavigationItem>();
            foreach (var item in source)
            {
                // Never expose a forbidden ancestor to reveal its title or
                // hidden descendants. An empty container is not a menu item.
                if (!string.IsNullOrWhiteSpace(item.Route) &&
                    !allowed.Contains(item.Route))
                    continue;

                var children = Project(item.Childs);
                if (string.IsNullOrWhiteSpace(item.Route) &&
                    string.IsNullOrWhiteSpace(item.ExternalUrl) &&
                    children.Count == 0)
                    continue;

                items.Add(new CurrentNavigationItem(
                    item.Title, item.Icon, item.Route, item.ExternalUrl, children));
            }
            return items;
        }

        return raw.Select(category => new CurrentNavigationCategory(
                category.Title, Project(category.Items)))
            .Where(category => category.Items.Count > 0)
            .ToList();
    }
}
