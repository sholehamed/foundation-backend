using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Read-only System SuperAdmin central registry gate. This is a consistency
/// snapshot, NEVER an Apply approval, BFF policy, deployment certificate or
/// permission to activate the Tenant navigation pilot.
/// </summary>
public static class CentralAccessReadiness
{
    public sealed record Check(bool Ready, int Conflicts, int Pending,
        int Observed, string? Fingerprint = null);

    public sealed record Report(
        bool CentralCatalogConsistent,
        IReadOnlyList<string> BlockingChecks,
        Check SystemRegistry,
        Check TenantPermissionCatalog,
        Check TenantNavigation,
        Check SystemEndpoints);

    public static async Task<Report> ReadAsync(
        IdentityDbContext db, EndpointDataSource endpoints,
        CancellationToken ct = default)
    {
        // One DbContext cannot execute concurrent EF queries. Read each
        // existing authoritative diagnostic without any write/Apply.
        var system = await new FeatureRegistrySyncService(db, endpoints)
            .PreviewAsync(ct);
        var tenant = await new TenantPermissionCatalogSync(db).PreviewAsync(ct);
        var navigation = await CentralTenantNavigationReadiness.ReadAsync(db, ct);
        var runtime = await SystemEndpointRegistryIntegrity.ReadAsync(db, endpoints, ct);
        return Evaluate(system, tenant, navigation, runtime);
    }

    public static Report Evaluate(
        FeatureRegistryPreview system,
        TenantCatalogPreview tenant,
        CentralTenantNavigationReadiness.Report navigation,
        SystemEndpointRegistryIntegrity.Report runtime)
    {
        var systemConflict = system.Changes.Count(x => x.State == "Conflict");
        var systemPending = system.Changes.Count(x =>
            x.State is "Create" or "UpdateRoute");
        var tenantConflict = tenant.Changes.Count(x => x.State == "Conflict");
        var tenantPending = tenant.Changes.Count(x => x.State == "Create");
        var navConflict = navigation.Routes.Count(x => x.Status != "ready") +
            navigation.UnregisteredTenantRoutes.Count +
            navigation.UngatedExternalMenuCount;
        var endpointConflict = runtime.Endpoints.Count(x => x.Status != "ready") +
            runtime.UncatalogedSystemAdminRoutes.Count +
            runtime.UnreviewedActiveSystemApiKeys.Count;

        // Empty or structurally incomplete reports must never pass as green.
        var systemReady = system.CanApply && !system.HasPending &&
            system.Changes.Count > 0 && systemConflict == 0 && systemPending == 0;
        var tenantReady = tenant.CanApply && !tenant.HasPending &&
            tenant.Changes.Count > 0 && tenantConflict == 0 && tenantPending == 0;
        var navigationReady = navigation.ReadyForDynamicPilot &&
            navigation.RequiredRoutes == 6 && navigation.ReadyRoutes == 6 &&
            navigation.Routes.Count == 6 && navConflict == 0;
        var endpointReady = runtime.ReadyForGranularEnforcement &&
            runtime.DiscoveredFeatureEndpoints > 0 &&
            runtime.ReadyFeatureEndpoints == runtime.DiscoveredFeatureEndpoints &&
            endpointConflict == 0;

        var blocking = new List<string>(4);
        if (!systemReady) blocking.Add("system_registry_not_consistent");
        if (!tenantReady) blocking.Add("tenant_permission_catalog_not_consistent");
        if (!navigationReady) blocking.Add("tenant_navigation_not_ready");
        if (!endpointReady) blocking.Add("system_endpoints_not_ready");

        return new Report(blocking.Count == 0, blocking,
            new Check(systemReady, systemConflict, systemPending,
                system.Changes.Count, system.Fingerprint),
            new Check(tenantReady, tenantConflict, tenantPending,
                tenant.Changes.Count, tenant.Fingerprint),
            new Check(navigationReady, navConflict,
                navigation.RequiredRoutes - navigation.ReadyRoutes,
                navigation.RequiredRoutes),
            new Check(endpointReady, endpointConflict,
                runtime.DiscoveredFeatureEndpoints - runtime.ReadyFeatureEndpoints,
                runtime.DiscoveredFeatureEndpoints));
    }
}
