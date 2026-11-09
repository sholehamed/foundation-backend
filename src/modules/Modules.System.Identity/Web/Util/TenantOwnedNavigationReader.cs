using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Read-only SQL projection of FR-14C-2's composite Tenant-owned catalog.
/// Not a Tenant menu editor and never a new permission evaluator. Ownership
/// is enforced twice: composite SQL foreign keys and a server-derived,
/// parameterized TenantId bound to the live home-Tenant principal.
/// No global Menu fallback occurs if this catalog is empty.
/// </summary>
public static class TenantOwnedNavigationReader
{
    // These unmapped SQL rows intentionally do not modify the EF runtime
    // model/snapshot. Future audited write APIs can promote them to mapped
    // entities in a separately reviewed migration with CAS and grant checks.
    public sealed class CategoryRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public short SortOrder { get; set; }
    }

    public sealed class ItemRow
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public Guid? ParentId { get; set; }
        public string Title { get; set; } = "";
        public string? Icon { get; set; }
        public string? Route { get; set; }
        public short SortOrder { get; set; }
    }

    public static async Task<List<CurrentNavigationCategory>> ReadAsync(
        IdentityDbContext db, Guid tenantId, IReadOnlySet<string> allowedRoutes,
        CancellationToken ct = default)
    {
        // Turning the pilot on without the approved SQL migration must NOT
        // expose a weaker global menu or execute an in-memory approximation.
        if (tenantId == Guid.Empty || !db.Database.IsSqlServer())
            return [];

        // TenantId must be provided by the validated live ITenantContext,
        // never from a query parameter, header, body or route.
        var parameter = new SqlParameter("@tenantId", tenantId);
        var categories = await db.Database.SqlQueryRaw<CategoryRow>(
            """
            SELECT [Id], [Title], [SortOrder]
            FROM [dbo].[TenantNavigationCategories]
            WHERE [TenantId] = @tenantId AND [IsActive] = 1
            ORDER BY [SortOrder], [Id]
            """, parameter).ToListAsync(ct);

        var items = await db.Database.SqlQueryRaw<ItemRow>(
            """
            SELECT [Id], [CategoryId], [ParentId],
                   [Title], [Icon], [Route], [SortOrder]
            FROM [dbo].[TenantNavigationItems]
            WHERE [TenantId] = @tenantId AND [IsActive] = 1
            ORDER BY [SortOrder], [Id]
            """, new SqlParameter("@tenantId", tenantId)).ToListAsync(ct);

        return Project(categories, items, allowedRoutes);
    }

    /// <summary>
    /// Pure, deterministic tree builder (separate from principal validation).
    /// Suppresses unknown/denied routes, broken parents and cycles, with a
    /// bounded depth and output. Empty categories/folders are never emitted.
    /// </summary>
    public static List<CurrentNavigationCategory> Project(
        IEnumerable<CategoryRow> categories,
        IEnumerable<ItemRow> items, IReadOnlySet<string> allowedRoutes)
    {
        var result = new List<CurrentNavigationCategory>();
        var categoryRows = categories
            .GroupBy(x => x.Id)
            .Where(x => x.Count() == 1)
            .Select(x => x.Single())
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Take(128);

        foreach (var category in categoryRows)
        {
            var categoryItems = items.Where(x => x.CategoryId == category.Id)
                .GroupBy(x => x.Id).Where(x => x.Count() == 1)
                .Select(x => x.Single()).ToList();
            // Database composite FK requires the ParentId to reference an
            // item of this same tenant/category. The projector also prunes
            // invalid snapshots rather than trusting a forged in-memory graph.
            var children = categoryItems.ToLookup(x => x.ParentId);
            var visited = new HashSet<Guid>();
            List<CurrentNavigationItem> Build(Guid? parentId, int depth)
            {
                if (depth >= 32 || visited.Count >= 1024) return [];
                var output = new List<CurrentNavigationItem>();
                foreach (var row in children[parentId]
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                {
                    if (!visited.Add(row.Id)) continue;
                    // This exact allowlist comes from the existing live
                    // TenantWorkspacePermissionEvaluator (no unscoped URL).
                    if (row.Route is not null &&
                        (!TenantWorkspaceNavigationService.RoutePermissions
                            .ContainsKey(row.Route) ||
                         !allowedRoutes.Contains(row.Route)))
                        continue;

                    var nested = Build(row.Id, depth + 1);
                    if (row.Route is null && nested.Count == 0) continue;
                    output.Add(new CurrentNavigationItem(
                        row.Title, row.Icon, row.Route, null, nested));
                }
                return output;
            }

            var roots = Build(null, 0);
            if (roots.Count > 0)
                result.Add(new CurrentNavigationCategory(category.Title, roots));
        }

        return result;
    }
}
