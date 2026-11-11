using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Real disposable SQL Server acceptance of FR-17D-2A. The production
/// commercial module manifest remains empty: test-only, source-declared
/// contracts are supplied to the unexposed metering service.
/// </summary>
public sealed class AtomicUsageSqlServerTests
{
    private const string Key = "appointments.monthly";

    private static ResourceOperationContract Operation(string code = Key) =>
        new("appointments", "bookings", "APPOINTMENTS.CREATE",
            "APPOINTMENTS.BOOKINGS.CREATE", [code]);

    private static CapabilityContract Contract(
        CapabilityKind kind = CapabilityKind.PeriodQuota, string code = Key) =>
        new(code, "appointments", "bookings", kind,
            CapabilityValueKind.Integer, CapabilityMergePolicy.Override);

    private static ResolvedCatalogCapability Limit(long units, string code = Key) =>
        new(code, null, units, false, []);

    private static (DateTime Start, DateTime End) CurrentMonth()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }

    [Fact]
    public async Task Atomic_quota_is_idempotent_capped_and_append_only()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "quota");
            var (start, end) = CurrentMonth();
            await using var db = Scoped(options, tenant);
            var service = new TenantAtomicUsageService(db, Context(tenant));

            async Task<TenantUsageResult> Consume(string operation, long amount)
            {
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                // The same transaction would also persist the business write.
                var result = await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(3), start, end,
                    operation, amount);
                if (result.Ok) await tx.CommitAsync();
                return result;
            }

            var first = await Consume("request-quota-0001", 2);
            Assert.True(first.Ok, first.Code);
            Assert.Equal("consumed", first.Code);
            Assert.Equal(2, first.Consumed);
            Assert.Equal(1, first.Remaining);

            var retry = await Consume("request-quota-0001", 2);
            Assert.True(retry.Ok);
            Assert.Equal("already_consumed", retry.Code);
            Assert.Equal("idempotency_conflict",
                (await Consume("request-quota-0001", 1)).Code);

            Assert.Equal("consumed", (await Consume("request-quota-0002", 1)).Code);
            Assert.Equal("limit_exceeded",
                (await Consume("request-quota-0003", 1)).Code);

            Assert.Equal(3L, await Balance(db, tenant, start));
            Assert.Equal(2L, await EventCount(db, tenant, start));

            // Database itself disallows deleting/rewriting audit history.
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            var updateError = await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM dbo.TenantUsageEvents WHERE TenantId={0}", tenant));
            Assert.Equal(51101, updateError.Number);
            await tx.RollbackAsync();
        });
    }

    [Fact]
    public async Task Reversal_restores_capacity_is_idempotent_and_cannot_be_reconsumed()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "reverse");
            var (start, end) = CurrentMonth();
            await using var db = Scoped(options, tenant);
            var service = new TenantAtomicUsageService(db, Context(tenant));

            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                Assert.True((await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(2), start, end,
                    "request-reverse-1", 2)).Ok);
                await tx.CommitAsync();
            }
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var result = await service.ReverseWithinTransactionAsync(
                    Key, start, "request-reverse-1");
                Assert.Equal("reversed", result.Code);
                await tx.CommitAsync();
            }
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                Assert.Equal("already_reversed", (await service.ReverseWithinTransactionAsync(
                    Key, start, "request-reverse-1")).Code);
                Assert.Equal("already_reversed", (await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(2), start, end,
                    "request-reverse-1", 2)).Code);
                Assert.Equal("consume_event_missing", (await service.ReverseWithinTransactionAsync(
                    Key, start, "missing-request-1")).Code);
                await tx.CommitAsync();
            }
            Assert.Equal(0L, await Balance(db, tenant, start));
            Assert.Equal(2L, await EventCount(db, tenant, start));
        });
    }

    [Fact]
    public async Task SQL_rollback_removes_quota_charge_and_duplicate_command_can_retry()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "rollback");
            var (start, end) = CurrentMonth();
            await using (var db = Scoped(options, tenant))
            {
                var service = new TenantAtomicUsageService(db, Context(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var consumed = await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(1), start, end,
                    "request-rollback", 1);
                Assert.True(consumed.Ok, consumed.Code);
                // Simulate failed business validation AFTER the quota check.
                await tx.RollbackAsync();
            }
            await using (var db = Scoped(options, tenant))
            {
                Assert.Equal(0L, await Balance(db, tenant, start));
                Assert.Equal(0L, await EventCount(db, tenant, start));
                var service = new TenantAtomicUsageService(db, Context(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var retry = await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(1), start, end,
                    "request-rollback", 1);
                Assert.Equal("consumed", retry.Code);
                await tx.CommitAsync();
            }
        });
    }

    [Fact]
    public async Task Concurrent_quota_requests_cannot_overrun_one_remaining_unit()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "race");
            var (start, end) = CurrentMonth();

            async Task<TenantUsageResult> Run(string key)
            {
                await using var db = Scoped(options, tenant);
                var service = new TenantAtomicUsageService(db, Context(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var result = await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(1), start, end, key, 1);
                if (result.Ok) await tx.CommitAsync();
                return result;
            }

            var outcomes = await Task.WhenAll(
                Task.Run(() => Run("parallel-request-0001")),
                Task.Run(() => Run("parallel-request-0002")));
            Assert.Single(outcomes, x => x.Ok);
            Assert.Single(outcomes, x => x.Code == "limit_exceeded");
            await using var check = Scoped(options, tenant);
            Assert.Equal(1L, await Balance(check, tenant, start));
            Assert.Equal(1L, await EventCount(check, tenant, start));
        });
    }

    [Fact]
    public async Task Another_tenant_does_not_share_usage_or_reversal_history()
    {
        await WithDatabase(async (options, admin) =>
        {
            var a = await SeedTenant(admin, "a");
            var b = await SeedTenant(admin, "b");
            var (start, end) = CurrentMonth();

            async Task<TenantUsageResult> Run(Guid tenant)
            {
                await using var db = Scoped(options, tenant);
                var service = new TenantAtomicUsageService(db, Context(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var result = await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(1), start, end,
                    "tenant-shared-operation", 1);
                if (result.Ok) await tx.CommitAsync();
                return result;
            }
            Assert.True((await Run(a)).Ok);
            Assert.True((await Run(b)).Ok);
            await using var scopedB = Scoped(options, b);
            await using (var tx = await scopedB.Database.BeginTransactionAsync(
                IsolationLevel.Serializable))
            {
                var reversal = await new TenantAtomicUsageService(scopedB, Context(b))
                    .ReverseWithinTransactionAsync(Key, start, "tenant-a-only-operation");
                Assert.Equal("consume_event_missing", reversal.Code);
                await tx.RollbackAsync();
            }
            Assert.Equal(1L, await Balance(scopedB, b, start));
            await using var scopedA = Scoped(options, a);
            Assert.Equal(1L, await Balance(scopedA, a, start));
        });
    }

    [Fact]
    public async Task Missing_serializable_transaction_or_writable_tenant_fails_closed()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "boundary");
            var (start, end) = CurrentMonth();
            await using var db = Scoped(options, tenant);
            var service = new TenantAtomicUsageService(db, Context(tenant));
            Assert.Equal("serializable_sql_transaction_required",
                (await service.ConsumeWithinTransactionAsync(
                    Operation(), Contract(), Limit(3), start, end,
                    "request-no-transaction", 1)).Code);

            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), true);
            await using var systemDb = new IdentityDbContext(options, system);
            await using var tx = await systemDb.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            Assert.Equal("current_writable_tenant_required",
                (await new TenantAtomicUsageService(systemDb, system)
                    .ConsumeWithinTransactionAsync(
                        Operation(), Contract(), Limit(3), start, end,
                        "request-invalid-actor", 1)).Code);
            await tx.RollbackAsync();
        });
    }

    [Fact]
    public async Task Count_limit_guard_serializes_actual_business_writes_and_allows_cleanup()
    {
        await WithDatabase(async (options, admin) =>
        {
            var tenant = await SeedTenant(admin, "active");
            await admin.Database.ExecuteSqlRawAsync("""
                CREATE TABLE dbo.FR17D_TestActiveItems(
                  Id uniqueidentifier NOT NULL PRIMARY KEY,
                  TenantId uniqueidentifier NOT NULL,
                  IsActive bit NOT NULL);
                """);
            var capability = Contract(CapabilityKind.CountLimit, "appointments.max-active");
            var op = Operation("appointments.max-active");
            var max = Limit(1, "appointments.max-active");

            async Task<TenantUsageResult> Activate()
            {
                await using var db = Scoped(options, tenant);
                var service = new TenantAtomicUsageService(db, Context(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var result = await service.CheckCountWithinTransactionAsync(
                    op, capability, max, 1,
                    ct => db.Database.SqlQueryRaw<long>(
                        "SELECT COUNT_BIG(*) AS [Value] FROM dbo.FR17D_TestActiveItems WHERE TenantId={0} AND IsActive=1",
                        tenant).SingleAsync(ct));
                if (result.Ok)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT dbo.FR17D_TestActiveItems(Id,TenantId,IsActive) VALUES ({0},{1},1)",
                        Guid.NewGuid(), tenant);
                    await tx.CommitAsync();
                }
                return result;
            }

            var results = await Task.WhenAll(
                Task.Run(Activate), Task.Run(Activate));
            Assert.Single(results, x => x.Ok);
            Assert.Single(results, x => x.Code == "limit_exceeded");

            await using var cleanup = Scoped(options, tenant);
            var countGuard = new TenantAtomicUsageService(cleanup, Context(tenant));
            await using var cleanupTx = await cleanup.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            Assert.True((await countGuard.CheckCountWithinTransactionAsync(
                op, capability, Limit(0, "appointments.max-active"), -1,
                ct => cleanup.Database.SqlQueryRaw<long>(
                    "SELECT COUNT_BIG(*) AS [Value] FROM dbo.FR17D_TestActiveItems WHERE TenantId={0} AND IsActive=1",
                    tenant).SingleAsync(ct))).Ok);
            await cleanup.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.FR17D_TestActiveItems SET IsActive=0 WHERE TenantId={0}", tenant);
            await cleanupTx.CommitAsync();
        });
    }

    private static TenantContext Context(Guid tenantId)
    {
        var context = new TenantContext();
        context.InitializeTenant(Guid.NewGuid(), tenantId,
            [tenantId], [tenantId], tenantId);
        return context;
    }

    private static IdentityDbContext Scoped(
        DbContextOptions<IdentityDbContext> options, Guid tenant) =>
        new(options, Context(tenant));

    private static async Task<Guid> SeedTenant(IdentityDbContext db, string tag)
    {
        var tenant = new Tenant { Name = "Usage " + tag, Slug = "usage-" + Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<long> Balance(
        IdentityDbContext db, Guid tenant, DateTime start) =>
        await db.Database.SqlQueryRaw<long>(
            "SELECT CONVERT(bigint, COALESCE(SUM(ConsumedUnits),0)) AS [Value] " +
            "FROM dbo.TenantUsageBalances WHERE TenantId={0} AND PeriodStartUtc={1}",
            tenant, start).SingleAsync();

    private static async Task<long> EventCount(
        IdentityDbContext db, Guid tenant, DateTime start) =>
        await db.Database.SqlQueryRaw<long>(
            "SELECT COUNT_BIG(*) AS [Value] FROM dbo.TenantUsageEvents " +
            "WHERE TenantId={0} AND PeriodStartUtc={1}", tenant, start).SingleAsync();

    private static async Task WithDatabase(
        Func<DbContextOptions<IdentityDbContext>, IdentityDbContext, Task> run)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "SHINERA_SQLSERVER_TEST_CONNECTION required for disposable SQL tests");
        var admin = new SqlConnectionStringBuilder(raw);
        if (!admin.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(admin.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              admin.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tests require loopback master SQL instance");

        admin.InitialCatalog = "FoundationAtomicUsageTest_" + Guid.NewGuid().ToString("N");
        admin.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(admin.ConnectionString).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        try
        {
            Assert.False(db.Database.HasPendingModelChanges(),
                "EF model snapshot drift; raw-SQL usage tables must not duplicate EF tables");
            await db.Database.MigrateAsync();
            await run(options, db);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
