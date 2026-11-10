using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// Guards against returning to the abandoned, non-executable DefaultSeedData
/// lists or changing historical Master Tenant IDs during a seed refactor.
/// </summary>
public sealed class DefaultSeedDataTests
{
    [Fact]
    public void Master_storage_id_and_slug_stay_stable_but_EF_entities_are_fresh()
    {
        var first = DefaultSeedData.MasterTenant;
        var second = DefaultSeedData.CreateMasterTenant();
        Assert.Equal(DefaultSeedData.MasterTenantId, first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.NotSame(first, second);
        Assert.Equal("shinera", first.Slug);
        Assert.True(first.IsActive);

        // Do NOT reuse a tracked/mutated static Tenant object in a second
        // migration, seed run or isolated DbContext fixture.
        first.Name = "changed in a different DbContext";
        Assert.Equal("Master Tenant", DefaultSeedData.MasterTenant.Name);
    }

    [Fact]
    public void Development_admin_blueprint_is_fresh_and_cannot_be_tenant_user()
    {
        var a = DefaultSeedData.CreateDevelopmentAdmin();
        var b = DefaultSeedData.CreateDevelopmentAdmin();
        Assert.NotSame(a, b);
        Assert.Equal(UserType.System, a.UserType);
        Assert.Null(a.TenantId);
        Assert.Equal("admin", a.UserName);
        Assert.Equal("admin@master.ir", a.Email);
        Assert.Equal(DefaultSeedData.SuperAdminRoleName, "superadmin");
    }

    [Fact]
    public void Actual_System_and_Tenant_access_are_declared_in_reviewed_catalogs()
    {
        Assert.Contains(FeatureRegistryDefinitions.Features,
            x => x.Route == "/system/dashboard" &&
                 x.ReadPermission == "DASHBOARD.READ");
        Assert.Contains(FeatureRegistryDefinitions.Features,
            x => x.Route == "/system/menu-management" &&
                 x.ReadPermission == "MENU-MANAGEMENT.READ");
        Assert.Contains(FeatureRegistryDefinitions.Features,
            x => x.Route == "/system/feature-registry" &&
                 x.ReadPermission == "FEATURE-REGISTRY.READ");

        Assert.Equal(17, TenantWorkspacePermissionCatalog.Definitions.Count);
        var routes = TenantWorkspaceNavigationService.RoutePermissions;
        Assert.Equal(6, routes.Count);
        Assert.Equal(TenantWorkspacePermissionCatalog.MembersRead,
            routes["/dashboard/members"]);
        Assert.Equal(TenantWorkspacePermissionCatalog.RolesRead,
            routes["/dashboard/roles"]);
        Assert.Equal(TenantWorkspacePermissionCatalog.GroupsRead,
            routes["/dashboard/groups"]);
        Assert.Null(routes["/dashboard"]);
        Assert.Null(routes["/dashboard/business"]);
        Assert.Null(routes["/dashboard/branches"]);
        Assert.DoesNotContain(FeatureRegistryDefinitions.Features,
            x => x.Route.StartsWith("/system/admin/", StringComparison.Ordinal));
    }
}
