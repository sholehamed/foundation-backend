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
            Assert.Equal(80, await db.UiResources.CountAsync());
            Assert.Equal(80, await db.PermissionUiResources.CountAsync());
            Assert.Equal(67, FeatureRegistryDefinitions.UiElements.Count);
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
            Assert.Equal(80, await db.UiResources.CountAsync());
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
                Assert.Equal(80, await db.UiResources.CountAsync());
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
            Assert.Equal(35, preview.Changes.Count); // module + resource + 11x(permission, UI, link)
            Assert.Empty(await db.Permissions.AsNoTracking().ToListAsync());

            var accepted = await registry.ApplyAsync(preview.Fingerprint);
            Assert.True(accepted.CanApply);
            Assert.Equal(11, await db.Permissions.CountAsync());
            Assert.Equal(11, await db.UiResources.CountAsync());
            Assert.Equal(11, await db.PermissionUiResources.CountAsync());
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
            Assert.Equal(11, await db.Permissions.CountAsync());

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
