using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

public sealed class TenantNavigationOwnerWriterSqlServerTests
{
    [Fact]
    public async Task Owner_crud_versions_audit_and_cross_tenant_boundaries_are_enforced()
    {
        await WithSqlAsync(async (db, scope) =>
        {
            var (tenantId, actor, principal) = await SetupOwnerAsync(db, scope);
            var category = await TenantNavigationOwnerWriter.CreateCategoryAsync(
                scope, db, principal, new("Administration", 2), enabled: true);
            Assert.True(category.IsActive);
            Assert.NotEmpty(category.Version);
            var home = await TenantNavigationOwnerWriter.CreateItemAsync(
                scope, db, principal,
                new(category.Id, null, "Home", "home", "/dashboard", 1),
                enabled: true);
            var folder = await TenantNavigationOwnerWriter.CreateItemAsync(
                scope, db, principal,
                new(category.Id, null, "Folder", null, null, 2),
                enabled: true);
            var nested = await TenantNavigationOwnerWriter.CreateItemAsync(
                scope, db, principal,
                new(category.Id, folder.Id, "Business", "store", "/dashboard/business", 3),
                enabled: true);

            var read = await TenantNavigationOwnerWriter.ReadAsync(
                scope, db, principal, enabled: true);
            Assert.True(read.CanManage);
            Assert.Equal(3, read.Items.Count);

            await Assert.ThrowsAsync<TenantNavigationVersionConflictException>(() =>
                TenantNavigationOwnerWriter.UpdateItemAsync(
                    scope, db, principal, home.Id,
                    new(null, "Stale", "home", "/dashboard", 7, true,
                        Convert.ToBase64String(new byte[8])),
                    enabled: true));
            await Assert.ThrowsAsync<TenantNavigationInputException>(() =>
                TenantNavigationOwnerWriter.UpdateItemAsync(
                    scope, db, principal, folder.Id,
                    new(nested.Id, "Cycle", null, null, 3, true, folder.Version),
                    enabled: true));
            await Assert.ThrowsAsync<TenantNavigationInputException>(() =>
                TenantNavigationOwnerWriter.CreateItemAsync(
                    scope, db, principal,
                    new(category.Id, null, "Bad", null, "/system/admins", 1),
                    enabled: true));

            var moved = await TenantNavigationOwnerWriter.UpdateItemAsync(
                scope, db, principal, nested.Id,
                new(null, "Business updated", "store", "/dashboard/business",
                    1, true, nested.Version), enabled: true);
            Assert.NotEqual(nested.Version, moved.Version);
            Assert.Null(moved.ParentId);
            var disabled = await TenantNavigationOwnerWriter.UpdateItemAsync(
                scope, db, principal, home.Id,
                new(null, "Home", "home", "/dashboard", 1,
                    false, home.Version), enabled: true);
            Assert.False(disabled.IsActive);

            var changedCategory = await TenantNavigationOwnerWriter.UpdateCategoryAsync(
                scope, db, principal, category.Id,
                new("Reordered", 5, true, category.Version), enabled: true);
            Assert.NotEqual(category.Version, changedCategory.Version);
            await Assert.ThrowsAsync<TenantNavigationVersionConflictException>(() =>
                TenantNavigationOwnerWriter.UpdateCategoryAsync(
                    scope, db, principal, category.Id,
                    new("Old version", 5, false, category.Version), enabled: true));

            var auditCount = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantNavigationAuditEntries] " +
                "WHERE [TenantId]={0}", tenantId).SingleAsync();
            Assert.Equal(7, auditCount); // category + three items + three updates
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [dbo].[TenantNavigationAuditEntries]"));

            // An unrelated tenant ID or an actor without live Owner authority
            // cannot use the same existing object ID.
            var foreignTenant = Guid.NewGuid();
            db.Tenants.Add(new Tenant(foreignTenant, null,
                "Foreign", "owner-nav-foreign", "foreign.test"));
            await db.SaveChangesAsync();
            var mismatched = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "tenant"),
                new Claim("tenant_id", foreignTenant.ToString()),
                new Claim("tenant", "owner-nav-foreign")
            ], "test"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantNavigationOwnerWriter.ReadAsync(
                    scope, db, mismatched, enabled: true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantNavigationOwnerWriter.UpdateCategoryAsync(
                    scope, db, mismatched, category.Id,
                    new("Takeover", 2, false, changedCategory.Version),
                    enabled: true));

            var denyPermission = await db.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.NavigationManage);
            db.UserPermissions.Add(new UserPermission(Guid.NewGuid())
            {
                UserId = actor.Id, TenantId = tenantId,
                PermissionId = denyPermission.Id, IsGranted = false
            });
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantNavigationOwnerWriter.CreateCategoryAsync(
                    scope, db, principal, new("Denied", 1), enabled: true));
            Assert.Equal(7, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantNavigationAuditEntries] " +
                "WHERE [TenantId]={0}", tenantId).SingleAsync());
        });
    }

    [Fact]
    public async Task Audit_failure_rolls_back_sort_order_and_row_version()
    {
        await WithSqlAsync(async (db, scope) =>
        {
            var (_, _, principal) = await SetupOwnerAsync(db, scope);
            var initial = await TenantNavigationOwnerWriter.CreateCategoryAsync(
                scope, db, principal, new("Before", 1), enabled: true);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_TenantNavigationAudit_RejectTest]
                ON [dbo].[TenantNavigationAuditEntries] AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [Action]='category_updated')
                        THROW 51099, 'Injected navigation audit failure', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    TenantNavigationOwnerWriter.UpdateCategoryAsync(
                        scope, db, principal, initial.Id,
                        new("After", 7, false, initial.Version),
                        enabled: true));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_TenantNavigationAudit_RejectTest]");
            }

            var after = await TenantNavigationOwnerWriter.ReadAsync(
                scope, db, principal, enabled: true);
            var original = Assert.Single(after.Categories);
            Assert.Equal("Before", original.Title);
            Assert.Equal((short)1, original.SortOrder);
            Assert.True(original.IsActive);
            Assert.Equal(initial.Version, original.Version);

            var retried = await TenantNavigationOwnerWriter.UpdateCategoryAsync(
                scope, db, principal, initial.Id,
                new("After", 7, false, initial.Version),
                enabled: true);
            Assert.False(retried.IsActive);
            Assert.NotEqual(initial.Version, retried.Version);
        });
    }

    [Fact]
    public async Task An_explicit_permission_grant_to_a_non_owner_is_not_write_authority()
    {
        await WithSqlAsync(async (db, scope) =>
        {
            var (tenant, owner, ownerPrincipal) = await SetupOwnerAsync(db, scope);
            var other = new User(tenant, "nav-delegate", "delegate@nav.test",
                "Ordinary", "Member", UserType.Tenant)
            {
                NormalizedUserName = "NAV-DELEGATE",
                NormalizedEmail = "DELEGATE@NAV.TEST",
                PasswordHash = "fixture", SecurityStamp = "fixture"
            };
            var role = new Role(tenant, "menu-delegate", null)
                { Scope = RoleScope.Tenant };
            var grant = await db.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.NavigationManage);
            db.AddRange(other, role,
                new UserRole(tenant, other.Id, role.Id),
                new RolePermission(tenant, role.Id, grant.Id));
            await db.SaveChangesAsync();
            scope.InitializeTenant(other.Id, tenant, [tenant], [tenant], tenant);
            var principal = Principal(other.Id, tenant, "owned-nav");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantNavigationOwnerWriter.CreateCategoryAsync(
                    scope, db, principal, new("Delegated", 1), enabled: true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantNavigationOwnerWriter.CreateCategoryAsync(
                    scope, db, principal, new("Disabled", 1), enabled: false));

            scope.InitializeTenant(owner.Id, tenant, [tenant], [tenant], tenant);
            var category = await TenantNavigationOwnerWriter.CreateCategoryAsync(
                scope, db, ownerPrincipal, new("Owned", 1), enabled: true);
            Assert.Equal("Owned", category.Title);
        });
    }

    private static ClaimsPrincipal Principal(Guid user, Guid tenant, string slug) =>
        new(new ClaimsIdentity([
            new Claim(OpenIddictConstants.Claims.Subject, user.ToString()),
            new Claim("account_type", "tenant"),
            new Claim("tenant_id", tenant.ToString()),
            new Claim("tenant", slug)
        ], "test"));

    private static async Task<(Guid TenantId, User Actor, ClaimsPrincipal Principal)>
        SetupOwnerAsync(IdentityDbContext db, TenantContext current)
    {
        var tenant = Guid.NewGuid();
        var owner = new User(tenant, "nav-owner", "nav-owner@example.test",
            "Tenant", "Owner", UserType.Tenant)
        {
            NormalizedUserName = "NAV-OWNER",
            NormalizedEmail = "NAV-OWNER@EXAMPLE.TEST",
            PasswordHash = "fixture", SecurityStamp = "fixture"
        };
        var role = new Role(tenant, "Owner", null)
            { Scope = RoleScope.Tenant };
        var read = new Permission(Guid.NewGuid(),
            TenantWorkspacePermissionCatalog.NavigationRead, null,
            "Navigation read", null);
        var manage = new Permission(Guid.NewGuid(),
            TenantWorkspacePermissionCatalog.NavigationManage, null,
            "Navigation manage", null);
        db.AddRange(new Tenant(tenant, null, "Owned tenant",
                "owned-nav", "owned.test"),
            owner, role, read, manage,
            new UserRole(tenant, owner.Id, role.Id));
        await db.SaveChangesAsync();
        current.InitializeTenant(owner.Id, tenant, [tenant], [tenant], tenant);
        return (tenant, owner, Principal(owner.Id, tenant, "owned-nav"));
    }

    private static async Task WithSqlAsync(
        Func<IdentityDbContext, TenantContext, Task> action)
    {
        var source = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Disposable SQL connection missing.");
        var sql = new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = "TenantNavWrite_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 30
        };
        var current = new TenantContext();
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(sql.ConnectionString, x => x.EnableRetryOnFailure())
            .Options;
        await using var db = new IdentityDbContext(options, current);
        try
        {
            await db.Database.MigrateAsync();
            await action(db, current);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
