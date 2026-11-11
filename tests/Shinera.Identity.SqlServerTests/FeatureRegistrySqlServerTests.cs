using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Domain.Entities;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task First_system_login_bootstrap_creates_complete_admin_once_but_never_restores_revoked_grants()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var master = DefaultSeedData.MasterTenantId;
            db.Tenants.Add(DefaultSeedData.CreateMasterTenant());
            await db.SaveChangesAsync();
            Assert.True(await InitialAccessBootstrap.SeedFreshInstallAsync(db));

            var password = "StrongFirstLogin-Admin2026!";
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:Bootstrap:InitialSystemAdmin:Enabled"] = "true",
                    ["Identity:Bootstrap:InitialSystemAdmin:UserName"] = "first-admin",
                    ["Identity:Bootstrap:InitialSystemAdmin:Email"] = "first-admin@example.test",
                    ["Identity:Bootstrap:InitialSystemAdmin:Password"] = password
                }).Build();
            var hasher = new PasswordHasher<User>();
            Assert.True(await InitialSystemAdministratorBootstrap.CreateOnceAsync(
                db, hasher, config));

            var admin = await db.Users.SingleAsync(x =>
                x.UserType == UserType.System && x.TenantId == null);
            Assert.True(admin.IsActive);
            Assert.Equal("FIRST-ADMIN", admin.NormalizedUserName);
            Assert.NotEqual(password, admin.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(admin, admin.PasswordHash, password));
            var role = await db.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.NormalizedName == "SUPERADMIN");
            Assert.Equal(master, role.TenantId);
            Assert.Equal(RoleScope.System, role.Scope);
            Assert.Single(await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .Where(x => x.TenantId == master && x.UserId == admin.Id &&
                    x.RoleId == role.Id).ToListAsync());
            Assert.Equal(15, await db.Menus.CountAsync(x =>
                x.IsActive && x.Route != null &&
                x.Route.StartsWith("/system/")));

            var systemCodes = FeatureRegistryDefinitions.Features
                .SelectMany(x => x.PermissionCodes).Distinct().Count();
            Assert.Equal(systemCodes, await db.RolePermissions
                .IgnoreQueryFilters(["tenant"])
                .CountAsync(x => x.TenantId == master && x.RoleId == role.Id));
            Assert.False(await InitialSystemAdministratorBootstrap.CreateOnceAsync(
                db, hasher, config));

            // Deactivating/revoking the only administrator must NEVER cause
            // another account to appear or a revoked UserRole to return.
            admin.IsActive = false;
            var userRole = await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.UserId == admin.Id && x.RoleId == role.Id);
            db.UserRoles.Remove(userRole);
            await db.SaveChangesAsync();
            Assert.False(await InitialSystemAdministratorBootstrap.CreateOnceAsync(
                db, hasher, config));
            Assert.Single(await db.Users.Where(x =>
                x.UserType == UserType.System).ToListAsync());
            Assert.Empty(await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .Where(x => x.UserId == admin.Id).ToListAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .Where(x => x.TenantId != master).ToListAsync());
        });
    }

    [Fact]
    public async Task Initial_system_account_rejects_weak_password_and_incomplete_role_catalog()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            db.Tenants.Add(DefaultSeedData.CreateMasterTenant());
            await db.SaveChangesAsync();
            Assert.True(await InitialAccessBootstrap.SeedFreshInstallAsync(db));
            var hasher = new PasswordHasher<User>();
            var weak = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Identity:Bootstrap:InitialSystemAdmin:Enabled"] = "true",
                    ["Identity:Bootstrap:InitialSystemAdmin:UserName"] = "new-admin",
                    ["Identity:Bootstrap:InitialSystemAdmin:Email"] = "new-admin@example.test",
                    ["Identity:Bootstrap:InitialSystemAdmin:Password"] = "123456"
                }).Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InitialSystemAdministratorBootstrap.CreateOnceAsync(db, hasher, weak));
            Assert.Empty(await db.Users.ToListAsync());

            weak["Identity:Bootstrap:InitialSystemAdmin:Password"] =
                "ValidStrongInitial+Pass2026";
            var role = await db.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.NormalizedName == "SUPERADMIN");
            var privilege = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .FirstAsync(x => x.RoleId == role.Id);
            db.RolePermissions.Remove(privilege);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InitialSystemAdministratorBootstrap.CreateOnceAsync(db, hasher, weak));
            Assert.Empty(await db.Users.ToListAsync());
        });
    }

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
            Assert.Equal(15, await db.Resources.CountAsync());
            Assert.Equal(15, await db.Menus.CountAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            var stable = await registry.PreviewAsync();
            Assert.True(stable.CanApply);
            Assert.False(stable.HasPending);
            Assert.NotEqual(preview.Fingerprint, stable.Fingerprint);
            var stale = await registry.ApplyReviewedAsync(preview.Fingerprint);
            Assert.NotEqual(preview.Fingerprint, stale.Fingerprint);
            Assert.Equal(15, await db.Menus.CountAsync());

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
            Assert.Equal(15, await db.Menus.CountAsync());

            // Repeating the approved no-pending snapshot is idempotent.
            var repeat = await registry.ApplyReviewedAsync(changed.Fingerprint);
            Assert.False(repeat.HasPending);
            Assert.True(repeat.CanApply);
            Assert.Equal(15, await db.Menus.CountAsync());
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
            Assert.Equal(15, await db.Resources.CountAsync());
            Assert.Equal(15, await db.Menus.CountAsync());
            Assert.Equal(83, await db.UiResources.CountAsync());
            Assert.Equal(83, await db.PermissionUiResources.CountAsync());
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
            Assert.Equal(83, await db.UiResources.CountAsync());
            Assert.Equal(15, await db.Menus.CountAsync());
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
                Assert.Equal(83, await db.UiResources.CountAsync());
                Assert.Equal(15, await db.Menus.CountAsync());
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
    public async Task Clean_install_seeds_all_System_and_Tenant_pages_and_role_grants_only_once()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var masterId = Modules.System.Identity.Infrastructure.Persistence
                .DefaultSeedData.MasterTenant.Id;
            db.Tenants.Add(new Modules.System.Identity.Domain.Entities.Tenant(
                masterId, null, "Master Tenant", "shinera", "shinera.ir", true));
            await db.SaveChangesAsync();

            Assert.True(await Modules.System.Identity.Infrastructure.Persistence
                .InitialAccessBootstrap.SeedFreshInstallAsync(db));

            var systemCodes = FeatureRegistryDefinitions.Features
                .SelectMany(f => f.PermissionCodes)
                .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var systemRole = await db.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.TenantId == masterId &&
                    x.NormalizedName == "SUPERADMIN");
            Assert.Equal(Modules.System.Identity.Domain.Entities.RoleScope.System,
                systemRole.Scope);
            var assigned = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .Where(x => x.TenantId == masterId && x.RoleId == systemRole.Id)
                .Select(x => x.Permission!.Code)
                .ToListAsync();
            Assert.Equal(systemCodes.Count, assigned.Count);
            Assert.True(assigned.ToHashSet(StringComparer.Ordinal).SetEquals(systemCodes));
            Assert.DoesNotContain(assigned, x => x.StartsWith("TENANT."));
            // FR-18E: a real first-install System Admin must see the page
            // AND be allowed to read traces without any manual self-grant.
            const string pageCode = "CONTROL-TOWER.READ";
            const string traceCode = "CONTROL-TOWER.TRACES.READ";
            Assert.Contains(pageCode, assigned);
            Assert.Contains(traceCode, assigned);
            var tower = await db.Resources.SingleAsync(x => x.Code == "control-tower");
            Assert.True(tower.IsActive);
            foreach (var code in new[] { pageCode, traceCode })
            {
                var permission = await db.Permissions.SingleAsync(x =>
                    x.Code == code && x.ResourceId == tower.Id && x.IsActive);
                Assert.True(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                    .AnyAsync(x => x.TenantId == masterId && x.RoleId == systemRole.Id &&
                        x.PermissionId == permission.Id));
            }
            var towerMenu = await db.Menus.SingleAsync(x =>
                x.Route == "/system/control-tower");
            Assert.True(towerMenu.IsActive);
            Assert.False(towerMenu.IsHidden);
            Assert.Equal(await db.Permissions.Where(x => x.Code == pageCode)
                .Select(x => x.Id).SingleAsync(), towerMenu.PermissionId);
            var towerPage = await db.UiResources.SingleAsync(x =>
                x.Key == "system.control-tower");
            Assert.True(towerPage.IsActive);
            Assert.Equal("/system/control-tower", towerPage.Description);
            Assert.True(await db.PermissionUiResources.AnyAsync(x =>
                x.PermissionId == towerMenu.PermissionId && x.UiResourceId == towerPage.Id));
            Assert.DoesNotContain(traceCode,
                await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                    .Where(x => x.TenantId != masterId)
                    .Select(x => x.Permission!.Code).ToListAsync());
            Assert.Equal(15, await db.Menus.CountAsync(x =>
                x.Route != null && x.Route.StartsWith("/system/")));
            Assert.Equal(6, await db.Menus.CountAsync(x =>
                x.Route != null && x.Route.StartsWith("/dashboard")));
            Assert.True((await CentralTenantNavigationReadiness.ReadAsync(db))
                .ReadyForDynamicPilot);

            var tenantCodes = Modules.System.Identity.Web.Endpoints
                .TenantWorkspacePermissionCatalog.Definitions
                .Select(x => x.Key).ToArray();
            Assert.Equal(17, await db.Permissions.CountAsync(x =>
                tenantCodes.Contains(x.Code)));
            Assert.False(await Modules.System.Identity.Infrastructure.Persistence
                .InitialAccessBootstrap.SeedFreshInstallAsync(db));
            Assert.Equal(systemCodes.Count, await db.RolePermissions
                .IgnoreQueryFilters(["tenant"])
                .CountAsync(x => x.TenantId == masterId && x.RoleId == systemRole.Id));

            // An intentionally removed System grant must NEVER reappear on
            // another startup or "seed definitions" invocation.
            var revokeCode = "CONTROL-TOWER.TRACES.READ";
            var removal = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.RoleId == systemRole.Id &&
                    x.Permission!.Code == revokeCode);
            db.RolePermissions.Remove(removal);
            await db.SaveChangesAsync();
            Assert.False(await Modules.System.Identity.Infrastructure.Persistence
                .InitialAccessBootstrap.SeedFreshInstallAsync(db));
            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.RoleId == systemRole.Id &&
                    x.Permission!.Code == revokeCode));

            // Legacy/partially populated databases are not an invitation to
            // create privileged users, import orphan permissions or re-grant.
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"])
                .ToListAsync());
            Assert.Empty(await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .ToListAsync());
        });
    }

    [Fact]
    public async Task Control_tower_live_grants_honor_direct_denies_and_no_restart_regrant()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var master = DefaultSeedData.MasterTenantId;
            db.Tenants.Add(DefaultSeedData.CreateMasterTenant());
            await db.SaveChangesAsync();
            Assert.True(await InitialAccessBootstrap.SeedFreshInstallAsync(db));
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Identity:Bootstrap:InitialSystemAdmin:Enabled"] = "true",
                    ["Identity:Bootstrap:InitialSystemAdmin:UserName"] = "tower-admin",
                    ["Identity:Bootstrap:InitialSystemAdmin:Email"] = "tower-admin@example.test",
                    ["Identity:Bootstrap:InitialSystemAdmin:Password"] = "StrongTowerBootstrap2026!"
                }).Build();
            Assert.True(await InitialSystemAdministratorBootstrap.CreateOnceAsync(
                db, new PasswordHasher<User>(), config));

            var admin = await db.Users.SingleAsync(x =>
                x.NormalizedUserName == "TOWER-ADMIN" &&
                x.UserType == UserType.System && x.TenantId == null);
            var role = await db.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.TenantId == master &&
                    x.NormalizedName == "SUPERADMIN");
            var page = await db.Permissions.SingleAsync(x =>
                x.Code == "CONTROL-TOWER.READ");
            var trace = await db.Permissions.SingleAsync(x =>
                x.Code == "CONTROL-TOWER.TRACES.READ");
            var evaluator = new EffectivePermissionEvaluator(db);
            Task<bool> Can(string code) => evaluator.HasPermissionAsync(
                admin.Id, master, UserType.System, code);

            Assert.True(await Can(page.Code));
            Assert.True(await Can(trace.Code));
            var deny = new UserPermission
            {
                TenantId = master,
                UserId = admin.Id,
                PermissionId = trace.Id,
                IsGranted = false
            };
            db.UserPermissions.Add(deny);
            await db.SaveChangesAsync();
            Assert.False(await Can(trace.Code)); // Direct deny overrides role.
            Assert.True(await Can(page.Code));

            db.UserPermissions.Remove(deny);
            await db.SaveChangesAsync();
            Assert.True(await Can(trace.Code));

            // Remove only TRACE read, not the Control Tower PAGE permission.
            var grant = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.TenantId == master &&
                    x.RoleId == role.Id && x.PermissionId == trace.Id);
            db.RolePermissions.Remove(grant);
            await db.SaveChangesAsync();
            Assert.False(await Can(trace.Code));
            Assert.True(await Can(page.Code));
            Assert.False(await InitialAccessBootstrap.SeedFreshInstallAsync(db));
            Assert.False(await InitialSystemAdministratorBootstrap.CreateOnceAsync(
                db, new PasswordHasher<User>(), config));
            Assert.False(await Can(trace.Code)); // Restart never regrants.

            admin.IsActive = false;
            await db.SaveChangesAsync();
            Assert.False(await Can(page.Code)); // Disabled user has no PAGE.
        });
    }

    [Fact]
    public async Task Consolidated_central_access_readiness_is_SQL_read_only_and_never_approves_missing_menus()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var source = new DefaultEndpointDataSource([]);
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            // Direct handler invocation still demands a live persisted
            // System SuperAdmin, not merely an endpoint policy attribute.
            var denied = await FeatureRegistry.CentralAccessReadinessReport(
                new TenantContext(), db, source, ctx, CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(denied);
            Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());

            var missing = await CentralAccessReadiness.ReadAsync(db, source);
            Assert.False(missing.CentralCatalogConsistent);
            Assert.Equal(4, missing.BlockingChecks.Count);
            Assert.Empty(await db.Modules.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Menus.AsNoTracking().ToListAsync());

            // Real isolated SQL catalog Applies are fixture setup only.
            // The aggregate must not perform any reconciliation itself.
            var systemRegistry = new FeatureRegistrySyncService(db, source);
            await systemRegistry.ApplyReviewedAsync(
                (await systemRegistry.PreviewAsync()).Fingerprint);
            var tenantCatalog = new TenantPermissionCatalogSync(db);
            await tenantCatalog.ApplyAsync((await tenantCatalog.PreviewAsync()).Fingerprint);
            var assessed = await CentralAccessReadiness.ReadAsync(db, source);
            Assert.True(assessed.SystemRegistry.Ready);
            Assert.True(assessed.TenantPermissionCatalog.Ready);
            Assert.False(assessed.TenantNavigation.Ready);
            Assert.False(assessed.SystemEndpoints.Ready);
            Assert.Contains("tenant_navigation_not_ready", assessed.BlockingChecks);
            Assert.Contains("system_endpoints_not_ready", assessed.BlockingChecks);
            Assert.False(assessed.CentralCatalogConsistent);
            Assert.Equal(15, await db.Menus.CountAsync()); // 14 System-only menus
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            var again = await CentralAccessReadiness.ReadAsync(db, source);
            Assert.Equal(assessed.CentralCatalogConsistent, again.CentralCatalogConsistent);
            Assert.Equal(assessed.BlockingChecks, again.BlockingChecks);
            Assert.Equal(assessed.SystemRegistry.Fingerprint,
                again.SystemRegistry.Fingerprint);
            Assert.Equal(assessed.TenantPermissionCatalog.Fingerprint,
                again.TenantPermissionCatalog.Fingerprint);
        });
    }

    [Fact]
    public async Task Central_access_four_way_gate_goes_green_only_for_complete_disposable_SQL_catalog()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            // Build one code-owned runtime System endpoint with the actual
            // policies and metadata required by the runtime integrity reader.
            // This is a test EndpointDataSource, not a claimed Staging host.
            var builder = new RouteEndpointBuilder(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/System/Roles"), 0);
            builder.Metadata.Add(new HttpMethodMetadata(["POST"]));
            builder.Metadata.Add(new FeatureEndpointMetadata(
                "SYSTEM.ROLES.CREATE", "roles", "ROLES.CREATE", "Create role"));
            builder.Metadata.Add(new AuthorizeAttribute("SystemAdministrator"));
            builder.Metadata.Add(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new SystemFeatureEndpointPermissionRequirement(
                    "roles", "ROLES.CREATE")).Build());
            var endpoints = new DefaultEndpointDataSource([builder.Build()]);

            // Only disposable SQL fixture setup explicitly registers
            // definitions. The checked diagnostic never invokes Apply.
            var systemRegistry = new FeatureRegistrySyncService(db, endpoints);
            var systemPending = await systemRegistry.PreviewAsync();
            Assert.True(systemPending.CanApply);
            Assert.True(systemPending.HasPending);
            await systemRegistry.ApplyReviewedAsync(systemPending.Fingerprint);
            var tenantRegistry = new TenantPermissionCatalogSync(db);
            var tenantPending = await tenantRegistry.PreviewAsync();
            Assert.True(tenantPending.CanApply);
            await tenantRegistry.ApplyAsync(tenantPending.Fingerprint);

            // Without System Admin-authored Tenant menus, an otherwise
            // healthy registry cannot certify the navigation pilot.
            var beforeMenus = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.True(beforeMenus.SystemRegistry.Ready);
            Assert.True(beforeMenus.TenantPermissionCatalog.Ready);
            Assert.True(beforeMenus.SystemEndpoints.Ready);
            Assert.False(beforeMenus.TenantNavigation.Ready);
            Assert.False(beforeMenus.CentralCatalogConsistent);
            Assert.Equal(new[] { "tenant_navigation_not_ready" }, beforeMenus.BlockingChecks);

            var category = new Modules.System.Identity.Domain.Entities.MenuCategory(
                Guid.NewGuid())
            { Title = "Tenant CI navigation", Order = 0, IsActive = true };
            db.MenuCategories.Add(category);
            foreach (var route in TenantWorkspaceNavigationService.RoutePermissions)
            {
                var permissionId = route.Value is null ? (Guid?)null :
                    await db.Permissions.Where(x => x.Code == route.Value)
                        .Select(x => (Guid?)x.Id).SingleAsync();
                db.Menus.Add(new Modules.System.Identity.Domain.Entities.Menu(
                    Guid.NewGuid())
                {
                    CategoryId = category.Id, Title = route.Key,
                    Route = route.Key, Icon = "dashboard",
                    IsActive = true, IsHidden = false,
                    PermissionId = permissionId, RowVersion = [1]
                });
            }
            await db.SaveChangesAsync();

            var green = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.True(green.CentralCatalogConsistent);
            Assert.Empty(green.BlockingChecks);
            Assert.True(green.SystemRegistry.Ready);
            Assert.True(green.TenantPermissionCatalog.Ready);
            Assert.True(green.TenantNavigation.Ready);
            Assert.True(green.SystemEndpoints.Ready);
            Assert.Equal(6, green.TenantNavigation.Observed);
            Assert.Equal(1, green.SystemEndpoints.Observed);
            Assert.Equal(20, await db.Menus.CountAsync()); // 14 System + 6 Tenant
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            // An unregistered active Tenant route must fail the navigation
            // portion; no implicit Menu repair or changes to user grants.
            var rogue = new Modules.System.Identity.Domain.Entities.Menu(Guid.NewGuid())
            {
                CategoryId = category.Id, Route = "/dashboard/unreviewed",
                Title = "Unreviewed", Icon = "warning",
                IsActive = true, IsHidden = false, RowVersion = [1]
            };
            db.Menus.Add(rogue);
            await db.SaveChangesAsync();
            var routeDrift = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.False(routeDrift.CentralCatalogConsistent);
            Assert.Contains("tenant_navigation_not_ready", routeDrift.BlockingChecks);
            db.Menus.Remove(rogue);
            await db.SaveChangesAsync();
            Assert.True((await CentralAccessReadiness.ReadAsync(db, endpoints))
                .CentralCatalogConsistent);

            // An extra PAGE permission edge broadens a navigation surface.
            // The Tenant catalog AND navigation checks must fail closed.
            var foreignPermission = await db.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.RolesRead);
            var membersUi = await db.UiResources.SingleAsync(x =>
                x.Key == "tenant.workspace.members");
            var illegalLink = new Modules.System.Identity.Domain.Entities.PermissionUiResource(
                foreignPermission.Id, membersUi.Id) { RowVersion = [1] };
            db.PermissionUiResources.Add(illegalLink);
            await db.SaveChangesAsync();
            var linkDrift = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.False(linkDrift.CentralCatalogConsistent);
            Assert.Contains("tenant_permission_catalog_not_consistent", linkDrift.BlockingChecks);
            Assert.Contains("tenant_navigation_not_ready", linkDrift.BlockingChecks);
            db.PermissionUiResources.Remove(illegalLink);
            await db.SaveChangesAsync();

            // Drift in a persisted System API row must block strict endpoint
            // consistency even with healthy System and Tenant menus.
            var api = await db.ApiResources.SingleAsync(x =>
                x.Key == "SYSTEM.ROLES.CREATE");
            api.IsActive = false;
            await db.SaveChangesAsync();
            var apiDrift = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.False(apiDrift.CentralCatalogConsistent);
            Assert.Contains("system_endpoints_not_ready", apiDrift.BlockingChecks);
            api.IsActive = true;
            await db.SaveChangesAsync();

            var restored = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.True(restored.CentralCatalogConsistent);
            Assert.Empty(restored.BlockingChecks);
            // Catalog history/row versions can change even when an
            // operator reverts a field to its previous value. A PRE-DRIFT
            // fingerprint must not be assumed reusable. Only an unchanged
            // freshly reviewed read-only snapshot may be stable.
            var reread = await CentralAccessReadiness.ReadAsync(db, endpoints);
            Assert.True(reread.CentralCatalogConsistent);
            Assert.Equal(64, restored.SystemRegistry.Fingerprint!.Length);
            Assert.Equal(64, restored.TenantPermissionCatalog.Fingerprint!.Length);
            Assert.Equal(restored.SystemRegistry.Fingerprint,
                reread.SystemRegistry.Fingerprint);
            Assert.Equal(restored.TenantPermissionCatalog.Fingerprint,
                reread.TenantPermissionCatalog.Fingerprint);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
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
    public async Task Tenant_catalog_rejects_crosslinked_PAGE_ACTION_permissions_and_stale_SQL_approval()
    {
        await WithIsolatedDatabaseAsync(async db =>
        {
            var registry = new TenantPermissionCatalogSync(db);
            await registry.ApplyAsync((await registry.PreviewAsync()).Fingerprint);
            var stable = await registry.PreviewAsync();
            Assert.True(stable.CanApply);
            Assert.False(stable.HasPending);

            var rolesPermission = await db.Permissions.SingleAsync(p =>
                p.Code == TenantWorkspacePermissionCatalog.RolesRead);
            var membersPermission = await db.Permissions.SingleAsync(p =>
                p.Code == TenantWorkspacePermissionCatalog.MembersRead);
            var membersPage = await db.UiResources.SingleAsync(p =>
                p.Key == "tenant.workspace.members");
            var properLink = await db.PermissionUiResources.SingleAsync(x =>
                x.PermissionId == membersPermission.Id &&
                x.UiResourceId == membersPage.Id);

            // One additional valid Tenant Permission on the same PAGE must
            // not be mistaken for an intact approved System-owned catalog.
            var foreign = new Modules.System.Identity.Domain.Entities.PermissionUiResource(
                rolesPermission.Id, membersPage.Id) { RowVersion = [1] };
            db.PermissionUiResources.Add(foreign);
            await db.SaveChangesAsync();
            var conflicted = await registry.PreviewAsync();
            Assert.False(conflicted.CanApply);
            Assert.NotEqual(stable.Fingerprint, conflicted.Fingerprint);
            Assert.Contains(conflicted.Changes, x =>
                x.Entity == "PermissionUiResource" && x.State == "Conflict" &&
                x.Key == TenantWorkspacePermissionCatalog.MembersRead +
                    " -> tenant.workspace.members");
            Assert.Contains(conflicted.Changes, x =>
                x.Entity == "PermissionUiResource" && x.State == "Conflict" &&
                x.Key == TenantWorkspacePermissionCatalog.RolesRead +
                    " -> tenant.workspace.roles");

            // Even an earlier authorized operator fingerprint cannot repair
            // or silently adopt the cross-link. No UI/role grants are changed.
            var denied = await registry.ApplyAsync(stable.Fingerprint);
            Assert.False(denied.CanApply);
            Assert.Equal(18, await db.PermissionUiResources.CountAsync());
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            // A foreign link is still a Conflict when the intended link is
            // missing (not a harmless Create eligible for additive Apply).
            db.PermissionUiResources.Remove(properLink);
            await db.SaveChangesAsync();
            var replaced = await registry.PreviewAsync();
            Assert.False(replaced.CanApply);
            Assert.Contains(replaced.Changes, x =>
                x.Entity == "PermissionUiResource" && x.State == "Conflict" &&
                x.Key == TenantWorkspacePermissionCatalog.MembersRead +
                    " -> tenant.workspace.members");
            Assert.Equal(17, await db.PermissionUiResources.CountAsync());

            // Explicit fixture cleanup only: never auto-delete external
            // links from any real Tenant or System catalog.
            db.PermissionUiResources.Remove(foreign);
            await db.SaveChangesAsync();
            var repairable = await registry.PreviewAsync();
            Assert.True(repairable.CanApply);
            Assert.True(repairable.HasPending);
            var applied = await registry.ApplyAsync(repairable.Fingerprint);
            Assert.True(applied.CanApply);
            var restored = await registry.PreviewAsync();
            Assert.True(restored.CanApply);
            Assert.False(restored.HasPending);
            Assert.Equal(17, await db.PermissionUiResources.CountAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
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

            // Additional, unreviewed PAGE Permission link must block dynamic
            // navigation readiness even if the canonical link still exists.
            var foreignPagePermission = await db.Permissions.SingleAsync(p =>
                p.Code == TenantWorkspacePermissionCatalog.RolesRead);
            var membersPage = await db.UiResources.SingleAsync(u =>
                u.Key == "tenant.workspace.members");
            var extraPageLink = new Modules.System.Identity.Domain.Entities.PermissionUiResource(
                foreignPagePermission.Id, membersPage.Id) { RowVersion = [1] };
            db.PermissionUiResources.Add(extraPageLink);
            await db.SaveChangesAsync();
            var crosslinked = await CentralTenantNavigationReadiness.ReadAsync(db);
            Assert.False(crosslinked.ReadyForDynamicPilot);
            Assert.Contains("page_ui_permission_link_conflict",
                crosslinked.Routes.Single(r => r.Route == "/dashboard/members").Issues);
            db.PermissionUiResources.Remove(extraPageLink);
            await db.SaveChangesAsync();
            Assert.True((await CentralTenantNavigationReadiness.ReadAsync(db))
                .ReadyForDynamicPilot);

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
            Assert.Equal(15, await db.Menus.CountAsync());
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
