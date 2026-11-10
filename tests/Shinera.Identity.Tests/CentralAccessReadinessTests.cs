using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class CentralAccessReadinessTests
{
    private static readonly string[] Routes =
    [
        "/dashboard", "/dashboard/business", "/dashboard/branches",
        "/dashboard/members", "/dashboard/roles", "/dashboard/groups"
    ];

    [Fact]
    public void Only_four_complete_consistent_catalogs_can_pass_the_operator_gate()
    {
        var system = new FeatureRegistryPreview(true,
            [new FeatureRegistryChange("Module", "system", "Present")])
        { Fingerprint = new string('A', 64) };
        var tenant = new TenantCatalogPreview(new string('B', 64),
            true, false, [new TenantCatalogChange("Permission", "TENANT.MEMBERS.READ", "Present")]);
        var nav = new CentralTenantNavigationReadiness.Report(true, 6, 6,
            Routes.Select(r => new CentralTenantNavigationReadiness.RouteStatus(
                r, null, "ready", [])).ToArray(), [], 0);
        var endpoint = new SystemEndpointRegistryIntegrity.Report(true, 1, 1,
            [new SystemEndpointRegistryIntegrity.EndpointStatus(
                "SYSTEM.EXAMPLE", "GET", "/api/system/example",
                "example", "EXAMPLE.READ", "ready", [])],
            [], [], 0, 0);

        var good = CentralAccessReadiness.Evaluate(system, tenant, nav, endpoint);
        Assert.True(good.CentralCatalogConsistent);
        Assert.Empty(good.BlockingChecks);
        Assert.Equal(4, new[] { good.SystemRegistry.Ready,
            good.TenantPermissionCatalog.Ready, good.TenantNavigation.Ready,
            good.SystemEndpoints.Ready }.Count(x => x));
        Assert.Equal(system.Fingerprint, good.SystemRegistry.Fingerprint);
        Assert.Equal(tenant.Fingerprint, good.TenantPermissionCatalog.Fingerprint);

        var staleSystem = CentralAccessReadiness.Evaluate(
            new FeatureRegistryPreview(true,
                [new FeatureRegistryChange("ApiResource", "SYSTEM.NEW", "UpdateRoute")])
            { Fingerprint = new string('C', 64) }, tenant, nav, endpoint);
        Assert.False(staleSystem.CentralCatalogConsistent);
        Assert.Contains("system_registry_not_consistent", staleSystem.BlockingChecks);
        Assert.Equal(1, staleSystem.SystemRegistry.Pending);

        var unsafeTenant = CentralAccessReadiness.Evaluate(system,
            tenant with { Changes =
                [new TenantCatalogChange("PermissionUiResource", "foreign", "Conflict")],
                CanApply = false }, nav, endpoint);
        Assert.False(unsafeTenant.CentralCatalogConsistent);
        Assert.Contains("tenant_permission_catalog_not_consistent", unsafeTenant.BlockingChecks);
        Assert.Equal(1, unsafeTenant.TenantPermissionCatalog.Conflicts);

        var unregisteredRoute = CentralAccessReadiness.Evaluate(system, tenant,
            nav with { UnregisteredTenantRoutes = ["/dashboard/unknown"] }, endpoint);
        Assert.False(unregisteredRoute.CentralCatalogConsistent);
        Assert.Contains("tenant_navigation_not_ready", unregisteredRoute.BlockingChecks);
        Assert.Equal(1, unregisteredRoute.TenantNavigation.Conflicts);

        var unsafeEndpoints = CentralAccessReadiness.Evaluate(system, tenant,
            nav, endpoint with { UnreviewedActiveSystemApiKeys = ["SYSTEM.UNKNOWN"] });
        Assert.False(unsafeEndpoints.CentralCatalogConsistent);
        Assert.Contains("system_endpoints_not_ready", unsafeEndpoints.BlockingChecks);

        var empty = CentralAccessReadiness.Evaluate(
            new FeatureRegistryPreview(true, []) { Fingerprint = "" },
            new TenantCatalogPreview("", true, false, []),
            nav with { RequiredRoutes = 0, ReadyRoutes = 0, Routes = [] },
            endpoint with { DiscoveredFeatureEndpoints = 0,
                ReadyFeatureEndpoints = 0, Endpoints = [] });
        Assert.False(empty.CentralCatalogConsistent);
        Assert.Equal(4, empty.BlockingChecks.Count);
    }
}
