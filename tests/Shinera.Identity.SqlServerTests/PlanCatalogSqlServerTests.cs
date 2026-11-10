using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// FR-17B-1: real disposable SQL Server migrations, pinned parent versions,
/// non-commercial exclusion, typed limits, dynamic display and no grants.
/// No production SQL or third-party gateways are contacted.
/// </summary>
public sealed class PlanCatalogSqlServerTests
{
    [Fact]
    public async Task Published_child_inherits_pinned_parent_modules_and_overrides_typed_limits()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var appointments = new Module("appointments", "Appointments", null, 0);
            var staff = new Module("staff", "Staff", null, 1);
            var bookings = new Resource(appointments.Id, "bookings", "Bookings", null, 0);
            var members = new Resource(staff.Id, "members", "Members", null, 0);
            db.Modules.AddRange(appointments, staff);
            db.Resources.AddRange(bookings, members);

            var basePlan = new CatalogPlan
            {
                Code = "starter", Name = "Starter", IsActive = true
            };
            var pro = new CatalogPlan
            {
                Code = "pro", Name = "Pro", ParentPlanId = basePlan.Id,
                IsActive = true
            };
            db.CatalogPlans.AddRange(basePlan, pro);
            var baseV1 = new CatalogPlanVersion
            {
                PlanId = basePlan.Id, VersionNumber = 1,
                Status = "Published", DisplayTitle = "Starter",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-10),
                ContentFingerprint = new string('a', 64)
            };
            var proV1 = new CatalogPlanVersion
            {
                PlanId = pro.Id, VersionNumber = 1,
                ParentPlanVersionId = baseV1.Id,
                Status = "Published", DisplayTitle = "Pro",
                DisplaySubtitle = "For growing teams", BadgeText = "Popular",
                IsRecommended = true, PublishedAtUtc = DateTime.UtcNow.AddDays(-5),
                ContentFingerprint = new string('b', 64)
            };
            db.CatalogPlanVersions.AddRange(baseV1, proV1);
            db.CatalogPlanVersionModules.AddRange(
                new CatalogPlanVersionModule {
                    PlanVersionId = baseV1.Id, ModuleId = appointments.Id },
                new CatalogPlanVersionModule {
                    PlanVersionId = proV1.Id, ModuleId = staff.Id });

            var monthly = new CatalogCapabilityDefinition
            {
                Code = "appointments.monthly", Title = "Monthly bookings",
                ModuleId = appointments.Id, ResourceId = bookings.Id,
                Kind = "PeriodQuota", ValueKind = "Integer", MergePolicy = "Additive",
                Unit = "appointment"
            };
            var activeStaff = new CatalogCapabilityDefinition
            {
                Code = "staff.active.max", Title = "Active employees",
                ModuleId = staff.Id, ResourceId = members.Id,
                Kind = "CountLimit", ValueKind = "Integer", MergePolicy = "Override",
                Unit = "employee"
            };
            var recurring = new CatalogCapabilityDefinition
            {
                Code = "appointments.recurring", Title = "Recurring appointments",
                ModuleId = appointments.Id, ResourceId = bookings.Id,
                Kind = "Feature", ValueKind = "Boolean", MergePolicy = "Override"
            };
            db.CatalogCapabilityDefinitions.AddRange(monthly, activeStaff, recurring);
            db.CatalogPlanVersionCapabilityValues.AddRange(
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = baseV1.Id, CapabilityDefinitionId = monthly.Id,
                    IntegerValue = 100 },
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = baseV1.Id, CapabilityDefinitionId = recurring.Id,
                    BooleanValue = false },
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = proV1.Id, CapabilityDefinitionId = monthly.Id,
                    IntegerValue = 25 },
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = proV1.Id, CapabilityDefinitionId = activeStaff.Id,
                    IntegerValue = 10 },
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = proV1.Id, CapabilityDefinitionId = recurring.Id,
                    BooleanValue = true });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = proV1.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 2000000m
            });
            db.CatalogPlanDisplayItems.AddRange(
                new CatalogPlanDisplayItem {
                    PlanVersionId = proV1.Id, Kind = "Module",
                    ModuleId = staff.Id, Title = "Staff management", SortOrder = 0 },
                new CatalogPlanDisplayItem {
                    PlanVersionId = proV1.Id, Kind = "Capability",
                    CapabilityDefinitionId = activeStaff.Id,
                    Title = "10 active staff", SortOrder = 1 },
                new CatalogPlanDisplayItem {
                    PlanVersionId = proV1.Id, Kind = "Benefit",
                    Title = "Priority support", SortOrder = 2 });
            await db.SaveChangesAsync();

            var source = new[]
            {
                new ResourceContract("appointments", "bookings",
                    ModuleProductClass.Commercial,
                    ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"]),
                new ResourceContract("staff", "members",
                    ModuleProductClass.Commercial,
                    ["STAFF.READ", "STAFF.ACTIVATE"])
            };
            var caps = new[]
            {
                new CapabilityContract("appointments.monthly", "appointments", "bookings",
                    CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
                    CapabilityMergePolicy.Additive),
                new CapabilityContract("staff.active.max", "staff", "members",
                    CapabilityKind.CountLimit, CapabilityValueKind.Integer,
                    CapabilityMergePolicy.Override),
                new CapabilityContract("appointments.recurring", "appointments", "bookings",
                    CapabilityKind.Feature, CapabilityValueKind.Boolean,
                    CapabilityMergePolicy.Override)
            };

            var reader = new PublishedPlanCatalogReader(db);
            var result = await reader.ReadAsync(proV1.Id, source, caps, DateTime.UtcNow);
            Assert.Equal(["appointments", "staff"], result.ModuleCodes);
            Assert.Equal(125, Assert.Single(result.Capabilities,
                x => x.Code == "appointments.monthly").IntegerValue);
            Assert.Equal(10, Assert.Single(result.Capabilities,
                x => x.Code == "staff.active.max").IntegerValue);
            Assert.True(Assert.Single(result.Capabilities,
                x => x.Code == "appointments.recurring").BooleanValue);
            Assert.Equal(2000000m, Assert.Single(result.Prices).Amount);
            Assert.Equal(3, result.DisplayItems.Count);
            Assert.Equal("Priority support", result.DisplayItems[2].Title);
            Assert.True(result.IsRecommended);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            // Parent v2 is published later. Existing child v1 remains pinned
            // to Starter v1: no surprise license or price changes.
            var baseV2 = new CatalogPlanVersion
            {
                PlanId = basePlan.Id, VersionNumber = 2,
                Status = "Published", DisplayTitle = "Starter v2",
                PublishedAtUtc = DateTime.UtcNow,
                ContentFingerprint = new string('c', 64)
            };
            db.CatalogPlanVersions.Add(baseV2);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule {
                PlanVersionId = baseV2.Id, ModuleId = staff.Id });
            db.CatalogPlanVersionCapabilityValues.Add(
                new CatalogPlanVersionCapabilityValue {
                    PlanVersionId = baseV2.Id, CapabilityDefinitionId = monthly.Id,
                    IntegerValue = 10000
                });
            await db.SaveChangesAsync();
            var afterParentUpdated = await reader.ReadAsync(
                proV1.Id, source, caps, DateTime.UtcNow);
            Assert.Equal(125, Assert.Single(afterParentUpdated.Capabilities,
                x => x.Code == "appointments.monthly").IntegerValue);
            Assert.Equal(2, afterParentUpdated.ModuleCodes.Count);
            Assert.Empty(await db.TenantModules.ToListAsync());
        });
    }

    [Fact]
    public async Task Unpublished_plan_and_core_Module_are_not_licensed()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var sys = new Module("system", "System", null, 0);
            db.Modules.Add(sys);
            var plan = new CatalogPlan { Code = "unsafe", Name = "Unsafe" };
            db.CatalogPlans.Add(plan);
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Draft"
            };
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
            { PlanVersionId = version.Id, ModuleId = sys.Id });
            await db.SaveChangesAsync();

            var reader = new PublishedPlanCatalogReader(db);
            var systemOnly = new[] {
                new ResourceContract("system", "users",
                    ModuleProductClass.PlatformCore, ["USERS.READ"])
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reader.ReadAsync(version.Id, systemOnly, [], DateTime.UtcNow));

            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1);
            version.ContentFingerprint = new string('d', 64);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reader.ReadAsync(version.Id, systemOnly, [], DateTime.UtcNow));

            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
        });
    }

    [Fact]
    public async Task SQL_rejects_negative_price_and_invalid_typed_limit()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var plan = new CatalogPlan { Code = "test", Name = "Test" };
            db.CatalogPlans.Add(plan);
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Test"
            };
            db.CatalogPlanVersions.Add(version);
            await db.SaveChangesAsync();

            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = -10m
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var def = new CatalogCapabilityDefinition
            {
                Code = "test.monthly", Title = "Monthly",
                ModuleId = Guid.NewGuid(), Kind = "PeriodQuota",
                ValueKind = "Integer", MergePolicy = "Additive"
            };
            // FK prevents orphan capability definition; no arbitrary feature
            // record may refer to a nonexistent module.
            db.CatalogCapabilityDefinitions.Add(def);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    private static async Task WithDisposableDatabaseAsync(Func<IdentityDbContext, Task> action)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "SHINERA_SQLSERVER_TEST_CONNECTION required for disposable SQL Server tests");

        var admin = new SqlConnectionStringBuilder(raw);
        if (!admin.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(admin.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              admin.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tests require loopback SQL Server master");

        admin.InitialCatalog = "FoundationPlanCatalogTest_" + Guid.NewGuid().ToString("N");
        admin.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(admin.ConnectionString).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        try
        {
            await db.Database.MigrateAsync();
            await action(db);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
