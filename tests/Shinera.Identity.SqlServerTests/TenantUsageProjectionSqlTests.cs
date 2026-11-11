using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Real SQL acceptance for the read-only, source-validated usage projection.
/// Commercial declarations and plans exist ONLY in disposable test databases.
/// </summary>
public sealed class TenantUsageProjectionSqlTests
{
    private static readonly ResourceContract[] Sources =
    [
        new("appointments", "bookings", ModuleProductClass.Commercial,
            ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"])
    ];

    private static readonly CapabilityContract[] Definitions =
    [
        new("appointments.monthly", "appointments", "bookings",
            CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override, QuotaPeriodCadence.MonthlyAnniversary),
        new("appointments.unlimited", "appointments", "bookings",
            CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override, QuotaPeriodCadence.SubscriptionTerm),
        new("appointments.no-cadence", "appointments", "bookings",
            CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override),
        new("appointments.max-active", "appointments", "bookings",
            CapabilityKind.CountLimit, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override)
    ];

    private static TenantContext Context(Guid user, Guid tenant)
    {
        var current = new TenantContext();
        current.InitializeTenant(user, tenant, [tenant], [tenant], tenant);
        return current;
    }

    [Fact]
    public async Task Verified_active_subscription_reads_real_period_balance_and_never_guesses_count()
    {
        await Database(async (db, options) =>
        {
            var a = new Tenant { Name = "Usage A", Slug = "usage-a-" + Guid.NewGuid().ToString("N") };
            var b = new Tenant { Name = "Usage B", Slug = "usage-b-" + Guid.NewGuid().ToString("N") };
            var notSubscribed = new Tenant {
                Name = "Usage C", Slug = "usage-c-" + Guid.NewGuid().ToString("N") };
            var module = new Module("appointments", "Appointments", null, 0);
            var bookings = new Resource(module.Id, "bookings", "Bookings", null, 0);
            var plan = new CatalogPlan { Code = "usage-test", Name = "Usage Test" };
            var version = new CatalogPlanVersion {
                PlanId = plan.Id, VersionNumber = 1, DisplayTitle = "Usage Test", Status = "Draft" };
            var now = DateTime.UtcNow;
            var starts = now.AddDays(-2);
            var ends = now.AddMonths(2);

            db.Tenants.AddRange(a, b, notSubscribed);
            db.Modules.Add(module);
            db.Resources.Add(bookings);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
                { PlanVersionId = version.Id, ModuleId = module.Id });

            foreach (var definition in Definitions)
            {
                var entity = new CatalogCapabilityDefinition
                {
                    Code = definition.Code, Title = definition.Code,
                    ModuleId = module.Id, ResourceId = bookings.Id,
                    Kind = definition.Kind.ToString(),
                    ValueKind = definition.ValueKind.ToString(),
                    MergePolicy = definition.MergePolicy.ToString()
                };
                db.CatalogCapabilityDefinitions.Add(entity);
                db.CatalogPlanVersionCapabilityValues.Add(
                    new CatalogPlanVersionCapabilityValue
                    {
                        PlanVersionId = version.Id,
                        CapabilityDefinitionId = entity.Id,
                        IntegerValue = definition.Code switch
                        {
                            "appointments.monthly" => 5,
                            "appointments.max-active" => 2,
                            "appointments.no-cadence" => 9,
                            _ => null
                        },
                        IsUnlimited = definition.Code == "appointments.unlimited"
                    });
            }
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 200000
            });
            await db.SaveChangesAsync();
            version.Status = "Published";
            version.PublishedAtUtc = now.AddDays(-5);
            version.ContentFingerprint = new string('d', 64);
            await db.SaveChangesAsync();

            static User UserFor(Tenant tenant)
            {
                var id = Guid.NewGuid();
                return new User(id, tenant.Id, "meter-" + id.ToString("N"),
                    null, "Meter", "Tester", UserType.Tenant)
                {
                    PasswordHash = "test-only",
                    NormalizedUserName = "METER-" + id.ToString("N").ToUpperInvariant()
                };
            }
            var actorA = UserFor(a);
            var actorB = UserFor(b);
            db.Users.AddRange(actorA, actorB);

            foreach (var tenant in new[] { a, b })
            {
                var order = new TenantPlanOrder
                {
                    TenantId = tenant.Id, PlanVersionId = version.Id,
                    Status = "Settled", BillingCycle = "monthly", Currency = "IRR",
                    GrossAmount = 200000m, DiscountAmount = 0m, NetAmount = 200000m,
                    QuotedAtUtc = now.AddDays(-6),
                    SettledAtUtc = now.AddDays(-5),
                    SettlementKind = "ExternalManual",
                    SettlementReference = "test-reviewed-" + tenant.Id.ToString("N"),
                    IdempotencyKey = "meter-test-" + tenant.Id.ToString("N")
                };
                db.TenantPlanOrders.Add(order);
                db.TenantSubscriptions.Add(new TenantSubscription
                {
                    TenantId = tenant.Id, PlanVersionId = version.Id,
                    SourceOrderId = order.Id, Status = "Active",
                    StartsAtUtc = starts, EndsAtUtc = ends,
                    ActivatedAtUtc = now.AddDays(-4)
                });
            }
            await db.SaveChangesAsync();

            var currentWindow = QuotaPeriodResolver.Resolve(
                starts, ends, now, QuotaPeriodCadence.MonthlyAnniversary)!;

            async Task<TenantUsageProjection> Read(Guid actor, Guid tenant)
            {
                var context = Context(actor, tenant);
                await using var scoped = new IdentityDbContext(options, context);
                return await new TenantUsageProjectionReader(
                    scoped, context, Sources, Definitions).ReadCurrentAsync(DateTime.UtcNow);
            }

            var emptyA = await Read(actorA.Id, a.Id);
            Assert.Equal("Active", emptyA.Status);
            Assert.DoesNotContain(emptyA.Items, x => x.Code == "unregistered");
            var initial = Assert.Single(emptyA.Items,
                x => x.Code == "appointments.monthly");
            Assert.Equal("Measured", initial.State);
            Assert.Equal("0", initial.CurrentUsage);
            Assert.Equal("5", initial.Remaining);
            Assert.Equal(currentWindow.StartUtc, initial.PeriodStartUtc);
            Assert.Equal(currentWindow.EndUtc, initial.PeriodEndUtc);

            async Task<TenantUsageResult> Charge(Guid actor, Guid tenant,
                string key, long amount, string capability = "appointments.monthly")
            {
                var context = Context(actor, tenant);
                await using var scoped = new IdentityDbContext(options, context);
                await using var tx = await scoped.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var interval = capability == "appointments.unlimited"
                    ? QuotaPeriodResolver.Resolve(starts, ends, DateTime.UtcNow,
                        QuotaPeriodCadence.SubscriptionTerm)!
                    : currentWindow;
                var result = await new TenantAtomicUsageService(scoped, context)
                    .ConsumeWithinTransactionAsync(
                        new("appointments", "bookings", "APPOINTMENTS.CREATE",
                            "APPOINTMENTS.BOOKINGS.CREATE", [capability]),
                        Definitions.Single(x => x.Code == capability),
                        capability == "appointments.unlimited"
                            ? new(capability, null, null, true, [])
                            : new(capability, null, 5, false, []),
                        interval.StartUtc, interval.EndUtc,
                        key, amount);
                if (result.Ok) await tx.CommitAsync();
                return result;
            }

            Assert.True((await Charge(actorA.Id, a.Id, "usage-a-request-001", 3)).Ok);
            Assert.True((await Charge(actorB.Id, b.Id, "usage-b-request-001", 1)).Ok);
            Assert.True((await Charge(actorA.Id, a.Id, "usage-a-unlimited-01", 7,
                "appointments.unlimited")).Ok);

            var aResult = await Read(actorA.Id, a.Id);
            var am = Assert.Single(aResult.Items, x => x.Code == "appointments.monthly");
            Assert.Equal("3", am.CurrentUsage);
            Assert.Equal("2", am.Remaining);
            Assert.False(am.IsUnlimited);
            var au = Assert.Single(aResult.Items, x => x.Code == "appointments.unlimited");
            Assert.Equal("7", au.CurrentUsage);
            Assert.True(au.IsUnlimited);
            Assert.Null(au.Limit);
            Assert.Null(au.Remaining);
            var count = Assert.Single(aResult.Items, x => x.Code == "appointments.max-active");
            Assert.Equal("DomainCountUnavailable", count.State);
            Assert.Equal("2", count.Limit);
            Assert.Null(count.CurrentUsage);
            Assert.Null(count.Remaining);
            var noCadence = Assert.Single(aResult.Items, x => x.Code == "appointments.no-cadence");
            Assert.Equal("PeriodNotConfigured", noCadence.State);
            Assert.Null(noCadence.CurrentUsage);
            Assert.Null(noCadence.PeriodStartUtc);

            var bResult = await Read(actorB.Id, b.Id);
            Assert.Equal("1", Assert.Single(bResult.Items,
                x => x.Code == "appointments.monthly").CurrentUsage);
            var cContext = Context(Guid.NewGuid(), notSubscribed.Id);
            await using (var scoped = new IdentityDbContext(options, cContext))
            {
                var unavailable = await new TenantUsageProjectionReader(
                    scoped, cContext, Sources, Definitions).ReadCurrentAsync(now);
                Assert.Equal("NotSubscribed", unavailable.Status);
                Assert.Empty(unavailable.Items);
            }

            // Any mismatched period definition is not reported as "0 used".
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.TenantUsageBalances SET PeriodEndUtc={0} " +
                "WHERE TenantId={1} AND CapabilityCode={2} AND PeriodStartUtc={3}",
                currentWindow.EndUtc.AddMinutes(1), a.Id, "appointments.monthly",
                currentWindow.StartUtc);
            var conflicted = await Read(actorA.Id, a.Id);
            var item = Assert.Single(conflicted.Items,
                x => x.Code == "appointments.monthly");
            Assert.Equal("PeriodConflict", item.State);
            Assert.Null(item.CurrentUsage);
            Assert.Null(item.Remaining);

            // Deleting the deployed code-owned Commercial manifest makes even
            // previously settled license data unavailable to the projection.
            var contextA = Context(actorA.Id, a.Id);
            await using var production = new IdentityDbContext(options, contextA);
            var closed = await new TenantUsageProjectionReader(
                production, contextA).ReadCurrentAsync(now);
            Assert.NotEqual("Active", closed.Status);
            Assert.Empty(closed.Items);
        });
    }

    [Fact]
    public async Task System_or_foreign_tenant_context_is_not_a_usage_reader()
    {
        await Database(async (db, options) =>
        {
            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), true);
            await using var scope = new IdentityDbContext(options, system);
            var result = await new TenantUsageProjectionReader(scope, system,
                Sources, Definitions).ReadCurrentAsync(DateTime.UtcNow);
            Assert.Equal("tenant_owner_context_required", result.Reason);
            Assert.Empty(result.Items);

            var c = new TenantContext();
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            c.InitializeTenant(Guid.NewGuid(), a, [a], [a], b);
            await using var foreign = new IdentityDbContext(options, c);
            var denied = await new TenantUsageProjectionReader(foreign, c,
                Sources, Definitions).ReadCurrentAsync(DateTime.UtcNow);
            Assert.Equal("tenant_owner_context_required", denied.Reason);
            Assert.Empty(denied.Items);
        });
    }

    private static async Task Database(Func<IdentityDbContext,
        DbContextOptions<IdentityDbContext>, Task> run)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Local disposable SQL Server required");
        var connection = new SqlConnectionStringBuilder(raw);
        if (!connection.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(connection.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              connection.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Disposable localhost master required");
        connection.InitialCatalog = "FoundationTenantUsageTest_" + Guid.NewGuid().ToString("N");
        connection.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(connection.ConnectionString).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.MigrateAsync();
            await run(db, options);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
