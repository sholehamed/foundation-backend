using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using System.Linq;

namespace Modules.System.Identity.Web.Util;

using IdentityHttpMethod = Modules.System.Identity.Domain.Entities.HttpMethod;

public sealed record FeatureRegistryChange(string Type, string Key, string State, string? Detail = null);

public sealed record FeatureRegistryPreview(bool CanApply, IReadOnlyList<FeatureRegistryChange> Changes)
{
    public bool HasPending => Changes.Any(x => x.State is "Create" or "UpdateRoute");
}

/// <summary>
/// Explicit, reviewed additive reconciliation. Preview reads DB without writes.
/// Apply never grants roles/users, never deletes or re-enables a disabled record,
/// never deprecates removed endpoints, and aborts on any key ownership collision.
/// </summary>
public sealed class FeatureRegistrySyncService(IIdentityDbContext db, EndpointDataSource endpoints)
{
    private sealed record DiscoveredApi(
        string Key, string ResourceCode, string PermissionCode,
        string Title, string Route, IdentityHttpMethod Method, bool AllowAnonymous);

    private sealed record State(
        List<Module> Modules,
        List<Resource> Resources,
        List<Permission> Permissions,
        List<UiResource> Ui,
        List<ApiResource> Api,
        List<MenuCategory> Categories,
        List<Menu> Menus,
        List<PermissionApiResource> ApiLinks,
        List<PermissionUiResource> UiLinks);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<FeatureRegistryPreview> PreviewAsync(CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        var (discovered, errors) = Discover();
        return Plan(state, discovered, errors);
    }

    /// <summary>
    /// Apply must be called only from an authenticated SystemAdministrator
    /// endpoint with the explicit deployment write flag enabled.
    /// One SaveChanges transaction inserts definitions and links.
    /// Unique SQL constraints still protect concurrent reconciliations.
    /// </summary>
    public async Task<FeatureRegistryPreview> ApplyAsync(CancellationToken ct = default)
    {
        var snapshot = await LoadAsync(ct);
        var (discovered, errors) = Discover();
        var preview = Plan(snapshot, discovered, errors);
        if (!preview.CanApply || !preview.HasPending)
            return preview;

        var modules = snapshot.Modules.ToDictionary(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        var resources = snapshot.Resources.ToDictionary(
            x => (x.ModuleId, x.Code.Trim().ToLowerInvariant()), x => x);
        var permissions = snapshot.Permissions.ToDictionary(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        var ui = snapshot.Ui.ToDictionary(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase);
        var apis = snapshot.Api.ToDictionary(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var category = snapshot.Categories.SingleOrDefault(x =>
            Same(x.Title, FeatureRegistryDefinitions.NavigationCategory));
        if (category is null)
        {
            category = new MenuCategory(Guid.NewGuid())
            {
                Title = FeatureRegistryDefinitions.NavigationCategory,
                Order = 0, IsActive = true, RowVersion = [1]
            };
            db.MenuCategories.Add(category);
        }

        foreach (var feature in FeatureRegistryDefinitions.Features)
        {
            if (!modules.TryGetValue(feature.ModuleCode, out var module))
            {
                module = new Module(Guid.NewGuid(), feature.ModuleCode, feature.ModuleTitle,
                    null, 0, true) { RowVersion = [1] };
                db.Modules.Add(module);
                modules.Add(feature.ModuleCode, module);
            }

            var identity = (module.Id, feature.ResourceCode.ToLowerInvariant());
            if (!resources.TryGetValue(identity, out var resource))
            {
                resource = new Resource(Guid.NewGuid(), module.Id,
                    feature.ResourceCode, feature.ResourceTitle, null, 0, true)
                    { RowVersion = [1] };
                db.Resources.Add(resource);
                resources.Add(identity, resource);
            }

            foreach (var code in feature.PermissionCodes)
            {
                if (permissions.ContainsKey(code))
                    continue;

                var permission = new Permission(Guid.NewGuid(), code, resource.Id,
                    code + " (code-owned)", null) { RowVersion = [1], IsActive = true };
                db.Permissions.Add(permission);
                permissions.Add(code, permission);
            }

            if (!ui.TryGetValue(feature.PageKey, out var page))
            {
                page = new UiResource(Guid.NewGuid(), resource.Id, feature.PageKey,
                    feature.PageTitle, feature.Route, UiResourceType.PAGE)
                    { RowVersion = [1], IsActive = true };
                db.UiResources.Add(page);
                ui.Add(feature.PageKey, page);
            }

            var readPermission = permissions[feature.ReadPermission];
            if (!snapshot.UiLinks.Any(x =>
                    x.PermissionId == readPermission.Id && x.UiResourceId == page.Id))
            {
                db.PermissionUiResources.Add(new PermissionUiResource(readPermission.Id, page.Id)
                    { RowVersion = [1] });
            }

            foreach (var element in FeatureRegistryDefinitions.UiElements
                .Where(x => Same(x.FeatureKey, feature.PageKey)))
            {
                if (!ui.TryGetValue(element.Key, out var component))
                {
                    // Subordinate PAGE/ACTION: no separate menu, and draft
                    // identity forms are deliberately inactive by default.
                    component = new UiResource(Guid.NewGuid(), resource.Id,
                        element.Key, element.Title, element.Route, element.Type)
                    {
                        RowVersion = [1],
                        IsActive = element.DefaultActive
                    };
                    db.UiResources.Add(component);
                    ui.Add(element.Key, component);
                }

                var required = permissions[element.PermissionCode];
                if (!snapshot.UiLinks.Any(x =>
                        x.PermissionId == required.Id && x.UiResourceId == component.Id))
                {
                    db.PermissionUiResources.Add(
                        new PermissionUiResource(required.Id, component.Id)
                        { RowVersion = [1] });
                }
            }

            if (!snapshot.Menus.Any(x => Same(x.Route, feature.Route)))
            {
                db.Menus.Add(new Menu(Guid.NewGuid())
                {
                    CategoryId = category.Id, PermissionId = readPermission.Id,
                    Title = feature.PageTitle, Icon = feature.Icon,
                    Order = feature.Order, Route = feature.Route,
                    IsActive = true, IsHidden = false, RowVersion = [1]
                });
            }
        }

        foreach (var endpoint in discovered)
        {
            var feature = FeatureRegistryDefinitions.Features
                .Single(x => Same(x.ResourceCode, endpoint.ResourceCode));
            var module = modules[feature.ModuleCode];
            var resource = resources[(module.Id, feature.ResourceCode)];
            if (!apis.TryGetValue(endpoint.Key, out var api))
            {
                api = new ApiResource(Guid.NewGuid(), resource.Id,
                    endpoint.Key, endpoint.Title, null, endpoint.Method, endpoint.Route)
                {
                    RowVersion = [1], Source = ApiSource.Auto,
                    AllowAnonymous = endpoint.AllowAnonymous,
                    IsActive = true, IsDeprecated = false
                };
                db.ApiResources.Add(api);
                apis.Add(endpoint.Key, api);
            }
            else if (api.Source == ApiSource.Auto && !Same(api.RouteTemplate, endpoint.Route))
            {
                // Only an explicitly approved Apply may update code-owned URLs.
                // Existing disabled/deprecated flags are never changed.
                var tracked = db.ApiResources.Local.FirstOrDefault(x => x.Id == api.Id);
                if (tracked is null)
                {
                    db.ApiResources.Attach(api);
                    tracked = api;
                }
                tracked.RouteTemplate = endpoint.Route;
            }

            var permission = permissions[endpoint.PermissionCode];
            if (!snapshot.ApiLinks.Any(x =>
                x.PermissionId == permission.Id && x.ApiResourceId == api.Id))
            {
                db.PermissionApiResources.Add(new PermissionApiResource(permission.Id, api.Id)
                    { RowVersion = [1] });
            }
        }

        await db.SaveChangesAsync(ct);
        return preview;
    }

    private (List<DiscoveredApi> Endpoints, List<FeatureRegistryChange> Errors) Discover()
    {
        var candidates = new List<DiscoveredApi>();
        var errors = new List<FeatureRegistryChange>();
        var definitions = FeatureRegistryDefinitions.Features;

        foreach (var route in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            var metadata = route.Metadata.GetMetadata<FeatureEndpointMetadata>();
            if (metadata is null)
                continue;
            var feature = definitions.SingleOrDefault(x => Same(x.ResourceCode, metadata.ResourceCode));
            if (feature is null || !feature.PermissionCodes.Any(x => Same(x, metadata.PermissionCode)))
            {
                errors.Add(new("ApiResource", metadata.Key, "Conflict",
                    "Unknown resource or permission code on endpoint metadata"));
                continue;
            }

            var verbs = route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (verbs is null || verbs.Count == 0 || string.IsNullOrWhiteSpace(route.RoutePattern.RawText))
            {
                errors.Add(new("ApiResource", metadata.Key, "Conflict",
                    "Endpoint must declare an HTTP method and route"));
                continue;
            }

            foreach (var verb in verbs)
            {
                if (!Enum.TryParse<IdentityHttpMethod>(verb, true, out var method))
                {
                    errors.Add(new("ApiResource", metadata.Key, "Conflict",
                        "Unsupported HTTP method " + verb));
                    continue;
                }

                // A multi-verb endpoint needs one key per HTTP method. Single
                // verb keys remain identical across route/template changes.
                var key = verbs.Count == 1 ? metadata.Key : metadata.Key + "." + verb.ToUpperInvariant();
                candidates.Add(new DiscoveredApi(key, feature.ResourceCode,
                    metadata.PermissionCode, metadata.Title,
                    route.RoutePattern.RawText!, method,
                    route.Metadata.GetMetadata<IAllowAnonymous>() is not null));
            }
        }

        foreach (var dupe in candidates.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() != 1))
        {
            errors.Add(new("ApiResource", dupe.Key, "Conflict",
                "Duplicate stable endpoint key"));
        }
        foreach (var dupe in candidates.GroupBy(x => (x.Method, x.Route.ToLowerInvariant()))
                     .Where(x => x.Count() != 1))
        {
            errors.Add(new("ApiResource", dupe.First().Key, "Conflict",
                "Multiple feature keys for the same HTTP method and route"));
        }

        return (candidates, errors);
    }

    private async Task<State> LoadAsync(CancellationToken ct) => new(
        await db.Modules.AsNoTracking().ToListAsync(ct),
        await db.Resources.AsNoTracking().ToListAsync(ct),
        await db.Permissions.AsNoTracking().ToListAsync(ct),
        await db.UiResources.AsNoTracking().ToListAsync(ct),
        await db.ApiResources.AsNoTracking().ToListAsync(ct),
        await db.MenuCategories.AsNoTracking().ToListAsync(ct),
        await db.Menus.AsNoTracking().ToListAsync(ct),
        await db.PermissionApiResources.AsNoTracking().ToListAsync(ct),
        await db.PermissionUiResources.AsNoTracking().ToListAsync(ct));

    private static FeatureRegistryPreview Plan(
        State db, IReadOnlyList<DiscoveredApi> discovered,
        IReadOnlyList<FeatureRegistryChange> errors)
    {
        var changes = new List<FeatureRegistryChange>(errors);
        void Add(string type, string key, string status, string? message = null) =>
            changes.Add(new(type, key, status, message));

        // Validate code-owned metadata before writes. Fail closed on duplicate
        // stable keys/routes and undefined parent feature or permission.
        foreach (var duplicate in FeatureRegistryDefinitions.UiElements
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1))
            Add("UiResource", duplicate.Key, "Conflict", "Duplicate code-owned UI key");

        foreach (var duplicate in FeatureRegistryDefinitions.UiElements
            .Where(x => x.Type == UiResourceType.PAGE && x.Route is not null)
            .GroupBy(x => x.Route!, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1))
            Add("UiResource", duplicate.Key, "Conflict", "Duplicate code-owned UI route");

        foreach (var element in FeatureRegistryDefinitions.UiElements)
        {
            var parent = FeatureRegistryDefinitions.Features.SingleOrDefault(x =>
                Same(x.PageKey, element.FeatureKey));
            if (parent is null ||
                !parent.PermissionCodes.Any(x => Same(x, element.PermissionCode)) ||
                (element.Type == UiResourceType.PAGE &&
                    (string.IsNullOrWhiteSpace(element.Route) ||
                     !element.Route.StartsWith("/system/", StringComparison.Ordinal))) ||
                (element.Type == UiResourceType.ACTION && element.Route is not null))
                Add("UiResource", element.Key, "Conflict",
                    "Unknown parent/permission or invalid UI type/route");
        }

        var categoryMatches = db.Categories.Where(x =>
            Same(x.Title, FeatureRegistryDefinitions.NavigationCategory)).ToList();
        Add("MenuCategory", FeatureRegistryDefinitions.NavigationCategory,
            categoryMatches.Count switch { 0 => "Create", 1 => "Present", _ => "Conflict" },
            categoryMatches.Count > 1 ? "Duplicate title; choose category manually" : null);

        foreach (var feature in FeatureRegistryDefinitions.Features)
        {
            var matches = db.Modules.Where(x => Same(x.Code, feature.ModuleCode)).ToList();
            Add("Module", feature.ModuleCode,
                matches.Count switch { 0 => "Create", 1 => "Present", _ => "Conflict" });
            var module = matches.Count == 1 ? matches[0] : null;

            var resMatches = db.Resources.Where(x => module is not null &&
                x.ModuleId == module.Id && Same(x.Code, feature.ResourceCode)).ToList();
            Add("Resource", feature.ModuleCode + "." + feature.ResourceCode,
                resMatches.Count switch { 0 => "Create", 1 => "Present", _ => "Conflict" });
            var resource = resMatches.Count == 1 ? resMatches[0] : null;

            foreach (var code in feature.PermissionCodes)
            {
                var perms = db.Permissions.Where(x => Same(x.Code, code)).ToList();
                var conflict = perms.Count > 1 ||
                    (perms.Count == 1 && perms[0].ResourceId is Guid owner &&
                     (resource is null || owner != resource.Id));
                Add("Permission", code, conflict ? "Conflict" :
                    perms.Count == 0 ? "Create" : "Present",
                    conflict ? "Permission code already belongs to another resource" : null);
            }

            var pages = db.Ui.Where(x => Same(x.Key, feature.PageKey)).ToList();
            var badPage = pages.Count > 1 ||
                (pages.Count == 1 && (pages[0].Type != UiResourceType.PAGE ||
                    resource is null || pages[0].ResourceId != resource.Id));
            Add("UiResource", feature.PageKey,
                badPage ? "Conflict" : pages.Count == 0 ? "Create" : "Present");

            var read = db.Permissions.FirstOrDefault(x => Same(x.Code, feature.ReadPermission));
            var page = pages.Count == 1 ? pages[0] : null;
            Add("PermissionUiResource", feature.ReadPermission + " -> " + feature.PageKey,
                read is not null && page is not null &&
                db.UiLinks.Any(x => x.PermissionId == read.Id && x.UiResourceId == page.Id)
                    ? "Present" : "Create");

            foreach (var element in FeatureRegistryDefinitions.UiElements
                .Where(x => Same(x.FeatureKey, feature.PageKey)))
            {
                var existing = db.Ui.Where(x => Same(x.Key, element.Key)).ToList();
                var component = existing.Count == 1 ? existing[0] : null;
                var invalidComponent = existing.Count > 1 ||
                    (component is not null &&
                     (resource is null || component.ResourceId != resource.Id ||
                      component.Type != element.Type ||
                      (element.Type == UiResourceType.PAGE &&
                       !Same(component.Description, element.Route))));
                Add("UiResource", element.Key,
                    invalidComponent ? "Conflict" :
                    component is null ? "Create" : "Present",
                    invalidComponent ? "UI resource key owned by a different resource/type/route" : null);

                var linkedPermission = db.Permissions.FirstOrDefault(x =>
                    Same(x.Code, element.PermissionCode));
                var links = component is null
                    ? new List<PermissionUiResource>()
                    : db.UiLinks.Where(x => x.UiResourceId == component.Id).ToList();
                // An unrelated existing permission link may broaden an ACTION
                // or PAGE through OR authorization. Never silently add to it.
                var unexpectedLinks = links.Any(x =>
                    linkedPermission is null || x.PermissionId != linkedPermission.Id);
                var linked = linkedPermission is not null && links.Any(x =>
                    x.PermissionId == linkedPermission.Id);
                Add("PermissionUiResource", element.PermissionCode + " -> " + element.Key,
                    unexpectedLinks ? "Conflict" : linked ? "Present" : "Create",
                    unexpectedLinks ? "Existing UI key has unrelated permission links" : null);
            }

            var menuMatches = db.Menus.Where(x => Same(x.Route, feature.Route)).ToList();
            var badMenu = menuMatches.Count > 1 ||
                (menuMatches.Count == 1 &&
                 (read is null || menuMatches[0].PermissionId != read.Id));
            Add("Menu", feature.Route,
                badMenu ? "Conflict" : menuMatches.Count == 0 ? "Create" : "Present",
                badMenu ? "Existing menu requires manual permission/route reconciliation" : null);
        }

        foreach (var endpoint in discovered)
        {
            var feature = FeatureRegistryDefinitions.Features.Single(x =>
                Same(x.ResourceCode, endpoint.ResourceCode));
            var module = db.Modules.FirstOrDefault(x => Same(x.Code, feature.ModuleCode));
            var resource = module is null ? null : db.Resources.FirstOrDefault(x =>
                x.ModuleId == module.Id && Same(x.Code, feature.ResourceCode));
            var apiMatches = db.Api.Where(x => Same(x.Key, endpoint.Key)).ToList();
            var api = apiMatches.Count == 1 ? apiMatches[0] : null;
            // Legacy URL-derived discovery may have already inserted a second
            // key for the SAME resource/method/route. The database has a unique
            // index on that tuple; report a reviewable conflict in Preview
            // instead of letting approved Apply hit a late SQL constraint.
            var legacyRouteCollision = resource is null ? null :
                db.Api.FirstOrDefault(x => x.ResourceId == resource.Id &&
                    x.HttpMethod == endpoint.Method &&
                    Same(x.RouteTemplate, endpoint.Route) &&
                    !Same(x.Key, endpoint.Key));
            var conflict = apiMatches.Count > 1 || legacyRouteCollision is not null ||
                (api is not null && (resource is null ||
                    api.ResourceId != resource.Id || api.HttpMethod != endpoint.Method ||
                    api.Source == ApiSource.Manual && !Same(api.RouteTemplate, endpoint.Route)));
            var state = conflict ? "Conflict" : api is null ? "Create" :
                !Same(api.RouteTemplate, endpoint.Route) ? "UpdateRoute" : "Present";
            Add("ApiResource", endpoint.Key, state, !conflict ? null :
                legacyRouteCollision is not null
                    ? "Existing API uses this resource/method/route with a different key; review legacy route-derived records"
                    : "Existing key owned by another resource/verb or manual definition");

            var permission = db.Permissions.FirstOrDefault(x => Same(x.Code, endpoint.PermissionCode));
            Add("PermissionApiResource", endpoint.PermissionCode + " -> " + endpoint.Key,
                permission is not null && api is not null &&
                    db.ApiLinks.Any(x => x.PermissionId == permission.Id && x.ApiResourceId == api.Id)
                        ? "Present" : "Create");
        }

        var canApply = !changes.Any(x => x.State == "Conflict");
        return new FeatureRegistryPreview(canApply, changes);
    }
}
