using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Exercises the actual SQL Server Identity migrations + FeatureRegistry
/// additive reconcile, not a fake InMemory transaction. Each test is restricted
/// to loopback/master and creates/drops a unique disposable SQL database.
/// </summary>
public sealed class FeatureRegistrySqlServerTests
{
    [Fact]
    public async Task Reviewed_System_apply_requires_fresh_fingerprint_and_atomic_real_SQL()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var registry = new FeatureRegistrySyncService(db,
                new DefaultEndpointDataSource([]));
            var preview = await registry.PreviewAsync();
            Assert.True(preview.CanApply);
            Assert.True(preview.HasPending);
            Assert.Equal(64, preview.Fingerprint.Length);

            var fake = await registry.ApplyReviewedAsync(new string('0', 64));
            Assert.NotEqual(preview.Fingerprint, new string('0', 64));
            Assert.Equal(preview.Fingerprint, fake.Fingerprint);
            Assert.Empty(await db.Modules.ToListAsync());
            Assert.Empty(await db.Menus.ToListAsync());

            // An untrusted caller cannot bypass the live Master SuperAdmin
            // check to commit the approved snapshot.
            var denied = await FeatureRegistry.Apply(
                registry, new TenantContext(), db,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> {
                        ["Identity:FeatureRegistry:EnableApply"] = "true"
                    }).Build(),
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                new FeatureRegistry.SystemFeatureApplyRequest(preview.Fingerprint),
                CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(denied);
            Assert.Empty(await db.Modules.ToListAsync());

            var accepted = await registry.ApplyReviewedAsync(preview.Fingerprint);
            Assert.True(accepted.CanApply);
            Assert.Equal(preview.Fingerprint, accepted.Fingerprint);
            Assert.Equal(13, await db.Resources.CountAsync());
            Assert.Equal(13, await db.Menus.CountAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var stable = await registry.PreviewAsync();
            Assert.True(stable.CanApply);
            Assert.False(stable.HasPending);
            Assert.NotEqual(preview.Fingerprint, stable.Fingerprint);
            var stale = await registry.ApplyReviewedAsync(preview.Fingerprint);
            Assert.NotEqual(preview.Fingerprint, stale.Fingerprint);
            Assert.Equal(13, await db.Menus.CountAsync());

            // A change to an already Present row must invalidate the token,
            // even if the change-state remains Present (no new records).
            var page = await db.UiResources.SingleAsync(x => x.Key == "system.roles");
            page.IsActive = false;
            await db.SaveChangesAsync();
            var changed = await registry.PreviewAsync();
            Assert.NotEqual(stable.Fingerprint, changed.Fingerprint);
            var rejected = await registry.ApplyReviewedAsync(stable.Fingerprint);
            Assert.NotEqual(stable.Fingerprint, rejected.Fingerprint);
            Assert.False((await db.UiResources.SingleAsync(x => x.Id == page.Id)).IsActive);
            Assert.Equal(13, await db.Menus.CountAsync());

            // Repeating the approved no-pending snapshot is idempotent.
            var repeat = await registry.ApplyReviewedAsync(changed.Fingerprint);
            Assert.False(repeat.HasPending);
            Assert.True(repeat.CanApply);
            Assert.Equal(13, await db.Menus.CountAsync());
        });
    }

    [Fact]
    public async Task Approved_apply_is_idempotent_preserves_disabled_ui_and_never_grants()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var registry = new FeatureRegistrySyncService(db, new DefaultEndpointDataSource([]));
            var pending = await registry.PreviewAsync();
            Assert.True(pending.CanApply);
            Assert.True(pending.HasPending);
            Assert.Empty(await db.Modules.AsNoTracking().ToListAsync());
            Assert.Empty(await db.UiResources.AsNoTracking().ToListAsync());

            await registry.ApplyAsync();
            Assert.Equal(13, await db.Resources.CountAsync());
            Assert.Equal(13, await db.Menus.CountAsync());
            Assert.Equal(81, await db.UiResources.CountAsync());
            Assert.Equal(81, await db.PermissionUiResources.CountAsync());
            Assert.Equal(68, FeatureRegistryDefinitions.UiElements.Count);
            foreach (var descriptor in FeatureRegistryDefinitions.UiElements)
            {
                var item = await db.UiResources.AsNoTracking().SingleAsync(x =>
                    x.Key == descriptor.Key);
                Assert.Equal(descriptor.Type, item.Type);
                Assert.Equal(descriptor.DefaultActive, item.IsActive);
                Assert.Equal(descriptor.Route, item.Description);
            }

            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var editPage = await db.UiResources.SingleAsync(x =>
                x.Key == "system.roles.edit");
            var preservedId = editPage.Id;
            editPage.IsActive = false;
            editPage.Title = "Operator-owned title";
            await db.SaveChangesAsync();

            var stable = await registry.PreviewAsync();
            Assert.True(stable.CanApply);
            Assert.False(stable.HasPending);
            await registry.ApplyAsync();

            var result = await db.UiResources.AsNoTracking().SingleAsync(x =>
                x.Id == preservedId);
            Assert.Equal("Operator-owned title", result.Title);
            Assert.False(result.IsActive);
            Assert.Equal(81, await db.UiResources.CountAsync());
            Assert.Equal(13, await db.Menus.CountAsync());
        });
    }

    [Fact]
    public async Task Approved_apply_in_outer_sql_transaction_rolls_back_all_registry_rows()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var registry = new FeatureRegistrySyncService(db,
                    new DefaultEndpointDataSource([]));
                Assert.True((await registry.PreviewAsync()).CanApply);
                await registry.ApplyAsync();
                Assert.Equal(81, await db.UiResources.CountAsync());
                Assert.Equal(13, await db.Menus.CountAsync());
                await transaction.RollbackAsync();
            }

            db.ChangeTracker.Clear();
            Assert.Empty(await db.Modules.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Resources.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Permissions.AsNoTracking().ToListAsync());
            Assert.Empty(await db.UiResources.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Menus.AsNoTracking().ToListAsync());
            Assert.Empty(await db.PermissionUiResources.AsNoTracking().ToListAsync());
        });
    }


    [Fact]
    public async Task Tenant_catalog_preview_apply_is_additive_idempotent_and_uses_real_SQL_migrations()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var registry = new TenantPermissionCatalogSync(db);
            var preview = await registry.PreviewAsync();
            Assert.True(preview.CanApply);
            Assert.True(preview.HasPending);
            Assert.Equal(53, preview.Changes.Count); // module + resource + 17x(permission, UI, link)
            Assert.Empty(await db.Permissions.AsNoTracking().ToListAsync());

            var accepted = await registry.ApplyAsync(preview.Fingerprint);
            Assert.True(accepted.CanApply);
            Assert.Equal(17, await db.Permissions.CountAsync());
            Assert.Equal(17, await db.UiResources.CountAsync());
            Assert.Equal(17, await db.PermissionUiResources.CountAsync());
            Assert.Single(await db.Modules.ToListAsync());
            Assert.Single(await db.Resources.ToListAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.Menus.ToListAsync());

            var stable = await registry.PreviewAsync();
            Assert.True(stable.CanApply);
            Assert.False(stable.HasPending);
            Assert.NotEqual(preview.Fingerprint, stable.Fingerprint);
            await registry.ApplyAsync(stable.Fingerprint);
            Assert.Equal(17, await db.Permissions.CountAsync());

            var p = await db.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.MembersRead);
            p.Name = "Operator-changed display name";
            await db.SaveChangesAsync();
            var untouched = await registry.PreviewAsync();
            Assert.True(untouched.CanApply);
            Assert.False(untouched.HasPending);
            await registry.ApplyAsync(untouched.Fingerprint);
            Assert.Equal("Operator-changed display name", (await db.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.MembersRead)).Name);
        });
    }

    [Fact]
    public async Task Tenant_catalog_collision_and_stale_fingerprint_prevent_all_writes()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var registry = new TenantPermissionCatalogSync(db);
            var preview = await registry.PreviewAsync();
            db.Permissions.Add(new Modules.System.Identity.Domain.Entities.Permission
            {
                Code = TenantWorkspacePermissionCatalog.GroupsRead,
                Name = "Unrelated unmanaged definition", IsActive = false, RowVersion = [1]
            });
            await db.SaveChangesAsync();
            var conflict = await registry.PreviewAsync();
            Assert.False(conflict.CanApply);
            Assert.Contains(conflict.Changes, x => x.State == "Conflict" &&
                x.Key == TenantWorkspacePermissionCatalog.GroupsRead);
            var stale = await registry.ApplyAsync(preview.Fingerprint);
            Assert.False(stale.CanApply);
            Assert.Empty(await db.Modules.ToListAsync());
            Assert.Single(await db.Permissions.ToListAsync());

            var disabled = await db.Permissions.SingleAsync();
            db.Permissions.Remove(disabled);
            await db.SaveChangesAsync();
            var clean = await registry.PreviewAsync();
            Assert.True(clean.CanApply);
            Assert.True(clean.HasPending);
            // Simulate a changed preview / request token: no writes occur.
            var rejected = await registry.ApplyAsync(new string('A', 64));
            Assert.True(rejected.CanApply);
            Assert.NotEqual(new string('A', 64), rejected.Fingerprint);
            Assert.Empty(await db.Modules.ToListAsync());
        });
    }

    [Fact]
    public async Task Central_tenant_menu_readiness_requires_exact_system_catalog_and_never_writes()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            // Only a live System SuperAdmin can open the read-only report.
            var deniedHttp = await FeatureRegistry.TenantNavigationReadiness(
                new TenantContext(), db, new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(deniedHttp);

            var missing = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.False(missing.ReadyForDynamicPilot);
            Assert.Equal(6, missing.RequiredRoutes);
            Assert.Equal(0, missing.ReadyRoutes);
            Assert.All(missing.Routes,
                r => Assert.Contains("menu_missing", r.Issues));
            Assert.Empty(await db.Menus.ToListAsync());

            // System Admin's permission Preview/Apply prepares definitions,
            // not menus or Tenant grants. The audit never does either.
            var sync = new TenantPermissionCatalogSync(db);
            await sync.ApplyAsync((await sync.PreviewAsync()).Fingerprint);
            Assert.Empty(await db.Menus.ToListAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var category = new Modules.System.Identity.Domain.Entities.MenuCategory(
                Guid.NewGuid())
            { Title = "Tenant dashboard", Order = 0, IsActive = true };
            db.MenuCategories.Add(category);
            var menuIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var route in TenantWorkspaceNavigationService.RoutePermissions)
            {
                var permissionId = route.Value is string key
                    ? (Guid?)await db.Permissions.Where(p => p.Code == key)
                        .Select(p => p.Id).SingleAsync()
                    : null;
                var menu = new Modules.System.Identity.Domain.Entities.Menu(Guid.NewGuid())
                {
                    CategoryId = category.Id, Title = route.Key,
                    Route = route.Key, Icon = "dashboard", Order = 0,
                    IsActive = true, IsHidden = false, PermissionId = permissionId
                };
                db.Menus.Add(menu);
                menuIds[route.Key] = menu.Id;
            }
            await db.SaveChangesAsync();

            var ready = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.True(ready.ReadyForDynamicPilot);
            Assert.Equal(6, ready.ReadyRoutes);
            Assert.All(ready.Routes, r => Assert.Equal("ready", r.Status));
            Assert.Equal(6, await db.Menus.CountAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            // Missing/inactive Page definition is detected without mutating it.
            var page = await db.UiResources.SingleAsync(u =>
                u.Key == "tenant.workspace.members");
            page.IsActive = false;
            await db.SaveChangesAsync();
            var brokenUi = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.False(brokenUi.ReadyForDynamicPilot);
            Assert.Contains("page_ui_not_registered", brokenUi.Routes.Single(r =>
                r.Route == "/dashboard/members").Issues);
            page.IsActive = true;
            await db.SaveChangesAsync();

            // A System-owned Menu pointing at a different Permission is NOT
            // treated as Tenant access even if the route string matches.
            var membersMenu = await db.Menus.SingleAsync(m =>
                m.Id == menuIds["/dashboard/members"]);
            var oldPermission = membersMenu.PermissionId;
            membersMenu.PermissionId = null;
            await db.SaveChangesAsync();
            var wrong = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.False(wrong.ReadyForDynamicPilot);
            Assert.Contains("menu_page_permission_mismatch",
                wrong.Routes.Single(r => r.Route == "/dashboard/members").Issues);
            membersMenu.PermissionId = oldPermission;
            await db.SaveChangesAsync();

            // Duplicate URL definitions are a conflict; never pick the first.
            db.Menus.Add(new Modules.System.Identity.Domain.Entities.Menu(Guid.NewGuid())
            {
                CategoryId = category.Id, Title = "Duplicated members",
                Route = "/dashboard/members", Icon = "people",
                IsActive = true, IsHidden = false, PermissionId = oldPermission
            });
            await db.SaveChangesAsync();
            var duplicated = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.Contains("menu_route_duplicate",
                duplicated.Routes.Single(r => r.Route == "/dashboard/members").Issues);
            var duplicate = await db.Menus.SingleAsync(m =>
                m.Title == "Duplicated members");
            db.Menus.Remove(duplicate);
            await db.SaveChangesAsync();

            // Unregistered Tenant route and public external Menu are visible
            // only as operator diagnostics, never silently approved.
            var extra = new Modules.System.Identity.Domain.Entities.Menu(Guid.NewGuid())
            {
                CategoryId = category.Id, Title = "Unknown route",
                Route = "/dashboard/unregistered", Icon = "warning",
                IsActive = true, IsHidden = false
            };
            var external = new Modules.System.Identity.Domain.Entities.Menu(Guid.NewGuid())
            {
                CategoryId = category.Id, Title = "External public link",
                ExternalUrl = "https://example.test", Icon = "link",
                IsActive = true, IsHidden = false
            };
            db.Menus.AddRange(extra, external);
            await db.SaveChangesAsync();
            var dirty = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.False(dirty.ReadyForDynamicPilot);
            Assert.Contains("/dashboard/unregistered", dirty.UnregisteredTenantRoutes);
            Assert.Equal(1, dirty.UngatedExternalMenuCount);
            db.Menus.RemoveRange(extra, external);
            await db.SaveChangesAsync();

            // Invalid Parent graph or inactive category must fail readiness.
            var roles = await db.Menus.SingleAsync(m =>
                m.Id == menuIds["/dashboard/roles"]);
            roles.ParentId = roles.Id;
            await db.SaveChangesAsync();
            var cycle = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.Contains("parent_chain_invalid",
                cycle.Routes.Single(r => r.Route == "/dashboard/roles").Issues);
            roles.ParentId = null;
            await db.SaveChangesAsync();
            category.IsActive = false;
            await db.SaveChangesAsync();
            Assert.False((await CentralTenantNavigationReadiness.ReadAsync(db))
                .ReadyForDynamicPilot);
        });
    }

    [Fact]
    public async Task System_endpoint_integrity_is_live_SQL_read_only_and_denies_extra_api_grants()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var builder = new Microsoft.AspNetCore.Routing.RouteEndpointBuilder(
                _ => Task.CompletedTask,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse(
                    "/api/System/Roles"), 0);
            builder.Metadata.Add(new Microsoft.AspNetCore.Routing.HttpMethodMetadata(["POST"]));
            builder.Metadata.Add(new FeatureEndpointMetadata(
                "SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE", "Create role"));
            builder.Metadata.Add(
                new Microsoft.AspNetCore.Authorization.AuthorizeAttribute("SystemAdministrator"));
            builder.Metadata.Add(
                new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new SystemFeatureEndpointPermissionRequirement("roles", "ROLES.CREATE"))
                .Build());
            var source = new DefaultEndpointDataSource([builder.Build()]);
            var empty = await SystemEndpointRegistryIntegrity.ReadAsync(db, source);
            Assert.False(empty.ReadyForGranularEnforcement);
            Assert.Empty(await db.ApiResources.ToListAsync());

            var sync = new FeatureRegistrySyncService(db, source);
            await sync.ApplyAsync();
            var clean = await SystemEndpointRegistryIntegrity.ReadAsync(db, source);
            Assert.True(clean.ReadyForGranularEnforcement);
            Assert.Single(clean.Endpoints);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var api = await db.ApiResources.SingleAsync();
            var unrelated = await db.Permissions.SingleAsync(p => p.Code == "USERS.READ");
            var link = new Modules.System.Identity.Domain.Entities.PermissionApiResource(
                unrelated.Id, api.Id) { RowVersion = [1] };
            db.PermissionApiResources.Add(link);
            await db.SaveChangesAsync();
            var expanded = await SystemEndpointRegistryIntegrity.ReadAsync(db, source);
            Assert.False(expanded.ReadyForGranularEnforcement);
            Assert.Contains("api_permission_link_missing_or_extra",
                Assert.Single(expanded.Endpoints).Issues);
            Assert.Equal(2, await db.PermissionApiResources.CountAsync());
            db.PermissionApiResources.Remove(link);
            await db.SaveChangesAsync();

            // A wrong persisted endpoint HTTP method is also a conflict even
            // when the code-owned key and permission link both exist.
            api.HttpMethod = Modules.System.Identity.Domain.Entities.HttpMethod.PUT;
            await db.SaveChangesAsync();
            var wrongVerb = await SystemEndpointRegistryIntegrity.ReadAsync(db, source);
            Assert.Contains("api_route_or_verb_mismatch",
                Assert.Single(wrongVerb.Endpoints).Issues);
            Assert.Equal(13, await db.Menus.CountAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(
                await FeatureRegistry.SystemEndpointIntegrity(
                    new TenantContext(), db, source, http, CancellationToken.None));
            Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        });
    }

    private static async Task WithIsolatedDatabaseAsync(Func<IdentityDbContext, Task> test)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "SHINERA_SQLSERVER_TEST_CONNECTION must point to disposable local SQL Server master");

        var source = new SqlConnectionStringBuilder(raw);
        if (!source.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(source.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              source.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "FeatureRegistry SQL integration tests require a loopback master test connection");

        source.InitialCatalog = "FoundationRegistryTest_" + Guid.NewGuid().ToString("N");
        source.ConnectTimeout = 30;

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(source.ConnectionString).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        try
        {
            await db.Database.MigrateAsync();
            await test(db);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
