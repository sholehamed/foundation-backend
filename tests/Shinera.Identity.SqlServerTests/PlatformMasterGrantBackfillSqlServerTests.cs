using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>Disposable real SQL Server test databases. Never shared or Production.</summary>
public sealed class PlatformMasterGrantBackfillSqlServerTests
{
    [Fact]
    public async Task Exact_approved_grant_is_audited_and_rollback_only_removes_that_grant()
    {
        await WithDatabaseAsync(async (db, databaseName) =>
        {
            var (role, actor, reviewer, permissions) = await SeedAsync(db);
            var existing = new RolePermission(DefaultSeedData.MasterTenant.Id,
                role.Id, permissions[0].Id) { CreatedBy = actor.Id };
            db.RolePermissions.Add(existing);
            await db.SaveChangesAsync();

            var newAssignmentId = Guid.NewGuid();
            var manifest = new PlatformMasterGrantBackfill.Manifest(
                DefaultSeedData.MasterTenant.Id, role.Id, actor.Id, reviewer.Id,
                databaseName,
                [new PlatformMasterGrantBackfill.Grant(existing.Id, existing.PermissionId)],
                [new PlatformMasterGrantBackfill.Grant(newAssignmentId, permissions[1].Id)]);

            await PlatformMasterGrantBackfill.ApplyAsync(db, manifest);
            Assert.True(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == newAssignmentId &&
                    x.RoleId == role.Id && x.PermissionId == permissions[1].Id));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlatformMasterGrantBackfill.ApplyAsync(db, manifest));

            await PlatformMasterGrantBackfill.RollbackAsync(db, manifest);
            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == newAssignmentId));
            Assert.True(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == existing.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlatformMasterGrantBackfill.RollbackAsync(db, manifest));

            var audit = await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] " +
                "WHERE [Kind]='RolePermission' AND [TargetId]={0} AND [RelatedId]={1}",
                role.Id, permissions[1].Id).SingleAsync();
            Assert.Equal(2, audit);
        });
    }

    [Fact]
    public async Task Snapshot_drift_and_audit_failure_cannot_commit_any_backfill_grants()
    {
        await WithDatabaseAsync(async (db, databaseName) =>
        {
            var (role, actor, reviewer, permissions) = await SeedAsync(db);
            var id = Guid.NewGuid();
            var manifest = new PlatformMasterGrantBackfill.Manifest(
                DefaultSeedData.MasterTenant.Id, role.Id, actor.Id, reviewer.Id,
                databaseName, [],
                [new PlatformMasterGrantBackfill.Grant(id, permissions[0].Id)]);

            // Existing state changed since the separately approved empty snapshot.
            var unexpected = new RolePermission(DefaultSeedData.MasterTenant.Id,
                role.Id, permissions[2].Id) { CreatedBy = actor.Id };
            db.RolePermissions.Add(unexpected);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlatformMasterGrantBackfill.ApplyAsync(db, manifest));
            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == id));
            db.RolePermissions.Remove(unexpected);
            await db.SaveChangesAsync();

            // Injection failure on the mandatory append-only ledger must roll
            // back the RolePermission insert in the SAME SQL transaction.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_GrantBackfill_FailAuditTest]
                ON [dbo].[PlatformGrantAuditEntries] AFTER INSERT AS
                BEGIN
                    THROW 51075, 'Simulated audit storage failure.', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(
                    () => PlatformMasterGrantBackfill.ApplyAsync(db, manifest));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_GrantBackfill_FailAuditTest]");
            }

            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == id));
            var audit = await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] " +
                "WHERE [Kind]='RolePermission' AND [TargetId]={0} AND [RelatedId]={1}",
                role.Id, permissions[0].Id).SingleAsync();
            Assert.Equal(0, audit);

            // No automatic retry/re-grant; only an explicit operator replay.
            await PlatformMasterGrantBackfill.ApplyAsync(db, manifest);
            Assert.True(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == id));
        });
    }

    [Fact]
    public async Task Invalid_reviewer_or_database_scope_fails_closed_without_grants_or_audit()
    {
        await WithDatabaseAsync(async (db, databaseName) =>
        {
            var (role, actor, reviewer, permissions) = await SeedAsync(db);
            var grant = new PlatformMasterGrantBackfill.Grant(
                Guid.NewGuid(), permissions[0].Id);
            var approved = new PlatformMasterGrantBackfill.Manifest(
                DefaultSeedData.MasterTenant.Id, role.Id, actor.Id,
                reviewer.Id, databaseName, [], [grant]);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PlatformMasterGrantBackfill.ApplyAsync(
                    db, approved with { DatabaseName = databaseName + "_other" }));

            var reviewerMembership = await db.UserRoles
                .IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.RoleId == role.Id && x.UserId == reviewer.Id);
            db.UserRoles.Remove(reviewerMembership);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PlatformMasterGrantBackfill.ApplyAsync(db, approved));

            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == grant.AssignmentId));

            var audit = await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] " +
                "WHERE [Kind]='RolePermission' AND [TargetId]={0} AND [RelatedId]={1}",
                role.Id, grant.PermissionId).SingleAsync();
            Assert.Equal(0, audit);
        });
    }

    [Fact]
    public async Task Failure_on_second_grant_audit_rolls_back_the_entire_two_grant_batch()
    {
        await WithDatabaseAsync(async (db, databaseName) =>
        {
            var (role, actor, reviewer, permissions) = await SeedAsync(db);
            var first = new PlatformMasterGrantBackfill.Grant(Guid.NewGuid(), permissions[0].Id);
            var second = new PlatformMasterGrantBackfill.Grant(Guid.NewGuid(), permissions[1].Id);
            var manifest = new PlatformMasterGrantBackfill.Manifest(
                DefaultSeedData.MasterTenant.Id, role.Id, actor.Id, reviewer.Id,
                databaseName, [], [first, second]);

            // First audit INSERT succeeds, second fails. SQL transaction must
            // undo the first audit AND both RolePermission inserts.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_GrantBackfill_FailSecondAuditTest]
                ON [dbo].[PlatformGrantAuditEntries] AFTER INSERT AS
                BEGIN
                    IF (SELECT COUNT_BIG(*) FROM [dbo].[PlatformGrantAuditEntries]
                        WHERE [Kind] = 'RolePermission' AND [TargetId] IN
                            (SELECT [TargetId] FROM inserted)) >= 2
                        THROW 51076, 'Simulated failure on second grant audit.', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(
                    () => PlatformMasterGrantBackfill.ApplyAsync(db, manifest));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_GrantBackfill_FailSecondAuditTest]");
            }

            Assert.False(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == first.AssignmentId ||
                               x.Id == second.AssignmentId));
            var audits = await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] " +
                "WHERE [Kind] = 'RolePermission' AND [TargetId] = {0}",
                role.Id).SingleAsync();
            Assert.Equal(0, audits);
        });
    }

    [Fact]
    public async Task Rollback_rejects_a_matching_row_without_prior_committed_grant_audit()
    {
        await WithDatabaseAsync(async (db, databaseName) =>
        {
            var (role, actor, reviewer, permissions) = await SeedAsync(db);
            var manuallyInserted = new RolePermission(
                DefaultSeedData.MasterTenant.Id, role.Id, permissions[0].Id)
            {
                CreatedBy = actor.Id
            };
            db.RolePermissions.Add(manuallyInserted);
            await db.SaveChangesAsync();

            // The row ID, permission and CreatedBy all match the claimed
            // manifest. Without matching committed grant-audit evidence,
            // deleting it would silently revoke an unrelated manual grant.
            var claimed = new PlatformMasterGrantBackfill.Manifest(
                DefaultSeedData.MasterTenant.Id, role.Id, actor.Id, reviewer.Id,
                databaseName, [],
                [new PlatformMasterGrantBackfill.Grant(
                    manuallyInserted.Id, manuallyInserted.PermissionId)]);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlatformMasterGrantBackfill.RollbackAsync(db, claimed));
            Assert.Contains("grant audit is missing", error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.Id == manuallyInserted.Id));

            var audits = await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] " +
                "WHERE [Kind]='RolePermission' AND [TargetId]={0} AND [RelatedId]={1}",
                role.Id, permissions[0].Id).SingleAsync();
            Assert.Equal(0, audits);
        });
    }

    private static async Task<(Role role, User actor, User reviewer, List<Permission> permissions)>
        SeedAsync(IdentityDbContext db)
    {
        var master = DefaultSeedData.MasterTenant.Id;
        db.Tenants.Add(new Tenant(master, null, "Master", "backfill-master", "localhost"));
        var role = new Role(master, "superadmin", "Test-only reviewed role");
        var actor = User("backfill-actor");
        var reviewer = User("backfill-reviewer");
        db.AddRange(role, actor, reviewer,
            new UserRole(master, actor.Id, role.Id),
            new UserRole(master, reviewer.Id, role.Id));
        await db.SaveChangesAsync();
        await PlatformPermissionCatalogSeeder.SeedDefinitionsAsync(db);
        var codes = PlatformPermissionCatalog.Codes;
        var permissions = await db.Permissions.AsNoTracking()
            .Where(x => codes.Contains(x.Code)).OrderBy(x => x.Code)
            .ToListAsync();
        Assert.Equal(codes.Count, permissions.Count);
        return (role, actor, reviewer, permissions);
    }

    private static User User(string username)
        => new(null, username, username + "@localhost.test",
            "Test", "User", UserType.System)
        {
            NormalizedUserName = username.ToUpperInvariant(),
            NormalizedEmail = (username + "@localhost.test").ToUpperInvariant(),
            PasswordHash = "CI-only",
            SecurityStamp = "CI-only"
        };

    private static async Task WithDatabaseAsync(
        Func<IdentityDbContext, string, Task> run)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Disposable SQL Server connection required.");
        var cs = new SqlConnectionStringBuilder(raw)
        {
            InitialCatalog = "ShineraGrantBatch_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 30
        };
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs.ConnectionString).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        try
        {
            await db.Database.MigrateAsync();
            await run(db, cs.InitialCatalog);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
