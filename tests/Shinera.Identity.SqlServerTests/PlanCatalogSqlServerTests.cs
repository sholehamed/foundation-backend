using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Web.Endpoints;
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
                Status = "Draft", DisplayTitle = "Starter"
            };
            var proV1 = new CatalogPlanVersion
            {
                PlanId = pro.Id, VersionNumber = 1,
                ParentPlanVersionId = baseV1.Id,
                Status = "Draft", DisplayTitle = "Pro",
                DisplaySubtitle = "For growing teams", BadgeText = "Popular",
                IsRecommended = true
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
                    PlanVersionId = baseV1.Id, Kind = "Module",
                    ModuleId = appointments.Id, Title = "Starter appointments", SortOrder = 0 },
                new CatalogPlanDisplayItem {
                    PlanVersionId = baseV1.Id, Kind = "Capability",
                    CapabilityDefinitionId = recurring.Id,
                    Title = "Recurring appointments", SortOrder = 1 },
                new CatalogPlanDisplayItem {
                    PlanVersionId = proV1.Id, Kind = "Module",
                    ModuleId = appointments.Id, Title = "Professional appointments",
                    SortOrder = 0 },
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
            // FR-17B-2C: SQL publishes a populated Draft. It never accepts
            // children inserted directly into an already Published revision.
            baseV1.Status = "Published";
            baseV1.PublishedAtUtc = DateTime.UtcNow.AddDays(-10);
            baseV1.ContentFingerprint = new string('a', 64);
            proV1.Status = "Published";
            proV1.PublishedAtUtc = DateTime.UtcNow.AddDays(-5);
            proV1.ContentFingerprint = new string('b', 64);
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
            Assert.Equal(new[] { "appointments", "staff" }, result.ModuleCodes);
            Assert.Equal(125, Assert.Single(result.Capabilities,
                x => x.Code == "appointments.monthly").IntegerValue);
            Assert.Equal(10, Assert.Single(result.Capabilities,
                x => x.Code == "staff.active.max").IntegerValue);
            Assert.True(Assert.Single(result.Capabilities,
                x => x.Code == "appointments.recurring").BooleanValue);
            Assert.Equal(2000000m, Assert.Single(result.Prices).Amount);
            Assert.Equal(5, result.DisplayItems.Count);
            Assert.Contains(result.DisplayItems,
                item => item.Title == "Professional appointments");
            Assert.DoesNotContain(result.DisplayItems,
                item => item.Title == "Starter appointments");
            Assert.Contains(result.DisplayItems,
                item => item.Title == "Recurring appointments");
            Assert.Contains(result.DisplayItems,
                item => item.Title == "Priority support");
            Assert.True(result.IsRecommended);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToListAsync());

            // Parent v2 is published later. Existing child v1 remains pinned
            // to Starter v1: no surprise license or price changes.
            var baseV2 = new CatalogPlanVersion
            {
                PlanId = basePlan.Id, VersionNumber = 2,
                Status = "Draft", DisplayTitle = "Starter v2"
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
            baseV2.Status = "Published";
            baseV2.PublishedAtUtc = DateTime.UtcNow;
            baseV2.ContentFingerprint = new string('c', 64);
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

    [Fact]
    public async Task Public_cards_only_show_latest_saleable_version_and_never_create_user_grants()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var module = new Module("appointments", "Appointments", null, 0);
            db.Modules.Add(module);
            var plan = new CatalogPlan { Code = "starter", Name = "Starter", IsActive = true };
            var draftPlan = new CatalogPlan { Code = "draft", Name = "Draft" };
            db.CatalogPlans.AddRange(plan, draftPlan);

            var published = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "پلن پایه"
            };
            var newer = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 2, Status = "Draft",
                DisplayTitle = "پلن جدید", IsRecommended = true
            };
            var draft = new CatalogPlanVersion
            {
                PlanId = draftPlan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "unpublished"
            };
            db.CatalogPlanVersions.AddRange(published, newer, draft);
            db.CatalogPlanVersionModules.AddRange(
                new CatalogPlanVersionModule { ModuleId = module.Id,
                    PlanVersionId = published.Id },
                new CatalogPlanVersionModule { ModuleId = module.Id,
                    PlanVersionId = newer.Id },
                new CatalogPlanVersionModule { ModuleId = module.Id,
                    PlanVersionId = draft.Id });
            db.CatalogPlanPriceOffers.AddRange(
                new CatalogPlanPriceOffer { PlanVersionId = published.Id,
                    BillingCycle = "monthly", Currency = "IRR", Amount = 9000 },
                new CatalogPlanPriceOffer { PlanVersionId = newer.Id,
                    BillingCycle = "monthly", Currency = "IRR", Amount = 12000 },
                new CatalogPlanPriceOffer { PlanVersionId = draft.Id,
                    BillingCycle = "monthly", Currency = "IRR", Amount = 1 });
            db.CatalogPlanDisplayItems.AddRange(
                new CatalogPlanDisplayItem { PlanVersionId = newer.Id,
                    Kind = "Module", ModuleId = module.Id, Title = "نوبت‌دهی" },
                new CatalogPlanDisplayItem { PlanVersionId = newer.Id,
                    Kind = "Benefit", Title = "پشتیبانی ویژه", SortOrder = 1 });
            await db.SaveChangesAsync();
            published.Status = "Published";
            published.PublishedAtUtc = DateTime.UtcNow.AddDays(-2);
            published.ContentFingerprint = new string('a', 64);
            newer.Status = "Published";
            newer.PublishedAtUtc = DateTime.UtcNow.AddDays(-1);
            newer.ContentFingerprint = new string('b', 64);
            await db.SaveChangesAsync();

            var definitions = new[] { new ResourceContract(
                "appointments", "bookings", ModuleProductClass.Commercial,
                ["APPOINTMENTS.READ"]) };
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            var cards = await PublicPlans.ReadCardsAsync(
                db, definitions, [], DateTime.UtcNow, logger);
            var card = Assert.Single(cards);
            Assert.Equal("starter", card.Code);
            Assert.Equal(newer.Id, card.VersionId);
            Assert.Equal("پلن جدید", card.Title);
            Assert.True(card.IsRecommended);
            Assert.Equal(12000m, Assert.Single(card.Prices).Amount);
            Assert.Equal("IRR", card.Prices[0].Currency);
            Assert.Equal(new[] { "نوبت‌دهی", "پشتیبانی ویژه" },
                card.Features.Select(x => x.Title).ToArray());

            // No unregistered commercial definitions => fail-closed rather
            // than selling a DB row that does not correspond to deployed code.
            var noDeployedModule = await PublicPlans.ReadCardsAsync(
                db, ModuleResourceContracts.Current(), [], DateTime.UtcNow, logger);
            Assert.Empty(noDeployedModule);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.TenantModules.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Public_cards_are_empty_when_nothing_was_published()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var cards = await PublicPlans.ReadCardsAsync(
                db, [], [], DateTime.UtcNow,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            Assert.Empty(cards);
        });
    }

    [Fact]
    public async Task Admin_draft_publish_requires_vetted_module_review_fingerprint_and_is_immutable_via_api()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var module = new Module("appointments", "Appointments", null, 0);
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var approved = new[]
            {
                new ResourceContract("appointments", "bookings",
                    ModuleProductClass.Commercial, ["APPOINTMENTS.READ"])
            };
            var admin = new SystemPlanCatalogAdminService(db, approved);
            var created = await admin.CreateAsync(new(
                "starter", "Starter", null, null, "شروع", "پلن پایه"),
                CancellationToken.None);
            Assert.True(created.Ok);
            var draftId = created.Id!.Value;
            var empty = await admin.PreviewAsync(draftId, CancellationToken.None);
            Assert.False(empty.CanPublish);
            Assert.Contains("no_commercial_module", empty.Issues);
            Assert.Contains("no_current_valid_price", empty.Issues);
            Assert.False((await admin.PublishAsync(
                draftId, empty.Fingerprint, CancellationToken.None)).Ok);

            var saved = await admin.ReplaceDraftAsync(draftId, new(
                "انفرادی", "مناسب شروع", "جدید", true, 1,
                ["appointments"],
                [new("monthly", "IRR", 300000m)],
                [],
                [new("Module", "appointments", "نوبت‌دهی", null, true, 0),
                 new("Benefit", null, "پشتیبانی", null, false, 1)]),
                CancellationToken.None);
            Assert.True(saved.Ok);
            var persisted = await admin.ReadDraftAsync(draftId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal("appointments", Assert.Single(persisted.Content.ModuleCodes!));
            Assert.Equal(300000m, Assert.Single(persisted.Content.Prices!).Amount);

            var preview = await admin.PreviewAsync(draftId, CancellationToken.None);
            Assert.True(preview.CanPublish, string.Join(",", preview.Issues));
            Assert.Equal(["appointments"], preview.EffectiveModuleCodes);
            Assert.Equal(64, preview.Fingerprint.Length);

            var change = await admin.ReplaceDraftAsync(draftId, new(
                "انفرادی", "مناسب شروع", "جدید", true, 1,
                ["appointments"],
                [new("monthly", "IRR", 400000m)],
                [],
                [new("Module", "appointments", "نوبت‌دهی", null, true, 0)]),
                CancellationToken.None);
            Assert.True(change.Ok);
            var stale = await admin.PublishAsync(draftId, preview.Fingerprint, CancellationToken.None);
            Assert.False(stale.Ok);
            Assert.Equal("stale_preview", stale.Code);

            var fresh = await admin.PreviewAsync(draftId, CancellationToken.None);
            var publish = await admin.PublishAsync(draftId, fresh.Fingerprint, CancellationToken.None);
            Assert.True(publish.Ok);
            var version = await db.CatalogPlanVersions.AsNoTracking()
                .SingleAsync(x => x.Id == draftId);
            Assert.Equal("Published", version.Status);
            Assert.Equal(fresh.Fingerprint, version.ContentFingerprint);
            Assert.Null(await admin.ReadDraftAsync(draftId, CancellationToken.None));
            Assert.Equal("published_immutable", (await admin.ReplaceDraftAsync(
                draftId, new("another", null, null, false, 0, [], [], [], []),
                CancellationToken.None)).Code);
            Assert.False((await admin.PublishAsync(draftId, fresh.Fingerprint, CancellationToken.None)).Ok);

            var cards = await PublicPlans.ReadCardsAsync(db, approved, [],
                DateTime.UtcNow,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            Assert.Equal("starter", Assert.Single(cards).Code);
            Assert.Equal(400000m, Assert.Single(cards[0].Prices).Amount);

            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.TenantModules.ToArrayAsync());
            var newDraft = await admin.CreateRevisionAsync(
                (await db.CatalogPlans.SingleAsync()).Id, CancellationToken.None);
            Assert.True(newDraft.Ok);
            var edited = await admin.ReadDraftAsync(newDraft.Id!.Value, CancellationToken.None);
            Assert.Equal(400000m, Assert.Single(edited!.Content.Prices!).Amount);
            Assert.Equal("Draft", (await db.CatalogPlanVersions.AsNoTracking()
                .SingleAsync(x => x.Id == newDraft.Id)).Status);
        });
    }

    [Fact]
    public async Task Admin_catalog_never_publishes_core_module_or_invalid_price()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var sys = new Module("system", "System", null, 0);
            db.Modules.Add(sys);
            await db.SaveChangesAsync();
            var admin = new SystemPlanCatalogAdminService(db);
            var created = await admin.CreateAsync(
                new("test", "Test", null, null, "Test", null), CancellationToken.None);
            Assert.True(created.Ok);
            var id = created.Id!.Value;
            var proposed = await admin.ReplaceDraftAsync(id,
                new("Test", null, null, false, 0,
                    ["system"], [new("monthly", "IRR", 2000m)], [], []),
                CancellationToken.None);
            Assert.False(proposed.Ok);
            Assert.Equal("module_not_code_owned_commercial", proposed.Code);
            Assert.False((await admin.PublishAsync(id,
                new string('a', 64), CancellationToken.None)).Ok);
            Assert.False((await admin.ReplaceDraftAsync(id,
                new("Test", null, null, false, 0,
                    [], [new("monthly", "IRR", -1m)], [], []),
                CancellationToken.None)).Ok);
            var plans = await admin.ListAsync(CancellationToken.None);
            Assert.Single(plans);
            Assert.Equal("test", plans[0].Code);
            Assert.NotNull(plans[0].DraftVersionId);
        });
    }

    [Fact]
    public async Task Database_triggers_reject_direct_mutation_of_published_plan_and_every_child_table()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var module = new Module("appointments", "Appointments", null, 0);
            var plan = new CatalogPlan { Code = "starter", Name = "Starter", IsActive = true };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1,
                Status = "Draft", DisplayTitle = "Starter"
            };
            var nextDraft = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 2,
                Status = "Draft", DisplayTitle = "Draft two"
            };
            var definition = new CatalogCapabilityDefinition
            {
                ModuleId = module.Id, Code = "appointments.recurring",
                Title = "Recurring", Kind = "Feature", ValueKind = "Boolean",
                MergePolicy = "Override", IsActive = true
            };
            var owned = new CatalogPlanVersionModule
            {
                PlanVersionId = version.Id, ModuleId = module.Id
            };
            var price = new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 100m, IsActive = true
            };
            var draftPrice = new CatalogPlanPriceOffer
            {
                PlanVersionId = nextDraft.Id, BillingCycle = "yearly",
                Currency = "IRR", Amount = 42m, IsActive = true
            };
            var capability = new CatalogPlanVersionCapabilityValue
            {
                PlanVersionId = version.Id, CapabilityDefinitionId = definition.Id,
                BooleanValue = true
            };
            var featureText = new CatalogPlanDisplayItem
            {
                PlanVersionId = version.Id, Kind = "Module",
                ModuleId = module.Id, Title = "Appointments"
            };
            db.Modules.Add(module);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.AddRange(version, nextDraft);
            db.CatalogCapabilityDefinitions.Add(definition);
            db.CatalogPlanVersionModules.Add(owned);
            db.CatalogPlanPriceOffers.AddRange(price, draftPrice);
            db.CatalogPlanVersionCapabilityValues.Add(capability);
            db.CatalogPlanDisplayItems.Add(featureText);
            await db.SaveChangesAsync();

            // Original Draft is normally editable, and publication itself
            // is an UPDATE of Draft status plus reviewed metadata.
            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow;
            version.ContentFingerprint = new string('a', 64);
            await db.SaveChangesAsync();

            static async Task CheckAsync(Func<Task<int>> query, int error)
            {
                var thrown = await Assert.ThrowsAsync<SqlException>(query);
                Assert.Equal(error, thrown.Number);
            }

            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanVersions SET DisplayTitle = {"Tampered"} WHERE Id = {version.Id}"), 51022);
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanVersions SET Status = {"Draft"} WHERE Id = {version.Id}"), 51022);
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlans SET Name = {"New plan identity"} WHERE Id = {plan.Id}"), 51024);

            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.CatalogPlanVersionModules WHERE Id = {owned.Id}"), 51025);
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanVersionCapabilityValues SET BooleanValue = {false} WHERE Id = {capability.Id}"), 51026);
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanPriceOffers SET Amount = {250m} WHERE Id = {price.Id}"), 51027);
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanDisplayItems SET Title = {"Fake feature"} WHERE Id = {featureText.Id}"), 51028);

            // A published child cannot be moved to Draft in order to edit it.
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanPriceOffers SET PlanVersionId = {nextDraft.Id} WHERE Id = {price.Id}"), 51027);

            // Mixed Published+Draft statement is all-or-nothing. The unrelated
            // Draft row must retain its old price after the trigger throws.
            await CheckAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanPriceOffers SET Amount = {500m} WHERE Id = {price.Id} OR Id = {draftPrice.Id}"), 51027);
            Assert.Equal(42m, (await db.CatalogPlanPriceOffers.AsNoTracking()
                .SingleAsync(x => x.Id == draftPrice.Id)).Amount);

            // A Draft row is still writable; the triggers aren't a global
            // freeze of commercial authoring.
            Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CatalogPlanPriceOffers SET Amount = {48m} WHERE Id = {draftPrice.Id}"));
            Assert.Equal(48m, (await db.CatalogPlanPriceOffers.AsNoTracking()
                .SingleAsync(x => x.Id == draftPrice.Id)).Amount);

            // Even if an application tries to INSERT an additional offer after
            // publication, SQL fails, instead of EF attempting to bypass it.
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "yearly",
                Currency = "IRR", Amount = 1200m, IsActive = true
            });
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.Equal(51027, Assert.IsType<SqlException>(rejected.InnerException).Number);
            db.ChangeTracker.Clear();

            // A newly inserted row can never start life as Published/Retired.
            db.CatalogPlanVersions.Add(new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 3, Status = "Published",
                DisplayTitle = "Forged", PublishedAtUtc = DateTime.UtcNow,
                ContentFingerprint = new string('b', 64)
            });
            rejected = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.Equal(51021, Assert.IsType<SqlException>(rejected.InnerException).Number);
            db.ChangeTracker.Clear();

            Assert.Equal("Starter", (await db.CatalogPlans.AsNoTracking()
                .SingleAsync(x => x.Id == plan.Id)).Name);
            Assert.Equal("Starter", (await db.CatalogPlanVersions.AsNoTracking()
                .SingleAsync(x => x.Id == version.Id)).DisplayTitle);
            Assert.Equal(100m, (await db.CatalogPlanPriceOffers.AsNoTracking()
                .SingleAsync(x => x.Id == price.Id)).Amount);
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.TenantModules.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Shadow_subscription_is_tenant_scoped_and_requires_settled_same_tenant_order()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var a = new Tenant { Name = "Tenant A", Slug = "tenant-a-shadow" };
            var b = new Tenant { Name = "Tenant B", Slug = "tenant-b-shadow" };
            var module = new Module("appointments", "Appointments", null, 0);
            var plan = new CatalogPlan { Code = "starter-shadow", Name = "Starter", IsActive = true };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Starter"
            };
            db.Tenants.AddRange(a, b);
            db.Modules.Add(module);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
                { PlanVersionId = version.Id, ModuleId = module.Id });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 10000m, IsActive = true
            });
            await db.SaveChangesAsync();
            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddMinutes(-10);
            version.ContentFingerprint = new string('b', 64);
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            var orderA = new TenantPlanOrder
            {
                TenantId = a.Id, PlanVersionId = version.Id,
                Status = "Settled", BillingCycle = "monthly",
                Currency = "IRR", GrossAmount = 10000m,
                DiscountAmount = 0m, NetAmount = 10000m,
                QuotedAtUtc = now.AddDays(-2),
                SettledAtUtc = now.AddDays(-1),
                SettlementKind = "GatewayVerified",
                SettlementReference = "test-verified-payment-a",
                IdempotencyKey = "test-order-a"
            };
            var orderB = new TenantPlanOrder
            {
                TenantId = b.Id, PlanVersionId = version.Id,
                Status = "Pending", BillingCycle = "monthly",
                Currency = "IRR", GrossAmount = 10000m,
                DiscountAmount = 10000m, NetAmount = 0m,
                QuotedAtUtc = now.AddDays(-1),
                IdempotencyKey = "test-order-b"
            };
            db.TenantPlanOrders.AddRange(orderA, orderB);
            var subscriptionA = new TenantSubscription
            {
                TenantId = a.Id, PlanVersionId = version.Id,
                SourceOrderId = orderA.Id, Status = "Active",
                StartsAtUtc = now.AddHours(-2), EndsAtUtc = now.AddDays(30),
                ActivatedAtUtc = now.AddHours(-1)
            };
            var subscriptionB = new TenantSubscription
            {
                TenantId = b.Id, PlanVersionId = version.Id,
                SourceOrderId = orderB.Id, Status = "Pending",
                StartsAtUtc = now.AddHours(-1), EndsAtUtc = now.AddDays(30)
            };
            db.TenantSubscriptions.AddRange(subscriptionA, subscriptionB);
            // A legacy toggle is never evidence of a paid Module license.
            db.TenantModules.Add(new TenantModule
            {
                TenantId = b.Id, ModuleId = module.Id, IsEnabled = true
            });
            await db.SaveChangesAsync();

            var sources = new[]
            {
                new ResourceContract("appointments", "bookings",
                    ModuleProductClass.Commercial, ["APPOINTMENTS.READ"])
            };
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(db.Database.GetConnectionString()).Options;

            var contextA = new TenantContext();
            contextA.InitializeTenant(Guid.NewGuid(), a.Id, [a.Id], [a.Id], a.Id);
            await using (var dbA = new IdentityDbContext(options, contextA))
            {
                var eligible = await new TenantCommercialEntitlementReader(dbA, contextA)
                    .ReadCurrentAsync(now, sources, []);
                Assert.True(eligible.HasCommercialEntitlement);
                Assert.Equal("Active", eligible.Status);
                Assert.Equal(version.Id, eligible.PlanVersionId);
                Assert.Equal(new[] { "appointments" }, eligible.ModuleCodes);
                Assert.Empty(eligible.Capabilities);

                // Real source manifest still has zero Commercial modules, so
                // a production request NEVER advertises this test-only fixture.
                var noCommercial = await new TenantCommercialEntitlementReader(dbA, contextA)
                    .ReadCurrentAsync(now);
                Assert.False(noCommercial.HasCommercialEntitlement);
                Assert.Empty(noCommercial.ModuleCodes);
            }

            var contextB = new TenantContext();
            contextB.InitializeTenant(Guid.NewGuid(), b.Id, [b.Id], [b.Id], b.Id);
            await using (var dbB = new IdentityDbContext(options, contextB))
            {
                var pending = await new TenantCommercialEntitlementReader(dbB, contextB)
                    .ReadCurrentAsync(now, sources, []);
                Assert.False(pending.HasCommercialEntitlement);
                Assert.Equal("Pending", pending.Status);
                Assert.Empty(pending.ModuleCodes);
                // B cannot read A's single settled order via the tenant filter.
                Assert.Empty(await dbB.TenantPlanOrders
                    .Where(x => x.Id == orderA.Id).ToArrayAsync());
            }

            // Even a forged Active flag cannot borrow another Tenant's order.
            // Use a *different* A order, since SQL also enforces one
            // Subscription per SourceOrderId.
            var borrowedOrderA = new TenantPlanOrder
            {
                TenantId = a.Id, PlanVersionId = version.Id,
                Status = "Settled", BillingCycle = "monthly",
                Currency = "IRR", GrossAmount = 10000m,
                DiscountAmount = 0m, NetAmount = 10000m,
                QuotedAtUtc = now.AddDays(-2),
                SettledAtUtc = now.AddDays(-1),
                SettlementKind = "GatewayVerified",
                SettlementReference = "test-borrowed-a",
                IdempotencyKey = "test-order-a-2"
            };
            db.TenantPlanOrders.Add(borrowedOrderA);
            await db.SaveChangesAsync();
            subscriptionB.Status = "Active";
            subscriptionB.SourceOrderId = borrowedOrderA.Id;
            subscriptionB.ActivatedAtUtc = now.AddMinutes(-5);
            await db.SaveChangesAsync();
            await using (var dbB = new IdentityDbContext(options, contextB))
            {
                var borrowed = await new TenantCommercialEntitlementReader(dbB, contextB)
                    .ReadCurrentAsync(now, sources, []);
                Assert.False(borrowed.HasCommercialEntitlement);
                Assert.Equal("Invalid", borrowed.Status);
                Assert.Empty(borrowed.ModuleCodes);
            }
            subscriptionB.SourceOrderId = orderB.Id;
            await db.SaveChangesAsync();
            await using (var dbB = new IdentityDbContext(options, contextB))
            {
                var unsettled = await new TenantCommercialEntitlementReader(dbB, contextB)
                    .ReadCurrentAsync(now, sources, []);
                Assert.False(unsettled.HasCommercialEntitlement);
                Assert.Equal("Invalid", unsettled.Status);
            }

            // Also prevent an authenticated System principal from masquerading
            // as a Tenant just by supplying an arbitrary TenantId.
            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), true);
            await using (var systemDb = new IdentityDbContext(options, system))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    new TenantCommercialEntitlementReader(systemDb, system)
                        .ReadCurrentAsync(now, sources, []));
            }

            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
        });
    }

    [Fact]
    public async Task Billing_schema_rejects_negative_balance_and_active_without_settlement_reference()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var tenant = new Tenant { Name = "Billing", Slug = "billing-shadow" };
            var plan = new CatalogPlan { Code = "billing", Name = "Billing" };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Billing"
            };
            db.Tenants.Add(tenant);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            await db.SaveChangesAsync();

            db.TenantPlanOrders.Add(new TenantPlanOrder
            {
                TenantId = tenant.Id, PlanVersionId = version.Id,
                Status = "Pending", Currency = "IRR",
                GrossAmount = 100m, DiscountAmount = 150m, NetAmount = -50m,
                BillingCycle = "monthly", QuotedAtUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenant.Id, PlanVersionId = version.Id,
                Status = "Active",
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                ActivatedAtUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.Empty(await db.TenantSubscriptions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
        });
    }

    [Fact]
    public async Task Tenant_order_is_server_priced_idempotent_and_system_attestation_activates_once()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var a = new Tenant { Name = "Checkout A", Slug = "checkout-a" };
            var b = new Tenant { Name = "Checkout B", Slug = "checkout-b" };
            var module = new Module("appointments", "Appointments", null, 0);
            var plan = new CatalogPlan
                { Code = "checkout", Name = "Checkout", IsActive = true };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Checkout"
            };
            db.Tenants.AddRange(a, b);
            db.Modules.Add(module);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
                { PlanVersionId = version.Id, ModuleId = module.Id });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 250000m, IsActive = true
            });
            await db.SaveChangesAsync();
            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddHours(-1);
            version.ContentFingerprint = new string('d', 64);
            await db.SaveChangesAsync();

            var sources = new[]
            {
                new ResourceContract("appointments", "bookings",
                    ModuleProductClass.Commercial, ["APPOINTMENTS.READ"])
            };
            var opts = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(db.Database.GetConnectionString()).Options;
            var now = DateTime.UtcNow;

            var tenantA = new TenantContext();
            tenantA.InitializeTenant(Guid.NewGuid(), a.Id, [a.Id], [a.Id], a.Id);
            Guid orderId;
            await using (var scoped = new IdentityDbContext(opts, tenantA))
            {
                var checkout = new TenantPlanCheckoutService(scoped, sources);
                var request = new TenantPlanCheckoutService.CreateOrder(
                    version.Id, "monthly", "checkout-request-0001");
                var created = await checkout.CreatePendingAsync(
                    a.Id, request, now, CancellationToken.None);
                Assert.True(created.Ok, created.Code);
                Assert.Equal("Pending", created.Order!.Status);
                Assert.Equal(250000m, created.Order.GrossAmount);
                Assert.Equal(0m, created.Order.DiscountAmount);
                Assert.Equal(250000m, created.Order.NetAmount);
                orderId = created.Order.Id;
                var replay = await checkout.CreatePendingAsync(
                    a.Id, request, now, CancellationToken.None);
                Assert.Equal("existing_order", replay.Code);
                Assert.Equal(orderId, replay.Order!.Id);
                var changed = await checkout.CreatePendingAsync(
                    a.Id, request with { BillingCycle = "yearly" },
                    now, CancellationToken.None);
                Assert.False(changed.Ok);
                Assert.Equal("idempotency_conflict", changed.Code);
                // Even a second valid UUID for the same plan cannot create a
                // second unpaid quote after a lost response/browser refresh.
                var browserRetry = await checkout.CreatePendingAsync(a.Id,
                    request with { IdempotencyKey = "checkout-request-0001-retry" },
                    now, CancellationToken.None);
                Assert.True(browserRetry.Ok);
                Assert.Equal("existing_pending", browserRetry.Code);
                Assert.Equal(orderId, browserRetry.Order!.Id);
                Assert.Single(await checkout.ListForTenantAsync(a.Id, CancellationToken.None));
            }

            var tenantB = new TenantContext();
            tenantB.InitializeTenant(Guid.NewGuid(), b.Id, [b.Id], [b.Id], b.Id);
            await using (var other = new IdentityDbContext(opts, tenantB))
            {
                var checkout = new TenantPlanCheckoutService(other, sources);
                Assert.Empty(await checkout.ListForTenantAsync(b.Id, CancellationToken.None));
                Assert.Empty(await other.TenantPlanOrders.ToArrayAsync());
            }

            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), true);
            Guid subscriptionId;
            await using (var systemDb = new IdentityDbContext(opts, system))
            {
                var checkout = new TenantPlanCheckoutService(systemDb, sources);
                Assert.Contains(await checkout.ListForSystemAsync("Pending", CancellationToken.None),
                    x => x.Id == orderId);
                var noAttestation = await checkout.AttestExternalSettlementAsync(
                    orderId, Guid.NewGuid(), new("receipt-101", false),
                    now.AddMinutes(5), CancellationToken.None);
                Assert.False(noAttestation.Ok);

                var attestor = Guid.NewGuid();
                var paid = await checkout.AttestExternalSettlementAsync(
                    orderId, attestor, new("receipt-101", true),
                    now.AddMinutes(5), CancellationToken.None);
                Assert.True(paid.Ok, paid.Code);
                Assert.Equal("Settled", paid.Order!.Status);
                Assert.Equal("ExternalManual", paid.Order.SettlementKind);
                subscriptionId = paid.SubscriptionId!.Value;

                var replay = await checkout.AttestExternalSettlementAsync(
                    orderId, attestor, new("receipt-101", true),
                    now.AddMinutes(6), CancellationToken.None);
                Assert.True(replay.Ok);
                Assert.Equal("already_settled", replay.Code);
                Assert.Equal(subscriptionId, replay.SubscriptionId);

                var differentReceipt = await checkout.AttestExternalSettlementAsync(
                    orderId, attestor, new("receipt-102", true),
                    now.AddMinutes(7), CancellationToken.None);
                Assert.Equal("already_settled_conflict", differentReceipt.Code);

                var settled = await systemDb.TenantPlanOrders
                    .IgnoreQueryFilters(["tenant"]).AsNoTracking()
                    .SingleAsync(x => x.Id == orderId);
                Assert.Equal(attestor, settled.LastModifiedBy);
                var license = await systemDb.TenantSubscriptions
                    .IgnoreQueryFilters(["tenant"]).AsNoTracking()
                    .SingleAsync(x => x.SourceOrderId == orderId);
                Assert.Equal(a.Id, license.TenantId);
                Assert.Equal(version.Id, license.PlanVersionId);
                Assert.Equal("Active", license.Status);
                Assert.Equal(now.AddMinutes(5).AddMonths(1), license.EndsAtUtc);
            }

            await using (var scoped = new IdentityDbContext(opts, tenantA))
            {
                var snapshot = await new TenantCommercialEntitlementReader(scoped, tenantA)
                    .ReadCurrentAsync(now.AddHours(1), sources, []);
                Assert.True(snapshot.HasCommercialEntitlement);
                Assert.Equal("appointments", Assert.Single(snapshot.ModuleCodes));
                Assert.Equal(subscriptionId, snapshot.SubscriptionId);

                var checkout = new TenantPlanCheckoutService(scoped, sources);
                var secondOrder = await checkout.CreatePendingAsync(
                    a.Id, new(version.Id, "monthly", "checkout-request-0002"),
                    now.AddHours(1), CancellationToken.None);
                Assert.False(secondOrder.Ok);
                Assert.Equal("active_subscription_exists", secondOrder.Code);
            }

            // Production source registry contains zero Commercial modules.
            // Even settled SQL records are never licensed without deployed code.
            await using (var scoped = new IdentityDbContext(opts, tenantA))
            {
                var production = await new TenantCommercialEntitlementReader(scoped, tenantA)
                    .ReadCurrentAsync(now.AddHours(1));
                Assert.False(production.HasCommercialEntitlement);
                Assert.Empty(production.ModuleCodes);
            }
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.TenantModules.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Checkout_rejects_noncommercial_catalog_without_creating_orders()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var tenant = new Tenant { Name = "No products", Slug = "no-products" };
            var plan = new CatalogPlan { Code = "nop", Name = "None" };
            var draft = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "No products"
            };
            db.Tenants.Add(tenant);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(draft);
            await db.SaveChangesAsync();

            var service = new TenantPlanCheckoutService(db);
            var rejected = await service.CreatePendingAsync(tenant.Id,
                new(draft.Id, "monthly", "checkout-no-products-0001"),
                DateTime.UtcNow, CancellationToken.None);
            Assert.False(rejected.Ok);
            Assert.Equal("plan_not_saleable", rejected.Code);
            Assert.Empty(await db.TenantPlanOrders.ToArrayAsync());
            Assert.Empty(await db.TenantSubscriptions.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Percent_100_coupon_settles_without_gateway_once_and_global_cap_is_atomic()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var a = new Tenant { Name = "Coupon A", Slug = "coupon-a" };
            var b = new Tenant { Name = "Coupon B", Slug = "coupon-b" };
            var module = new Module("appointments", "Appointments", null, 0);
            var plan = new CatalogPlan { Code = "coupon-plan", Name = "Coupon Plan", IsActive = true };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft",
                DisplayTitle = "Coupon Plan"
            };
            db.Tenants.AddRange(a, b);
            db.Modules.Add(module);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
            { PlanVersionId = version.Id, ModuleId = module.Id });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "monthly",
                Currency = "IRR", Amount = 300000m, IsActive = true
            });
            await db.SaveChangesAsync();
            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddHours(-1);
            version.ContentFingerprint = new string('f', 64);
            await db.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var manager = new SystemCouponCatalogService(db);
            var issued = await manager.IssueAsync(new("Full discount", "Percentage",
                100m, version.Id, now.AddDays(-1), now.AddDays(3),
                1, 1), CancellationToken.None);
            Assert.True(issued.Ok, issued.Code);
            var code = Assert.IsType<string>(issued.SecretCode);
            Assert.Equal(24, code.Length);
            var stored = Assert.Single(await db.CatalogDiscountCoupons.ToArrayAsync());
            Assert.NotEqual(code, stored.CodeHash);
            Assert.Equal(CatalogCouponRules.Fingerprint(code), stored.CodeHash);
            Assert.DoesNotContain(code, stored.Title);
            var list = await manager.ListAsync(CancellationToken.None);
            Assert.Equal(0, Assert.Single(list).SettledUses);

            var manifest = new[]
            {
                new ResourceContract("appointments", "bookings",
                    ModuleProductClass.Commercial, ["APPOINTMENTS.READ"])
            };
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(db.Database.GetConnectionString()).Options;
            var contextA = new TenantContext();
            contextA.InitializeTenant(Guid.NewGuid(), a.Id, [a.Id], [a.Id], a.Id);
            Guid orderId;
            await using (var scoped = new IdentityDbContext(options, contextA))
            {
                var service = new TenantPlanCheckoutService(scoped, manifest);
                var request = new TenantPlanCheckoutService.CreateOrder(
                    version.Id, "monthly", "coupon-checkout-00001", code);
                var purchased = await service.CreatePendingAsync(
                    a.Id, request, now, CancellationToken.None);
                Assert.True(purchased.Ok, purchased.Code);
                Assert.Equal("zero_total_settled_and_activated", purchased.Code);
                Assert.Equal("Settled", purchased.Order!.Status);
                Assert.Equal(300000m, purchased.Order.DiscountAmount);
                Assert.Equal(0m, purchased.Order.NetAmount);
                Assert.Equal("ZeroTotalCoupon", purchased.Order.SettlementKind);
                Assert.NotNull(purchased.SubscriptionId);
                orderId = purchased.Order.Id;
                var repeated = await service.CreatePendingAsync(
                    a.Id, request, now, CancellationToken.None);
                Assert.True(repeated.Ok);
                Assert.Equal("existing_order", repeated.Code);
                Assert.Equal(orderId, repeated.Order!.Id);
                var otherCode = await service.CreatePendingAsync(a.Id,
                    request with { CouponCode = null }, now, CancellationToken.None);
                Assert.Equal("idempotency_conflict", otherCode.Code);

                var license = await new TenantCommercialEntitlementReader(scoped, contextA)
                    .ReadCurrentAsync(now.AddMinutes(2), manifest, []);
                Assert.True(license.HasCommercialEntitlement);
                Assert.Equal("appointments", Assert.Single(license.ModuleCodes));
            }

            var contextB = new TenantContext();
            contextB.InitializeTenant(Guid.NewGuid(), b.Id, [b.Id], [b.Id], b.Id);
            await using (var scoped = new IdentityDbContext(options, contextB))
            {
                var rejected = await new TenantPlanCheckoutService(scoped, manifest)
                    .CreatePendingAsync(b.Id,
                        new(version.Id, "monthly", "coupon-checkout-00002", code),
                        now.AddMinutes(1), CancellationToken.None);
                Assert.False(rejected.Ok);
                Assert.Equal("coupon_usage_exhausted", rejected.Code);
                Assert.Empty(await scoped.TenantPlanOrders.ToArrayAsync());
            }
            Assert.Equal(1, await db.CatalogCouponRedemptions
                .IgnoreQueryFilters(["tenant"]).CountAsync());
            Assert.Equal(1, await db.TenantSubscriptions
                .IgnoreQueryFilters(["tenant"]).CountAsync());
            Assert.Single((await manager.ListAsync(CancellationToken.None))
                .Where(x => x.SettledUses == 1));

            // Test-only commercial source; unregistered production modules remain OFF.
            await using (var scoped = new IdentityDbContext(options, contextA))
            {
                var shadow = await new TenantCommercialEntitlementReader(scoped, contextA)
                    .ReadCurrentAsync(now.AddMinutes(2));
                Assert.False(shadow.HasCommercialEntitlement);
            }
            Assert.Empty(await db.RolePermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
            Assert.Empty(await db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToArrayAsync());
        });
    }

    [Fact]
    public async Task Partial_coupon_consumes_use_only_on_verified_external_settlement()
    {
        await WithDisposableDatabaseAsync(async db =>
        {
            var tenant = new Tenant { Name = "Partial", Slug = "coupon-partial" };
            var module = new Module("appointments", "Appointments", null, 0);
            var plan = new CatalogPlan { Code = "partial-plan", Name = "Partial", IsActive = true };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Status = "Draft", DisplayTitle = "Partial"
            };
            db.Tenants.Add(tenant);
            db.Modules.Add(module);
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            db.CatalogPlanVersionModules.Add(new CatalogPlanVersionModule
                { PlanVersionId = version.Id, ModuleId = module.Id });
            db.CatalogPlanPriceOffers.Add(new CatalogPlanPriceOffer
            {
                PlanVersionId = version.Id, BillingCycle = "yearly",
                Currency = "IRR", Amount = 400000m, IsActive = true
            });
            await db.SaveChangesAsync();
            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow.AddHours(-1);
            version.ContentFingerprint = new string('a', 64);
            await db.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var issued = await new SystemCouponCatalogService(db).IssueAsync(
                new("Annual voucher", "FixedIRR", 120000m,
                    version.Id, now.AddHours(-1), now.AddDays(2), 2, 1),
                CancellationToken.None);
            Assert.True(issued.Ok);
            var code = issued.SecretCode!;
            var source = new[] { new ResourceContract("appointments", "bookings",
                ModuleProductClass.Commercial, ["APPOINTMENTS.READ"]) };
            var tenantContext = new TenantContext();
            tenantContext.InitializeTenant(Guid.NewGuid(), tenant.Id, [tenant.Id],
                [tenant.Id], tenant.Id);
            var systemContext = new TenantContext();
            systemContext.InitializeSystem(Guid.NewGuid(), true);
            var opts = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(db.Database.GetConnectionString()).Options;
            Guid orderId;
            await using (var scoped = new IdentityDbContext(opts, tenantContext))
            {
                var placed = await new TenantPlanCheckoutService(scoped, source)
                    .CreatePendingAsync(tenant.Id,
                        new(version.Id, "yearly", "coupon-partial-00001", code),
                        now, CancellationToken.None);
                Assert.True(placed.Ok, placed.Code);
                Assert.Equal("Pending", placed.Order!.Status);
                Assert.Equal(120000m, placed.Order.DiscountAmount);
                Assert.Equal(280000m, placed.Order.NetAmount);
                orderId = placed.Order.Id;
            }
            Assert.Empty(await db.CatalogCouponRedemptions
                .IgnoreQueryFilters(["tenant"]).ToArrayAsync());

            await using (var systemDb = new IdentityDbContext(opts, systemContext))
            {
                var settled = await new TenantPlanCheckoutService(systemDb, source)
                    .AttestExternalSettlementAsync(orderId, Guid.NewGuid(),
                        new("receipt-partial-101", true), now.AddMinutes(5),
                        CancellationToken.None);
                Assert.True(settled.Ok, settled.Code);
                Assert.Equal("settled_and_activated", settled.Code);
            }
            Assert.Equal(1, await db.CatalogCouponRedemptions
                .IgnoreQueryFilters(["tenant"]).CountAsync());
            await using (var scoped = new IdentityDbContext(opts, tenantContext))
            {
                var entitlement = await new TenantCommercialEntitlementReader(scoped, tenantContext)
                    .ReadCurrentAsync(now.AddMinutes(6), source, []);
                Assert.True(entitlement.HasCommercialEntitlement);
            }
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
            Assert.False(db.Database.HasPendingModelChanges(),
                "EF Core model does not match the last migration snapshot");
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
