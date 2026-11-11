using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// FR-17C-3A: real scoped SQL entitlement + delegated permission composition.
/// A test-only Commercial Module manifest is NEVER added to production source.
/// </summary>
public sealed class CommercialOperationPreflightSqlTests
{
    private static readonly ResourceContract[] Resources =
    [
        new("appointments", "bookings", ModuleProductClass.Commercial,
            ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"])
    ];

    private static readonly CapabilityContract[] Capabilities =
    [
        new("appointments.recurring", "appointments", "bookings",
            CapabilityKind.Feature, CapabilityValueKind.Boolean,
            CapabilityMergePolicy.Override),
        new("appointments.monthly", "appointments", "bookings",
            CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override, QuotaPeriodCadence.MonthlyAnniversary),
        new("appointments.max-active", "appointments", "bookings",
            CapabilityKind.CountLimit, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override),
        new("appointments.confirm-mode", "appointments", "bookings",
            CapabilityKind.AllowedModes, CapabilityValueKind.ModeSet,
            CapabilityMergePolicy.Override)
    ];

    private static readonly ResourceOperationContract[] Operations =
    [
        new("appointments", "bookings", "APPOINTMENTS.READ",
            "APPOINTMENTS.BOOKINGS.READ", []),
        new("appointments", "bookings", "APPOINTMENTS.CREATE",
            "APPOINTMENTS.BOOKINGS.CREATE",
            ["appointments.recurring", "appointments.monthly"]),
        new("appointments", "bookings", "APPOINTMENTS.CREATE",
            "APPOINTMENTS.BOOKINGS.CONFIRM",
            ["appointments.confirm-mode"]),
        new("appointments", "bookings", "APPOINTMENTS.CREATE",
            "APPOINTMENTS.BOOKINGS.ACTIVATE",
            ["appointments.max-active", "appointments.monthly"])
    ];

    [Fact]
    public async Task Licensed_tenant_still_needs_delegated_permission_and_atomic_metering()
    {
        await Database(async (db, options) =>
        {
            var tenantA = new Tenant { Name = "Licensed", Slug = "licensed-" + Guid.NewGuid().ToString("N") };
            var tenantB = new Tenant { Name = "Unlicensed", Slug = "unlicensed-" + Guid.NewGuid().ToString("N") };
            var module = new Module("appointments", "Appointments", null, 0);
            var resource = new Resource(module.Id, "bookings", "Bookings", null, 0);
            var read = new Permission(Guid.NewGuid(), "APPOINTMENTS.READ",
                resource.Id, "Read bookings", null);
            var create = new Permission(Guid.NewGuid(), "APPOINTMENTS.CREATE",
                resource.Id, "Create bookings", null);

            User MakeUser(Tenant tenant)
            {
                var id = Guid.NewGuid();
                return new User(id, tenant.Id, "user-" + id.ToString("N"),
                    null, "Test", "User", UserType.Tenant)
                {
                    PasswordHash = "test-only", SecurityStamp = "test-only",
                    NormalizedUserName = "USER-" + id.ToString("N").ToUpperInvariant()
                };
            }
            var userA = MakeUser(tenantA);
            var userB = MakeUser(tenantB);
            var plan = new CatalogPlan { Code = "test-composition", Name = "Test composition" };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Test composition"
            };
            db.Tenants.AddRange(tenantA, tenantB);
            db.Modules.Add(module);
            db.Resources.Add(resource);
            db.Permissions.AddRange(read, create);
            db.Users.AddRange(userA, userB);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
                { PlanVersionId = version.Id, ModuleId = module.Id });

            var recurring = new CatalogCapabilityDefinition
            {
                Code = "appointments.recurring", Title = "Recurring",
                ModuleId = module.Id, ResourceId = resource.Id,
                Kind = "Feature", ValueKind = "Boolean", MergePolicy = "Override"
            };
            var quota = new CatalogCapabilityDefinition
            {
                Code = "appointments.monthly", Title = "Monthly",
                ModuleId = module.Id, ResourceId = resource.Id,
                Kind = "PeriodQuota", ValueKind = "Integer", MergePolicy = "Override"
            };
            var modes = new CatalogCapabilityDefinition
            {
                Code = "appointments.confirm-mode", Title = "Confirmation",
                ModuleId = module.Id, ResourceId = resource.Id,
                Kind = "AllowedModes", ValueKind = "ModeSet", MergePolicy = "Override"
            };
            var active = new CatalogCapabilityDefinition
            {
                Code = "appointments.max-active", Title = "Active",
                ModuleId = module.Id, ResourceId = resource.Id,
                Kind = "CountLimit", ValueKind = "Integer", MergePolicy = "Override"
            };
            db.CatalogCapabilityDefinitions.AddRange(recurring, quota, modes, active);
            db.CatalogPlanVersionCapabilityValues.AddRange(
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = version.Id,
                    CapabilityDefinitionId = recurring.Id,
                    BooleanValue = true
                },
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = version.Id,
                    CapabilityDefinitionId = quota.Id,
                    IntegerValue = 5
                },
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = version.Id,
                    CapabilityDefinitionId = modes.Id,
                    ModeSetJson = "[\"manual\",\"auto\"]"
                },
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = version.Id,
                    CapabilityDefinitionId = active.Id,
                    IntegerValue = 1
                });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 10000m
            });
            db.UserPermissions.AddRange(
                new UserPermission { TenantId = tenantA.Id, UserId = userA.Id, PermissionId = read.Id, IsGranted = true },
                new UserPermission { TenantId = tenantB.Id, UserId = userB.Id, PermissionId = read.Id, IsGranted = true },
                new UserPermission { TenantId = tenantB.Id, UserId = userB.Id, PermissionId = create.Id, IsGranted = true });
            await db.SaveChangesAsync();

            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddHours(-1);
            version.ContentFingerprint = new string('d', 64);
            await db.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var order = new TenantPlanOrder
            {
                TenantId = tenantA.Id, PlanVersionId = version.Id,
                Status = "Settled", BillingCycle = "monthly", Currency = "IRR",
                GrossAmount = 10000m, DiscountAmount = 0m, NetAmount = 10000m,
                QuotedAtUtc = now.AddDays(-2), SettledAtUtc = now.AddDays(-1),
                SettlementKind = "ExternalManual", SettlementReference = "independently-reviewed-receipt",
                IdempotencyKey = "commercial-composition-test"
            };
            db.TenantPlanOrders.Add(order);
            db.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenantA.Id, PlanVersionId = version.Id,
                SourceOrderId = order.Id, Status = "Active",
                StartsAtUtc = now.AddHours(-2), EndsAtUtc = now.AddDays(5),
                ActivatedAtUtc = now.AddHours(-1)
            });
            await db.SaveChangesAsync();

            static TenantContext Context(Guid user, Guid tenant)
            {
                var current = new TenantContext();
                current.InitializeTenant(user, tenant, [tenant], [tenant], tenant);
                return current;
            }

            async Task<CommercialOperationPreflight> Check(
                Guid user, Guid tenant, string op, string? mode = null,
                bool useProductionManifest = false)
            {
                var context = Context(user, tenant);
                await using var scoped = new IdentityDbContext(options, context);
                var svc = useProductionManifest
                    ? new TenantCommercialOperationPreflight(scoped, context)
                    : new TenantCommercialOperationPreflight(scoped, context,
                        Resources, Capabilities, Operations);
                return await svc.CheckWriteAsync(op, mode);
            }

            Assert.True((await Check(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.READ")).CanRunWithoutMeter);

            // Commercial license does NOT create APPOINTMENTS.CREATE grants.
            Assert.Equal("permission_not_granted", (await Check(
                userA.Id, tenantA.Id, "APPOINTMENTS.BOOKINGS.CREATE")).Code);

            // Permission in another tenant never converts into a license.
            Assert.Equal("module_not_entitled", (await Check(
                userB.Id, tenantB.Id, "APPOINTMENTS.BOOKINGS.CREATE")).Code);

            // No production Commercial Module is deployed.
            Assert.Equal("operation_not_registered", (await Check(
                userA.Id, tenantA.Id, "APPOINTMENTS.BOOKINGS.READ",
                useProductionManifest: true)).Code);

            var grant = new UserPermission
            {
                TenantId = tenantA.Id, UserId = userA.Id,
                PermissionId = create.Id, IsGranted = true
            };
            db.UserPermissions.Add(grant);
            await db.SaveChangesAsync();

            var meter = await Check(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.CREATE");
            Assert.Equal("MeteringRequired", meter.State);
            Assert.False(meter.CanRunWithoutMeter);
            Assert.Equal(["appointments.monthly"], meter.MeteredCapabilities);

            // FR-17C-3B: test-only commercial domain table and source contract.
            // It is NOT part of a production module or migration.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE dbo.FR17C3B_TestWrites(
                  Id uniqueidentifier NOT NULL PRIMARY KEY,
                  TenantId uniqueidentifier NOT NULL,
                  IsActive bit NOT NULL);
                """);

            async Task<CommercialOperationResult> Execute(
                Guid actor, Guid tenant, string endpoint, string idempotency,
                CommercialCountRequirement[] countRequirements,
                CommercialQuotaRequirement[] quotaRequirements,
                bool throwDuringMutation = false, string? chosenMode = null)
            {
                var context = Context(actor, tenant);
                await using var scoped = new IdentityDbContext(options, context);
                return await new TenantCommercialOperationExecutor(
                    scoped, context, Resources, Capabilities, Operations)
                    .ExecuteAsync(endpoint, idempotency, countRequirements,
                        quotaRequirements,
                        async (database, token) =>
                        {
                            // CREATE inserts inactive, ACTIVATE adds a
                            // qualifying active entity guarded by CountLimit.
                            await database.Database.ExecuteSqlRawAsync(
                                "INSERT dbo.FR17C3B_TestWrites(Id,TenantId,IsActive) " +
                                "VALUES ({0},{1},{2})",
                                Guid.NewGuid(), tenant,
                                endpoint.EndsWith("ACTIVATE", StringComparison.Ordinal));
                            if (throwDuringMutation)
                                throw new InvalidOperationException("test business failure");
                        }, chosenMode);
            }

            static async Task<long> DomainCount(
                IdentityDbContext database, Guid tenant, CancellationToken token) =>
                await database.Database.SqlQueryRaw<long>(
                    "SELECT COUNT_BIG(*) AS [Value] FROM dbo.FR17C3B_TestWrites " +
                    "WHERE TenantId={0} AND IsActive=1", tenant)
                    .SingleAsync(token);

            var success = await Execute(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.CREATE", "exec-create-0001",
                [], [new("appointments.monthly", 2)]);
            Assert.True(success.Committed, success.Code);
            Assert.Equal("commercial_operation_committed", success.Code);

            Assert.Equal("duplicate_operation", (await Execute(userA.Id,
                tenantA.Id, "APPOINTMENTS.BOOKINGS.CREATE",
                "exec-create-0001", [], [new("appointments.monthly", 2)])).Code);
            Assert.Equal("idempotency_conflict", (await Execute(userA.Id,
                tenantA.Id, "APPOINTMENTS.BOOKINGS.CREATE",
                "exec-create-0001", [], [new("appointments.monthly", 3)])).Code);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Execute(userA.Id, tenantA.Id,
                    "APPOINTMENTS.BOOKINGS.CREATE", "exec-rollback-0001",
                    [], [new("appointments.monthly", 1)],
                    throwDuringMutation: true));

            Assert.Equal("missing_quota_adapter", (await Execute(userA.Id,
                tenantA.Id, "APPOINTMENTS.BOOKINGS.CREATE",
                "exec-missing-0001", [], [])).Code);
            Assert.Equal("module_not_entitled", (await Execute(userB.Id,
                tenantB.Id, "APPOINTMENTS.BOOKINGS.CREATE",
                "exec-unlicensed-0001", [], [new("appointments.monthly", 1)])).Code);

            // Two requests contend for only one allowed active entity. The
            // losing command MUST NOT charge quota or insert a domain row.
            var countRequirements = new[]
            {
                new CommercialCountRequirement(
                    "appointments.max-active", 1,
                    (database, token) => DomainCount(database, tenantA.Id, token))
            };
            async Task<CommercialOperationResult> Activate(string key) =>
                await Execute(userA.Id, tenantA.Id,
                    "APPOINTMENTS.BOOKINGS.ACTIVATE", key,
                    countRequirements, [new("appointments.monthly", 1)]);
            var outcomes = await Task.WhenAll(
                Task.Run(() => Activate("exec-activate-001")),
                Task.Run(() => Activate("exec-activate-002")));
            Assert.Single(outcomes, x => x.Committed);
            Assert.Single(outcomes, x => x.Code == "limit_exceeded");

            Assert.Equal("missing_count_adapter", (await Execute(
                userA.Id, tenantA.Id, "APPOINTMENTS.BOOKINGS.ACTIVATE",
                "exec-no-count-01", [], [new("appointments.monthly", 1)])).Code);

            var contextA = Context(userA.Id, tenantA.Id);
            await using (var checkDb = new IdentityDbContext(options, contextA))
            {
                Assert.Equal(1L, await DomainCount(checkDb, tenantA.Id, default));
                var totalWrites = await checkDb.Database.SqlQueryRaw<long>(
                    "SELECT COUNT_BIG(*) AS [Value] FROM dbo.FR17C3B_TestWrites " +
                    "WHERE TenantId={0}", tenantA.Id).SingleAsync();
                Assert.Equal(2L, totalWrites);
                var balance = await checkDb.Database.SqlQueryRaw<long>(
                    "SELECT ConsumedUnits AS [Value] FROM dbo.TenantUsageBalances " +
                    "WHERE TenantId={0} AND CapabilityCode={1}",
                    tenantA.Id, "appointments.monthly").SingleAsync();
                Assert.Equal(3L, balance);
            }

            Assert.Equal("mode_required", (await Check(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.CONFIRM")).Code);
            Assert.Equal("mode_unavailable", (await Check(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.CONFIRM", "unknown")).Code);
            Assert.True((await Check(userA.Id, tenantA.Id,
                "APPOINTMENTS.BOOKINGS.CONFIRM", "manual")).CanRunWithoutMeter);

            grant.IsGranted = false; // Direct deny defeats the licensed module.
            await db.SaveChangesAsync();
            Assert.Equal("permission_not_granted", (await Check(
                userA.Id, tenantA.Id, "APPOINTMENTS.BOOKINGS.CONFIRM", "manual")).Code);

            // Historical SQL subscription still exists; expired is not entitled.
            grant.IsGranted = true;
            var subscription = await db.TenantSubscriptions
                .IgnoreQueryFilters(["tenant"]).SingleAsync(x => x.TenantId == tenantA.Id);
            subscription.EndsAtUtc = now.AddMinutes(-1);
            await db.SaveChangesAsync();
            Assert.Equal("module_not_entitled", (await Check(
                userA.Id, tenantA.Id, "APPOINTMENTS.BOOKINGS.READ")).Code);

            // A foreign HomeTenant cannot impersonate B using A's user.
            Assert.Equal("permission_not_granted", (await Check(
                userA.Id, tenantB.Id, "APPOINTMENTS.BOOKINGS.READ")).Code);
        });
    }

    [Fact]
    public async Task System_context_and_unreviewed_source_operations_are_denied()
    {
        await Database(async (db, options) =>
        {
            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), true);
            await using var context = new IdentityDbContext(options, system);
            var access = new TenantCommercialOperationPreflight(
                context, system, Resources, Capabilities, Operations);
            Assert.Equal("writable_tenant_context_required",
                (await access.CheckWriteAsync("APPOINTMENTS.BOOKINGS.CREATE")).Code);
            Assert.Equal("operation_not_registered",
                (await access.CheckWriteAsync("APPOINTMENTS.UNKNOWN")).Code);

            var invalidOps = new ResourceOperationContract[]
            {
                new("appointments", "bookings", "SOMEONE.ELSE",
                    "APPOINTMENTS.BAD", [])
            };
            var unsafeSvc = new TenantCommercialOperationPreflight(
                context, system, Resources, Capabilities, invalidOps);
            Assert.Equal("invalid_source_catalog",
                (await unsafeSvc.CheckWriteAsync("APPOINTMENTS.BAD")).Code);
        });
    }

    private static async Task Database(
        Func<IdentityDbContext, DbContextOptions<IdentityDbContext>, Task> run)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Disposable SQL Server connection required");
        var admin = new SqlConnectionStringBuilder(raw);
        if (!admin.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(admin.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              admin.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only localhost disposable SQL Server master");
        admin.InitialCatalog = "FoundationCommercialGateTest_" + Guid.NewGuid().ToString("N");
        admin.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(admin.ConnectionString).Options;
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
