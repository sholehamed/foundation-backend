using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>Real SQL migrations, optimistic CAS, immutable audit and rollback.</summary>
public sealed class TenantRoleGrantSqlServerTests
{
    [Fact]
    public async Task Owner_grants_revokes_with_version_CAS_and_append_only_audit()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (context, actor, ownRole, target, foreign) =
                await SeedAsync(setup, options);
            await using var db = context;
            var original = await GetVersionAsync(db, target);
            var first = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(original,
                    [TenantWorkspacePermissionCatalog.MembersRead]), default);
            Assert.NotEqual(original, first);
            Assert.True(await db.RolePermissions.AsNoTracking().AnyAsync(x =>
                x.RoleId == target &&
                x.TenantId == actor.ActiveTenantId &&
                x.Permission.Code == TenantWorkspacePermissionCatalog.MembersRead));
            Assert.Equal(1, await AuditCountAsync(db));

            await Assert.ThrowsAsync<TenantRoleGrantConflictException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(
                    actor, db, target,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(original,
                        [TenantWorkspacePermissionCatalog.GroupsRead]), default));
            Assert.Equal(1, await AuditCountAsync(db));

            var noop = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(first,
                    [TenantWorkspacePermissionCatalog.MembersRead]), default);
            Assert.Equal(first, noop);
            Assert.Equal(1, await AuditCountAsync(db));

            var removed = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(first, []), default);
            Assert.NotEqual(first, removed);
            Assert.False(await db.RolePermissions.AsNoTracking().AnyAsync(
                x => x.RoleId == target && x.TenantId == actor.ActiveTenantId));
            Assert.Equal(2, await AuditCountAsync(db));
            var audit = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantRoleGrantAuditEntries] ORDER BY [OccurredAtUtc]")
                .ToArrayAsync();
            Assert.Contains("grant", audit);
            Assert.Contains("revoke", audit);

            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [dbo].[TenantRoleGrantAuditEntries]"));
            Assert.Equal(2, await AuditCountAsync(db));

            // Local OWNER cannot be edited; foreign target cannot be mutated.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, ownRole,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(removed, [])));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, foreign,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(removed, [])));
            Assert.Equal(2, await AuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Unknown_grants_and_actor_assigned_role_are_rejected()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var version = await GetVersionAsync(db, roleId);
            await Assert.ThrowsAsync<TenantRoleGrantValidationException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        ["SYSTEM.ROLES.CREATE"])));
            await Assert.ThrowsAsync<TenantRoleGrantValidationException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        [TenantWorkspacePermissionCatalog.MembersRead,
                            TenantWorkspacePermissionCatalog.MembersRead])));

            setup.UserRoles.Add(new UserRole(actor.ActiveTenantId!.Value,
                actor.UserId!.Value, roleId));
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        [TenantWorkspacePermissionCatalog.MembersRead])));
            Assert.Empty(await db.RolePermissions.AsNoTracking().ToListAsync());
            Assert.Equal(0, await AuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Failure_of_audit_insert_rolls_back_permissions_and_role_version()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var initial = await GetVersionAsync(db, roleId);
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantRoleGrantAuditEntries]");

            await Assert.ThrowsAsync<SqlException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(initial,
                        [TenantWorkspacePermissionCatalog.GroupsRead])));
            db.ChangeTracker.Clear();
            Assert.False(await db.RolePermissions.AsNoTracking()
                .AnyAsync(x => x.RoleId == roleId));
            Assert.Equal(initial, await GetVersionAsync(db, roleId));
        });
    }

    private static async Task<int> AuditCountAsync(IdentityDbContext db) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantRoleGrantAuditEntries]").SingleAsync();

    private static async Task<string> GetVersionAsync(IdentityDbContext db, Guid roleId) =>
        Convert.ToBase64String((await db.Roles.AsNoTracking()
            .SingleAsync(x => x.Id == roleId)).RowVersion);

    private static async Task<(IdentityDbContext, TenantContext, Guid, Guid, Guid)>
        SeedAsync(IdentityDbContext setup, DbContextOptions<IdentityDbContext> options)
    {
        var own = new Tenant(Guid.NewGuid(), null, "Owner workspace",
            "tenant-role-grants", "owner.test") { IsActive = true };
        var other = new Tenant(Guid.NewGuid(), null, "Other workspace",
            "other-role-grants", "other.test") { IsActive = true };
        var user = new User(Guid.NewGuid(), own.Id, "owner", "owner@example.test",
            "Local", "Owner", UserType.Tenant)
        {
            IsActive = true, NormalizedUserName = "OWNER",
            NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "fixture"
        };
        var ownerRole = new Role(own.Id, "Owner", "Owner role") { Scope = RoleScope.Tenant };
        var editor = new Role(own.Id, "Operator", "Target role") { Scope = RoleScope.Tenant };
        var foreign = new Role(other.Id, "Unrelated", "Foreign") { Scope = RoleScope.Tenant };
        setup.Tenants.AddRange(own, other);
        setup.Users.Add(user);
        setup.Roles.AddRange(ownerRole, editor, foreign);
        setup.UserRoles.Add(new UserRole(own.Id, user.Id, ownerRole.Id));
        await setup.SaveChangesAsync();

        var catalog = new TenantPermissionCatalogSync(setup);
        var preview = await catalog.PreviewAsync();
        Assert.True(preview.CanApply);
        await catalog.ApplyAsync(preview.Fingerprint);
        var current = new TenantContext();
        current.InitializeTenant(user.Id, own.Id, [own.Id], [own.Id], own.Id);
        var db = new IdentityDbContext(options, current);
        return (db, current, ownerRole.Id, editor.Id, foreign.Id);
    }

    private static async Task WithDisposableDatabaseAsync(
        Func<IdentityDbContext, DbContextOptions<IdentityDbContext>, Task> test)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "SHINERA_SQLSERVER_TEST_CONNECTION is required for disposable SQL tests.");
        var source = new SqlConnectionStringBuilder(raw);
        if (!source.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(source.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              source.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Disposable loopback master SQL required.");

        source.InitialCatalog = "FoundationTenantGrants_" + Guid.NewGuid().ToString("N");
        source.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(source.ConnectionString).Options;
        await using var setup = new IdentityDbContext(options, new TenantContext());
        try
        {
            await setup.Database.MigrateAsync();
            await test(setup, options);
        }
        finally
        {
            setup.ChangeTracker.Clear();
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
