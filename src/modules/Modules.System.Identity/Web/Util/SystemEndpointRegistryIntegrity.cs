using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-14C: live, read-only reconciliation of *System Admin feature endpoints*
/// with the System-owned Resource/Permission/API catalog. This deliberately
/// does not treat Login, BFF, Tenant Workspace or media protocols as features.
/// Readiness is not a grant, policy decision, or Apply authorization.
/// </summary>
public static class SystemEndpointRegistryIntegrity
{
    public sealed record EndpointStatus(
        string Key, string HttpMethod, string Route, string ResourceCode,
        string PermissionCode, string Status, IReadOnlyList<string> Issues);

    public sealed record Report(
        bool ReadyForGranularEnforcement,
        int DiscoveredFeatureEndpoints, int ReadyFeatureEndpoints,
        IReadOnlyList<EndpointStatus> Endpoints,
        IReadOnlyList<string> UncatalogedSystemAdminRoutes,
        IReadOnlyList<string> UnreviewedActiveSystemApiKeys,
        int OutOfScopeRouteCount);

    private sealed record RuntimeFeature(
        RouteEndpoint Route, FeatureEndpointMetadata Metadata,
        string Method, string Key);

    public static async Task<Report> ReadAsync(
        IIdentityDbContext db, EndpointDataSource dataSource,
        CancellationToken ct = default)
    {
        var modules = await db.Modules.AsNoTracking().ToListAsync(ct);
        var resources = await db.Resources.AsNoTracking().ToListAsync(ct);
        var permissions = await db.Permissions.AsNoTracking().ToListAsync(ct);
        var apis = await db.ApiResources.AsNoTracking().ToListAsync(ct);
        var links = await db.PermissionApiResources.AsNoTracking().ToListAsync(ct);

        var found = new List<RuntimeFeature>();
        var untagged = new List<string>();
        var outOfScope = 0;
        foreach (var route in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var meta = route.Metadata.GetMetadata<FeatureEndpointMetadata>();
            var systemAdministrator = route.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Any(x => string.Equals(x.Policy, "SystemAdministrator",
                    StringComparison.Ordinal));
            var methods = route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var rawRoute = route.RoutePattern.RawText ?? "";

            if (meta is null)
            {
                // Identity protocols, BFF, public and Tenant endpoints have
                // separate security contracts and must NOT be counted as
                // System feature coverage. But unannotated routes under the
                // SystemAdministrator policy are an explicit review failure.
                if (systemAdministrator)
                    untagged.Add(Describe(methods, rawRoute));
                else
                    outOfScope++;
                continue;
            }

            if (methods is null || methods.Count == 0)
            {
                found.Add(new RuntimeFeature(route, meta, "<missing>", meta.Key));
                continue;
            }
            foreach (var method in methods)
            {
                var key = methods.Count == 1 ? meta.Key :
                    meta.Key + "." + method.ToUpperInvariant();
                found.Add(new RuntimeFeature(route, meta, method.ToUpperInvariant(), key));
            }
        }

        var features = FeatureRegistryDefinitions.Features;
        var routeKeys = found.GroupBy(x => x.Key,
            StringComparer.OrdinalIgnoreCase).ToDictionary(
                g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var methodRoutes = found.GroupBy(x =>
            (x.Method.ToUpperInvariant(), x.Route.RoutePattern.RawText?.ToUpperInvariant() ?? ""))
            .ToDictionary(g => g.Key, g => g.Count());

        var output = new List<EndpointStatus>();
        foreach (var runtime in found.OrderBy(x => x.Key, StringComparer.Ordinal)
                     .ThenBy(x => x.Method, StringComparer.Ordinal))
        {
            var meta = runtime.Metadata;
            var issues = new List<string>();
            var routeText = runtime.Route.RoutePattern.RawText ?? "";
            if (string.IsNullOrWhiteSpace(routeText) ||
                !Enum.TryParse<Modules.System.Identity.Domain.Entities.HttpMethod>(
                    runtime.Method, true, out var httpMethod))
                issues.Add("unsupported_method_or_route");

            if (routeKeys[runtime.Key] != 1)
                issues.Add("duplicate_endpoint_key");
            if (methodRoutes[(runtime.Method, routeText.ToUpperInvariant())] != 1)
                issues.Add("duplicate_method_route");

            var definitions = features.Where(f =>
                string.Equals(f.ResourceCode, meta.ResourceCode, StringComparison.Ordinal) &&
                f.PermissionCodes.Contains(meta.PermissionCode, StringComparer.Ordinal)).ToList();
            if (definitions.Count != 1 || !runtime.Key.StartsWith("SYSTEM.", StringComparison.Ordinal))
                issues.Add("endpoint_not_code_owned");
            var feature = definitions.Count == 1 ? definitions[0] : null;

            if (runtime.Route.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                issues.Add("anonymous_system_feature");
            if (!runtime.Route.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Any(x => x.Policy == "SystemAdministrator"))
                issues.Add("system_administrator_policy_missing");
            var granular = runtime.Route.Metadata
                .GetOrderedMetadata<AuthorizationPolicy>()
                .SelectMany(x => x.Requirements)
                .OfType<SystemFeatureEndpointPermissionRequirement>()
                .Where(x => x.ResourceCode == meta.ResourceCode &&
                    x.PermissionCode == meta.PermissionCode && x.IsRegistered).ToList();
            if (granular.Count != 1)
                issues.Add("granular_requirement_missing_or_mismatched");

            var moduleRows = feature is null ? [] : modules.Where(x =>
                x.Code == feature.ModuleCode && x.IsActive).ToList();
            if (moduleRows.Count != 1) issues.Add("module_missing_or_inactive");
            var resourceRows = moduleRows.Count == 1
                ? resources.Where(x => x.ModuleId == moduleRows[0].Id &&
                    x.Code == meta.ResourceCode && x.IsActive).ToList()
                : [];
            if (resourceRows.Count != 1) issues.Add("resource_missing_or_inactive");

            var permissionRows = permissions.Where(x =>
                x.Code == meta.PermissionCode).ToList();
            if (permissionRows.Count != 1 || !permissionRows[0].IsActive ||
                resourceRows.Count != 1 ||
                permissionRows[0].ResourceId != resourceRows[0].Id)
                issues.Add("permission_missing_inactive_or_wrong_owner");
            var permission = permissionRows.Count == 1 ? permissionRows[0] : null;

            var apiRows = apis.Where(x =>
                string.Equals(x.Key, runtime.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (apiRows.Count != 1)
                issues.Add(apiRows.Count == 0 ? "api_not_registered" : "api_key_duplicate");
            var api = apiRows.Count == 1 ? apiRows[0] : null;
            if (api is not null)
            {
                if (!api.IsActive || api.IsDeprecated || api.AllowAnonymous)
                    issues.Add("api_inactive_deprecated_or_anonymous");
                if (api.Source != ApiSource.Auto || resourceRows.Count != 1 ||
                    api.ResourceId != resourceRows[0].Id)
                    issues.Add("api_not_owned_by_expected_resource");
                if (!string.Equals(api.RouteTemplate, routeText, StringComparison.Ordinal) ||
                    !Enum.TryParse<Modules.System.Identity.Domain.Entities.HttpMethod>(
                        runtime.Method, true, out var expectedMethod) ||
                    api.HttpMethod != expectedMethod)
                    issues.Add("api_route_or_verb_mismatch");
                var apiLinks = links.Where(x => x.ApiResourceId == api.Id).ToList();
                if (permission is null || apiLinks.Count != 1 ||
                    apiLinks[0].PermissionId != permission.Id)
                    issues.Add("api_permission_link_missing_or_extra");
            }

            output.Add(new EndpointStatus(runtime.Key, runtime.Method,
                routeText, meta.ResourceCode, meta.PermissionCode,
                issues.Count == 0 ? "ready" : "conflict", issues));
        }

        var knownKeys = found.Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownActive = apis.Where(x => x.IsActive && !x.IsDeprecated &&
                x.Key.StartsWith("SYSTEM.", StringComparison.OrdinalIgnoreCase) &&
                !knownKeys.Contains(x.Key))
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var uncataloged = untagged.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var ready = output.Count(x => x.Status == "ready");
        return new Report(output.Count > 0 && ready == output.Count &&
                uncataloged.Length == 0 && unknownActive.Length == 0,
            output.Count, ready, output, uncataloged,
            unknownActive, outOfScope);
    }

    private static string Describe(IReadOnlyList<string>? methods, string route) =>
        (methods is { Count: > 0 } ? string.Join(",", methods) : "<unknown>") +
        " " + route;
}
