using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using IdentityHttpMethod = Modules.System.Identity.Domain.Entities.HttpMethod;
using Modules.System.Identity.Web.Util;
using Xunit;

public sealed class FeatureRegistrySyncTests
{
    [Fact]
    public void Feature_keys_and_permissions_are_stable_and_unambiguous()
    {
        var features = FeatureRegistryDefinitions.Features;
        Assert.Equal(3, features.Count);
        Assert.Equal(features.Count, features.Select(x => x.PageKey)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(features.Count, features.Select(x => x.Route)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var keys = features.SelectMany(x => x.PermissionCodes).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keys, x => Assert.Equal(x.ToUpperInvariant(), x));
        Assert.All(features, x => Assert.Contains(x.ReadPermission, x.PermissionCodes));
        var contract = FeatureRegistryDefinitions.ExportManifest();
        Assert.Equal(1, contract.SchemaVersion);
        Assert.Equal(features.Count, contract.Features.Count);
        Assert.Contains(contract.Features, x =>
            x.Key == "system.users" && x.Route == "/system/users" &&
            x.ReadPermission == "USERS.READ" && x.ResourceCode == "users");
        Assert.Equal(contract.Features.Count, contract.Features
            .Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Preview_has_no_writes_then_apply_is_idempotent_and_never_grants()
    {
        await using var db = await CreateDbAsync();
        var manual = new Permission(Guid.NewGuid(), "ROLES.CREATE", null, "Custom label", null)
        { RowVersion = [1], IsActive = false };
        db.Permissions.Add(manual);
        await db.SaveChangesAsync();

        var registry = new FeatureRegistrySyncService(db, Endpoints(
            Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE", "POST", "/api/System/Roles"),
            Route("SYSTEM.PERMISSIONS.CREATE", "permissions", "PERMISSIONS.CREATE",
                "POST", "/api/System/Permissions")));

        var before = await registry.PreviewAsync();
        Assert.True(before.CanApply);
        Assert.True(before.HasPending);
        Assert.Empty(db.Modules);
        Assert.Empty(db.UiResources);
        Assert.Empty(db.Menus);

        var applied = await registry.ApplyAsync();
        Assert.True(applied.CanApply);
        Assert.Single(db.Modules);
        Assert.Equal(3, db.Resources.Count());
        Assert.Equal(3, db.UiResources.Count());
        Assert.Equal(3, db.Menus.Count());
        Assert.Equal(2, db.ApiResources.Count());
        Assert.Equal(2, db.PermissionApiResources.Count());
        Assert.Equal(3, db.PermissionUiResources.Count());

        var persisted = await db.Permissions.SingleAsync(x => x.Id == manual.Id);
        Assert.Equal("Custom label", persisted.Name);
        Assert.False(persisted.IsActive);
        Assert.Null(persisted.ResourceId);
        Assert.All(db.Menus, x => Assert.NotNull(x.PermissionId));
        Assert.Empty(db.RolePermissions.IgnoreQueryFilters(["tenant"]));
        Assert.Empty(db.UserPermissions.IgnoreQueryFilters(["tenant"]));
        Assert.Empty(db.UserRoles.IgnoreQueryFilters(["tenant"]));
        Assert.Empty(db.UserGroups.IgnoreQueryFilters(["tenant"]));

        var second = await registry.PreviewAsync();
        Assert.True(second.CanApply);
        Assert.False(second.HasPending);
        Assert.All(second.Changes, x => Assert.Equal("Present", x.State));

        await registry.ApplyAsync();
        Assert.Equal(3, db.Menus.Count());
        Assert.Equal(2, db.ApiResources.Count());
    }

    [Fact]
    public async Task Third_feature_users_creates_definitions_and_menu_without_any_user_grants()
    {
        await using var db = await CreateDbAsync();
        var registry = new FeatureRegistrySyncService(db, Endpoints(
            Route("SYSTEM.USERS.PAGED", "users", "USERS.READ",
                "POST", "/api/System/Users/pagedList"),
            Route("SYSTEM.USERS.GRANT-ROLE", "users", "USERS.ASSIGN-ROLE",
                "PUT", "/api/System/Users/{id:guid}/roles/{roleId:guid}")));

        var pending = await registry.PreviewAsync();
        Assert.True(pending.CanApply);
        Assert.Contains(pending.Changes, x => x.Type == "Menu" &&
            x.Key == "/system/users" && x.State == "Create");

        await registry.ApplyAsync();
        var userFeature = FeatureRegistryDefinitions.ExportManifest().Features
            .Single(x => x.Key == "system.users");
        var menu = await db.Menus.SingleAsync(x => x.Route == userFeature.Route);
        var permission = await db.Permissions.SingleAsync(x => x.Code == "USERS.READ");
        Assert.Equal(permission.Id, menu.PermissionId);
        Assert.Equal(2, await db.ApiResources.CountAsync());
        Assert.Empty(db.UserPermissions.IgnoreQueryFilters(["tenant"]));
        Assert.Empty(db.RolePermissions.IgnoreQueryFilters(["tenant"]));
        Assert.False((await registry.PreviewAsync()).HasPending);
    }

    [Fact]
    public async Task Preexisting_users_permission_owned_by_another_resource_blocks_apply()
    {
        await using var db = await CreateDbAsync();
        var wrongResource = Guid.NewGuid();
        db.Permissions.Add(new Permission(Guid.NewGuid(), "USERS.READ",
            wrongResource, "Custom tenant users read", null) { RowVersion = [1] });
        await db.SaveChangesAsync();

        var registry = new FeatureRegistrySyncService(db, Endpoints());
        var preview = await registry.PreviewAsync();
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Changes, x => x.Type == "Permission" &&
            x.Key == "USERS.READ" && x.State == "Conflict");
        Assert.False((await registry.ApplyAsync()).CanApply);
        Assert.Empty(db.Modules);
        Assert.Empty(db.Menus);
    }

    [Fact]
    public async Task Stable_endpoint_key_survives_route_change_without_reactivating_disabled_api()
    {
        await using var db = await CreateDbAsync();
        var oldRoute = Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE",
            "POST", "/api/System/Roles");
        var first = new FeatureRegistrySyncService(db, Endpoints(oldRoute));
        await first.ApplyAsync();

        var api = await db.ApiResources.SingleAsync();
        api.IsActive = false;
        api.IsDeprecated = true;
        await db.SaveChangesAsync();

        var moved = new FeatureRegistrySyncService(db, Endpoints(
            Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE",
                "POST", "/api/System/Roles/create")));
        var preview = await moved.PreviewAsync();
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Changes,
            x => x.Key == "SYSTEM.ROLES.CREATE" && x.State == "UpdateRoute");

        await moved.ApplyAsync();
        var updated = await db.ApiResources.SingleAsync();
        Assert.Equal(api.Id, updated.Id);
        Assert.Equal("/api/System/Roles/create", updated.RouteTemplate);
        Assert.False(updated.IsActive);
        Assert.True(updated.IsDeprecated);
    }

    [Fact]
    public async Task Conflicting_manual_key_aborts_entire_apply()
    {
        await using var db = await CreateDbAsync();
        db.ApiResources.Add(new ApiResource(Guid.NewGuid(), Guid.NewGuid(),
            "SYSTEM.ROLES.CREATE", "Manual definition", null, IdentityHttpMethod.POST,
            "/manually-owned-path")
        { RowVersion = [1], Source = ApiSource.Manual });
        await db.SaveChangesAsync();

        var registry = new FeatureRegistrySyncService(db, Endpoints(
            Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE",
                "POST", "/api/System/Roles")));
        var preview = await registry.PreviewAsync();
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Changes, x => x.State == "Conflict");

        var denied = await registry.ApplyAsync();
        Assert.False(denied.CanApply);
        Assert.Empty(db.Modules);
        Assert.Empty(db.Menus);
        Assert.Empty(db.PermissionApiResources);
        Assert.Single(db.ApiResources);
    }

    [Fact]
    public async Task Duplicate_or_unknown_endpoint_metadata_fails_closed()
    {
        await using var db = await CreateDbAsync();
        var endpoint = Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE",
            "POST", "/api/System/Roles");
        var duplicate = Route("SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE",
            "POST", "/api/System/Roles/duplicate");
        var unknown = Route("SYSTEM.UNLISTED", "unlisted", "UNLISTED.WRITE",
            "POST", "/api/System/Unlisted");
        var registry = new FeatureRegistrySyncService(db, Endpoints(endpoint, duplicate, unknown));
        var result = await registry.PreviewAsync();
        Assert.False(result.CanApply);
        Assert.Contains(result.Changes, x =>
            x.Key == "SYSTEM.ROLES.CREATE" && x.State == "Conflict");
        Assert.Contains(result.Changes, x =>
            x.Key == "SYSTEM.UNLISTED" && x.State == "Conflict");
        Assert.False((await registry.ApplyAsync()).CanApply);
        Assert.Empty(db.Modules);
    }

    private static RouteEndpoint Route(
        string key, string resource, string permission, string method, string route)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(route), 0);
        builder.Metadata.Add(new FeatureEndpointMetadata(key, resource, permission, key));
        builder.Metadata.Add(new HttpMethodMetadata([method]));
        return (RouteEndpoint)builder.Build();
    }

    private static EndpointDataSource Endpoints(params Endpoint[] endpoints) =>
        new DefaultEndpointDataSource(endpoints);

    private static async Task<IdentityDbContext> CreateDbAsync()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new IdentityDbContext(options,
            new TenantContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
