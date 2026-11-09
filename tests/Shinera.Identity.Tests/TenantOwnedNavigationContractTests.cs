using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class TenantOwnedNavigationContractTests
{
    private static TenantOwnedNavigationReader.CategoryRow Category(Guid? id = null,
        string title = "Workspace", short order = 0) => new()
    { Id = id ?? Guid.NewGuid(), Title = title, SortOrder = order };

    private static TenantOwnedNavigationReader.ItemRow Item(
        Guid categoryId, string title, string? route = null,
        Guid? parentId = null, short order = 0) => new()
    {
        Id = Guid.NewGuid(), CategoryId = categoryId,
        Title = title, Route = route, ParentId = parentId, SortOrder = order
    };

    [Fact]
    public void Owned_navigation_is_exactly_code_owned_and_permission_filtered()
    {
        var cat = Category();
        var folder = Item(cat.Id, "Admin folder", order: 1);
        var home = Item(cat.Id, "Home", "/dashboard", order: 0);
        var members = Item(cat.Id, "Members", "/dashboard/members", folder.Id);
        var roles = Item(cat.Id, "Roles", "/dashboard/roles", folder.Id);
        var unknown = Item(cat.Id, "Fake admin", "/dashboard/unknown", folder.Id);
        var system = Item(cat.Id, "System", "/system/admins");
        var orphan = Item(cat.Id, "Orphan", "/dashboard/branches", Guid.NewGuid());
        var items = new[] { folder, home, members, roles, unknown, system, orphan };

        var restricted = TenantOwnedNavigationReader.Project(
            [cat], items, new HashSet<string>(StringComparer.Ordinal)
            { "/dashboard", "/dashboard/members" });
        var main = Assert.Single(restricted);
        Assert.Equal(new[] { "Home", "Admin folder" },
            main.Items.Select(x => x.Title));
        Assert.Equal("Members", Assert.Single(main.Items[1].Childs).Title);
        Assert.Null(main.Items[1].ExternalUrl);

        // Revocation removes the link and its now-empty folder; no unscoped
        // global navigation or permissionless parent is synthesized.
        var revoked = TenantOwnedNavigationReader.Project(
            [cat], items, new HashSet<string>(StringComparer.Ordinal)
            { "/dashboard" });
        Assert.Equal("Home", Assert.Single(Assert.Single(revoked).Items).Title);

        Assert.Empty(TenantOwnedNavigationReader.Project(
            [cat], items, new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Malformed_cycles_duplicate_ids_and_cross_category_parent_fail_closed()
    {
        var a = Category(title: "Local");
        var b = Category(title: "Other");
        var cyclicA = Item(a.Id, "Cycle A", "/dashboard");
        var cyclicB = Item(a.Id, "Cycle B", "/dashboard/business");
        cyclicA.ParentId = cyclicB.Id;
        cyclicB.ParentId = cyclicA.Id;

        var cross = Item(b.Id, "Foreign parent", "/dashboard/branches", cyclicA.Id);
        var item = Item(a.Id, "Unique", "/dashboard/members");
        var duplicate = Item(a.Id, "Duplicate", "/dashboard/roles");
        duplicate.Id = item.Id;
        var active = TenantOwnedNavigationReader.Project(
            [a, b], [cyclicA, cyclicB, cross, item, duplicate],
            new HashSet<string> { "/dashboard", "/dashboard/business",
                "/dashboard/branches", "/dashboard/members", "/dashboard/roles" });
        Assert.Empty(active);
    }

    [Fact]
    public void Navigation_catalog_has_a_finite_reviewed_route_contract()
    {
        var expected = new[] {
            "/dashboard", "/dashboard/business", "/dashboard/branches",
            "/dashboard/members", "/dashboard/roles", "/dashboard/groups"
        };
        Assert.Equal(expected.Order(),
            TenantWorkspaceNavigationService.RoutePermissions.Keys.Order());
        Assert.Equal("TENANT.MEMBERS.READ",
            TenantWorkspaceNavigationService.RoutePermissions["/dashboard/members"]);
        Assert.Null(TenantWorkspaceNavigationService.RoutePermissions["/dashboard"]);
    }
}
