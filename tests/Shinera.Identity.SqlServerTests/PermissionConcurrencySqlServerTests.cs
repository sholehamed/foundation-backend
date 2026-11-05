using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Actual disposable SQL Server databases + production Identity migrations.
/// Tests must never silently substitute EF InMemory, SQLite or fake locks.
/// </summary>
public sealed class PermissionConcurrencySqlServerTests
{
    [Fact]
    public async Task Concurrent_create_of_normalized_code_commits_exactly_one_row()
    {
        await WithDatabaseAsync(async (connection, options) =>
        {
            async Task<bool> TryCreateAsync(string code)
            {
                await using var db = Context(options);
                // Both writers are independently connected and start together.
                await Task.Yield();
                try
                {
                    db.Permissions.Add(new Permission { Code = code, Name = "Concurrent request" });
                    await db.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException ex) when (ex.InnerException
                    is SqlException { Number: 2601 or 2627 })
                {
                    return false;
                }
            }

            var first = TryCreateAsync("e2e.concurrent-unique");
            var second = TryCreateAsync(" E2E.CONCURRENT-UNIQUE ");
            await Task.WhenAll(first, second);
            Assert.Equal(1, new[] { first.Result, second.Result }.Count(x => x));

            await using var verify = Context(options);
            Assert.Single(await verify.Permissions.Where(p =>
                p.Code.ToUpper().Contains("CONCURRENT-UNIQUE")).ToListAsync());

            // Unique invariant is global, not filtered to active permissions.
            var row = await verify.Permissions.SingleAsync(p => p.Code.Contains("CONCURRENT"));
            row.IsActive = false;
            await verify.SaveChangesAsync();
            await using var attempt = Context(options);
            attempt.Permissions.Add(new Permission
            {
                Code = "E2E.CONCURRENT-UNIQUE", Name = "Inactive must remain unique"
            });
            var conflict = await Assert.ThrowsAsync<DbUpdateException>(
                () => attempt.SaveChangesAsync());
            Assert.True(conflict.InnerException is SqlException { Number: 2601 or 2627 });
        });
    }

    [Fact]
    public async Task Rolled_back_permission_insert_is_not_visible_and_code_can_be_reused()
    {
        await WithDatabaseAsync(async (_, options) =>
        {
            await using (var writer = Context(options))
            {
                await using var transaction = await writer.Database.BeginTransactionAsync();
                writer.Permissions.Add(new Permission
                    { Code = "E2E.ROLLED-BACK", Name = "Must disappear" });
                await writer.SaveChangesAsync();

                await using (var observer = Context(options))
                {
                    // READ UNCOMMITTED is only used to prove the row was written
                    // before the explicit rollback, never as an authorization read.
                    var uncommitted = await observer.Database.SqlQueryRaw<long>(
                        "SELECT COUNT_BIG(*) AS [Value] FROM [dbo].[Permissions] WITH (NOLOCK) WHERE [Code] = 'E2E.ROLLED-BACK'")
                        .SingleAsync();
                    Assert.Equal(1, uncommitted);
                }

                await transaction.RollbackAsync();
            }

            await using var fresh = Context(options);
            Assert.False(await fresh.Permissions.AnyAsync(
                p => p.Code == "E2E.ROLLED-BACK"));
            fresh.Permissions.Add(new Permission
                { Code = "e2e.rolled-back", Name = "A later transaction succeeds" });
            await fresh.SaveChangesAsync();
            Assert.Equal(1, await fresh.Permissions.CountAsync(
                p => p.Code == "e2e.rolled-back"));
        });
    }

    [Fact]
    public async Task Sql_deadlock_selects_one_victim_and_rolls_back_its_first_update()
    {
        await WithDatabaseAsync(async (cs, options) =>
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            await using (var seed = Context(options))
            {
                seed.Permissions.AddRange(
                    new Permission(firstId) { Code = "E2E.DEADLOCK-A", Name = "start-A" },
                    new Permission(secondId) { Code = "E2E.DEADLOCK-B", Name = "start-B" });
                await seed.SaveChangesAsync();
            }

            var aReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var bReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<bool> RunAsync(bool isA)
            {
                await using var connection = new SqlConnection(cs);
                await connection.OpenAsync();
                await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                try
                {
                    await using (var priority = new SqlCommand(
                        isA ? "SET DEADLOCK_PRIORITY LOW" : "SET DEADLOCK_PRIORITY NORMAL",
                        connection, tx))
                        await priority.ExecuteNonQueryAsync(timeout.Token);

                    async Task WriteAsync(Guid id, string name)
                    {
                        await using var cmd = new SqlCommand(
                            "UPDATE [dbo].[Permissions] SET [Name] = @name WHERE [Id] = @id",
                            connection, tx);
                        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
                        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                        Assert.Equal(1, await cmd.ExecuteNonQueryAsync(timeout.Token));
                    }

                    await WriteAsync(isA ? firstId : secondId,
                        isA ? "A-first" : "B-first");
                    (isA ? aReady : bReady).TrySetResult(true);
                    await (isA ? bReady : aReady).Task.WaitAsync(timeout.Token);
                    await WriteAsync(isA ? secondId : firstId,
                        isA ? "A-second" : "B-second");
                    await tx.CommitAsync(timeout.Token);
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 1205)
                {
                    // SQL Server automatically rolls back the deadlock victim.
                    return false;
                }
                finally
                {
                    (isA ? aReady : bReady).TrySetResult(true);
                }
            }

            var a = RunAsync(true);
            var b = RunAsync(false);
            await Task.WhenAll(a, b);
            Assert.Equal(1, new[] { a.Result, b.Result }.Count(x => x));

            await using var check = Context(options);
            var values = await check.Permissions.AsNoTracking()
                .Where(p => p.Id == firstId || p.Id == secondId)
                .Select(p => new { p.Id, p.Name }).ToListAsync();
            Assert.Equal(2, values.Count);
            var firstName = values.Single(x => x.Id == firstId).Name;
            var secondName = values.Single(x => x.Id == secondId).Name;
            Assert.True((firstName == "A-first" && secondName == "A-second") ||
                        (firstName == "B-second" && secondName == "B-first"),
                "The deadlock victim's earlier update must be rolled back.");
        });
    }

    [Fact]
    public async Task Migration_rejects_legacy_normalized_duplicates_without_rewriting_ids()
    {
        await WithDatabaseAsync(async (_, options) =>
        {
            await using var db = Context(options);
            var one = new Permission { Code = "E2E.LEGACY-DUP", Name = "Legacy one" };
            var two = new Permission { Code = " e2e.legacy-dup ", Name = "Legacy two" };
            db.Permissions.AddRange(one, two);
            await db.SaveChangesAsync();

            var error = await Assert.ThrowsAnyAsync<Exception>(
                () => db.Database.MigrateAsync());
            Assert.Contains("normalized duplicate codes", error.ToString(),
                StringComparison.OrdinalIgnoreCase);

            // Neither legacy ID was silently merged, deleted or reassigned.
            Assert.True(await db.Permissions.AnyAsync(p => p.Id == one.Id));
            Assert.True(await db.Permissions.AnyAsync(p => p.Id == two.Id));

            db.Permissions.Remove(two);
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            Assert.True(await db.Permissions.AnyAsync(p => p.Id == one.Id));
        }, targetMigration: "20261023210000_platformaccessdenialaudit");
    }

    [Fact]
    public async Task Production_startup_defers_unique_migration_until_explicit_opt_in()
    {
        await WithDatabaseAsync(async (_, options) =>
        {
            await using var db = Context(options);
            var one = new Permission { Code = "E2E.PROD-DUP", Name = "Legacy first" };
            var two = new Permission { Code = " e2e.prod-dup ", Name = "Legacy second" };
            db.Permissions.AddRange(one, two);
            await db.SaveChangesAsync();

            // Prior production startup automatically migrated everything;
            // that would now prevent API startup for historical duplicates.
            await DbSeeder.MigrateForStartupAsync(
                db, isProduction: true, allowCodeUniquenessMigration: false);

            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.DoesNotContain("20261024200000_permissioncodeuniqueness", applied);
            Assert.True(await db.Permissions.AnyAsync(p => p.Id == one.Id));
            Assert.True(await db.Permissions.AnyAsync(p => p.Id == two.Id));

            // Explicit reviewed rollout fails closed on dirty data, and the
            // attempted DDL does not change the existing Permission IDs.
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                DbSeeder.MigrateForStartupAsync(
                    db, isProduction: true, allowCodeUniquenessMigration: true));
            Assert.Contains("normalized duplicate codes", error.ToString(),
                StringComparison.OrdinalIgnoreCase);

            db.Permissions.Remove(two);
            await db.SaveChangesAsync();
            await DbSeeder.MigrateForStartupAsync(
                db, isProduction: true, allowCodeUniquenessMigration: true);

            Assert.Contains("20261024200000_permissioncodeuniqueness",
                await db.Database.GetAppliedMigrationsAsync());

            // Turning the flag back off after a successful rollout must NOT
            // downgrade/drop an already installed uniqueness constraint.
            await DbSeeder.MigrateForStartupAsync(
                db, isProduction: true, allowCodeUniquenessMigration: false);
            var conflict = new Permission { Code = "e2e.prod-dup", Name = "Duplicate" };
            db.Permissions.Add(conflict);
            var duplicateError = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.True(duplicateError.InnerException
                is SqlException { Number: 2601 or 2627 });
        }, targetMigration: "20261023210000_platformaccessdenialaudit");
    }

    private static IdentityDbContext Context(DbContextOptions<IdentityDbContext> options)
        => new(options, new TenantContext());

    private static async Task WithDatabaseAsync(
        Func<string, DbContextOptions<IdentityDbContext>, Task> work,
        string? targetMigration = null)
    {
        var connection = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Disposable SQL Server connection is required.");

        var cs = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "ShineraConcurrencyTest_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 30
        };
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs.ConnectionString)
            .Options;
        await using var setup = Context(options);
        try
        {
            if (targetMigration is null)
                await setup.Database.MigrateAsync();
            else
                await setup.Database.MigrateAsync(targetMigration);
            await work(cs.ConnectionString, options);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
