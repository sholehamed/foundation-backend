using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-17B-2B: central System-owned Plan authoring. No subscription, tenant grant,
/// coupon, payment or executable capability comes from these records.
/// The only production catalog of sellable Modules is code-owned.
/// </summary>
public sealed class SystemPlanCatalogAdminService(
    IdentityDbContext db,
    IReadOnlyCollection<ResourceContract>? sourceResources = null,
    IReadOnlyCollection<CapabilityContract>? sourceCapabilities = null)
{
    private readonly IReadOnlyCollection<ResourceContract> resources =
        sourceResources ?? ModuleResourceContracts.Current();
    private readonly IReadOnlyCollection<CapabilityContract> capabilities =
        sourceCapabilities ?? [];
    private static readonly Regex PlanCode = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record PlanRow(
        Guid Id, string Code, string Name, bool IsActive,
        Guid? ParentPlanId, Guid? DraftVersionId, int? LatestVersionNumber,
        string? LatestVersionStatus, int PublishedVersions);

    public sealed record CommercialModule(string Code, string Title);
    public sealed record NewPlan(string Code, string Name, string? Description,
        Guid? ParentPlanVersionId, string DisplayTitle, string? DisplaySubtitle);
    public sealed record PriceInput(string BillingCycle, string Currency, decimal Amount);
    public sealed record CapabilityInput(string Code, bool? BooleanValue,
        long? IntegerValue, bool IsUnlimited, string[]? Modes);
    public sealed record DisplayInput(string Kind, string? Code, string Title,
        string? Description, bool IsHighlighted, int SortOrder);
    public sealed record DraftInput(string DisplayTitle, string? DisplaySubtitle,
        string? BadgeText, bool IsRecommended, int DisplayOrder,
        string[]? ModuleCodes, PriceInput[]? Prices,
        CapabilityInput[]? Capabilities, DisplayInput[]? DisplayItems);
    public sealed record DraftPreview(
        Guid VersionId, int VersionNumber, string Fingerprint,
        bool CanPublish, IReadOnlyList<string> Issues,
        IReadOnlyList<string> EffectiveModuleCodes);
    public sealed record OperationResult(bool Ok, string Code,
        Guid? Id = null, DraftPreview? Preview = null);

    public async Task<IReadOnlyList<PlanRow>> ListAsync(CancellationToken ct)
    {
        var plans = await db.CatalogPlans.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Take(300).ToArrayAsync(ct);
        var ids = plans.Select(x => x.Id).ToArray();
        var versions = await db.CatalogPlanVersions.AsNoTracking()
            .Where(x => ids.Contains(x.PlanId))
            .ToArrayAsync(ct);
        return plans.Select(p =>
        {
            var related = versions.Where(v => v.PlanId == p.Id)
                .OrderByDescending(v => v.VersionNumber).ToArray();
            return new PlanRow(p.Id, p.Code, p.Name, p.IsActive,
                p.ParentPlanId,
                related.FirstOrDefault(v => v.Status == "Draft")?.Id,
                related.FirstOrDefault()?.VersionNumber,
                related.FirstOrDefault()?.Status,
                related.Count(v => v.Status == "Published"));
        }).ToArray();
    }

    /// <summary>Only deployed, active, code-owned commercial modules are eligible.</summary>
    public async Task<IReadOnlyList<CommercialModule>> CommercialModulesAsync(CancellationToken ct)
    {
        var codes = resources
            .Where(x => x.ProductClass == ModuleProductClass.Commercial)
            .Select(x => x.ModuleCode).ToHashSet(StringComparer.Ordinal);
        if (codes.Count == 0) return [];
        return (await db.Modules.AsNoTracking()
            .Where(x => x.IsActive && codes.Contains(x.Code))
            .OrderBy(x => x.SortOrder).ToArrayAsync(ct))
            .Select(x => new CommercialModule(x.Code, x.Title)).ToArray();
    }

    public async Task<OperationResult> CreateAsync(NewPlan request, CancellationToken ct)
    {
        if (!ValidText(request.Code, 20) || !PlanCode.IsMatch(request.Code) ||
            !ValidText(request.Name, 200) ||
            !ValidText(request.DisplayTitle, 200) ||
            !OptionalText(request.Description, 1000) ||
            !OptionalText(request.DisplaySubtitle, 1000))
            return new(false, "invalid_plan_fields");
        if (!db.Database.IsSqlServer())
            return new(false, "sql_server_required");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (await db.CatalogPlans.AnyAsync(x => x.Code == request.Code, ct))
                return new OperationResult(false, "duplicate_plan_code");
            CatalogPlanVersion? parent = null;
            if (request.ParentPlanVersionId is Guid parentId)
            {
                parent = await db.CatalogPlanVersions.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == parentId, ct);
                if (parent is null || parent.Status is not ("Published" or "Retired") ||
                    parent.PublishedAtUtc is null ||
                    !await db.CatalogPlans.AnyAsync(x => x.Id == parent.PlanId && x.IsActive, ct))
                    return new OperationResult(false, "invalid_parent_version");
            }
            var plan = new CatalogPlan
            {
                Code = request.Code, Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                ParentPlanId = parent?.PlanId, IsActive = true
            };
            var version = new CatalogPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1,
                Status = "Draft", ParentPlanVersionId = parent?.Id,
                DisplayTitle = request.DisplayTitle.Trim(),
                DisplaySubtitle = request.DisplaySubtitle?.Trim()
            };
            db.CatalogPlans.Add(plan);
            db.CatalogPlanVersions.Add(version);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OperationResult(true, "draft_created", version.Id);
        });
    }

    public async Task<OperationResult> CreateRevisionAsync(Guid planId, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return new(false, "sql_server_required");
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await db.CatalogPlans.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == planId && x.IsActive, ct);
            if (plan is null) return new OperationResult(false, "plan_not_found");
            var all = await db.CatalogPlanVersions.AsNoTracking()
                .Where(x => x.PlanId == plan.Id)
                .OrderByDescending(x => x.VersionNumber).ToArrayAsync(ct);
            if (all.Any(x => x.Status == "Draft"))
                return new OperationResult(false, "existing_draft");
            var prior = all.FirstOrDefault(x => x.Status is "Published" or "Retired");
            if (prior is null) return new OperationResult(false, "no_prior_published_version");
            var revision = new CatalogPlanVersion
            {
                PlanId = plan.Id, ParentPlanVersionId = prior.ParentPlanVersionId,
                VersionNumber = all.Max(x => x.VersionNumber) + 1, Status = "Draft",
                DisplayTitle = prior.DisplayTitle, DisplaySubtitle = prior.DisplaySubtitle,
                BadgeText = prior.BadgeText, IsRecommended = prior.IsRecommended,
                DisplayOrder = prior.DisplayOrder
            };
            db.CatalogPlanVersions.Add(revision);
            var modules = await db.CatalogPlanVersionModules.AsNoTracking()
                .Where(x => x.PlanVersionId == prior.Id).ToArrayAsync(ct);
            db.CatalogPlanVersionModules.AddRange(modules.Select(x =>
                new CatalogPlanVersionModule { PlanVersionId = revision.Id, ModuleId = x.ModuleId }));
            var prices = await db.CatalogPlanPriceOffers.AsNoTracking()
                .Where(x => x.PlanVersionId == prior.Id).ToArrayAsync(ct);
            db.CatalogPlanPriceOffers.AddRange(prices.Select(x => new CatalogPlanPriceOffer
            {
                PlanVersionId = revision.Id, BillingCycle = x.BillingCycle, Currency = x.Currency,
                Amount = x.Amount, IsActive = x.IsActive,
                SaleStartsAtUtc = x.SaleStartsAtUtc, SaleEndsAtUtc = x.SaleEndsAtUtc
            }));
            var values = await db.CatalogPlanVersionCapabilityValues.AsNoTracking()
                .Where(x => x.PlanVersionId == prior.Id).ToArrayAsync(ct);
            db.CatalogPlanVersionCapabilityValues.AddRange(values.Select(x =>
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = revision.Id, CapabilityDefinitionId = x.CapabilityDefinitionId,
                    BooleanValue = x.BooleanValue, IntegerValue = x.IntegerValue,
                    ModeSetJson = x.ModeSetJson, IsUnlimited = x.IsUnlimited
                }));
            var display = await db.CatalogPlanDisplayItems.AsNoTracking()
                .Where(x => x.PlanVersionId == prior.Id).ToArrayAsync(ct);
            db.CatalogPlanDisplayItems.AddRange(display.Select(x => new CatalogPlanDisplayItem
            {
                PlanVersionId = revision.Id, Kind = x.Kind, ModuleId = x.ModuleId,
                CapabilityDefinitionId = x.CapabilityDefinitionId, Title = x.Title,
                Description = x.Description, IsHighlighted = x.IsHighlighted,
                SortOrder = x.SortOrder
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OperationResult(true, "revision_created", revision.Id);
        });
    }

    public async Task<OperationResult> ReplaceDraftAsync(
        Guid versionId, DraftInput input, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return new(false, "sql_server_required");
        var validation = ValidateInput(input);
        if (validation is not null) return new(false, validation);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var version = await db.CatalogPlanVersions
                .SingleOrDefaultAsync(x => x.Id == versionId, ct);
            if (version is null) return new OperationResult(false, "version_not_found");
            if (version.Status != "Draft") return new OperationResult(false, "published_immutable");

            var sources = resources.Where(x =>
                x.ProductClass == ModuleProductClass.Commercial).ToArray();
            var moduleCodes = input.ModuleCodes!.Distinct(StringComparer.Ordinal).ToArray();
            var deployed = sources.Select(x => x.ModuleCode).ToHashSet(StringComparer.Ordinal);
            if (moduleCodes.Any(x => !deployed.Contains(x)))
                return new OperationResult(false, "module_not_code_owned_commercial");

            var moduleRows = await db.Modules.AsNoTracking()
                .Where(x => x.IsActive && moduleCodes.Contains(x.Code)).ToArrayAsync(ct);
            if (moduleRows.Length != moduleCodes.Length)
                return new OperationResult(false, "module_missing_in_catalog");
            var moduleLookup = moduleRows.ToDictionary(x => x.Code, x => x.Id,
                StringComparer.Ordinal);

            var capSource = capabilities.ToDictionary(x => x.Code, StringComparer.Ordinal);
            var capCodes = input.Capabilities!.Select(x => x.Code).ToArray();
            if (capCodes.Length != capCodes.Distinct(StringComparer.Ordinal).Count() ||
                capCodes.Any(x => !capSource.ContainsKey(x)))
                return new OperationResult(false, "unknown_or_duplicate_capability");
            var capRows = await db.CatalogCapabilityDefinitions.AsNoTracking()
                .Where(x => capCodes.Contains(x.Code) && x.IsActive).ToArrayAsync(ct);
            if (capRows.Length != capCodes.Length)
                return new OperationResult(false, "capability_not_registered");
            var capLookup = capRows.ToDictionary(x => x.Code, StringComparer.Ordinal);
            foreach (var capability in input.Capabilities)
            {
                var source = capSource[capability.Code];
                var record = capLookup[capability.Code];
                var module = moduleRows.SingleOrDefault(x => x.Id == record.ModuleId);
                if (module is null || module.Code != source.ModuleCode ||
                    record.Kind != source.Kind.ToString() ||
                    record.ValueKind != source.ValueKind.ToString() ||
                    record.MergePolicy != source.MergePolicy.ToString())
                    return new OperationResult(false, "invalid_capability_owner");
                if (!ValidCapabilityInput(capability, source))
                    return new OperationResult(false, "invalid_capability_value");
            }

            var rowIds = new Dictionary<string, Guid>(moduleLookup, StringComparer.Ordinal);
            foreach (var display in input.DisplayItems!)
            {
                if (display.Kind == "Module" &&
                    (display.Code is null || !rowIds.ContainsKey(display.Code)))
                    return new OperationResult(false, "display_module_not_in_draft");
                if (display.Kind == "Capability" &&
                    (display.Code is null || !capLookup.ContainsKey(display.Code)))
                    return new OperationResult(false, "display_capability_not_in_draft");
            }

            // Replace only owned Draft children; never touch published rows.
            db.CatalogPlanVersionModules.RemoveRange(
                await db.CatalogPlanVersionModules.Where(x => x.PlanVersionId == versionId)
                    .ToArrayAsync(ct));
            db.CatalogPlanVersionCapabilityValues.RemoveRange(
                await db.CatalogPlanVersionCapabilityValues.Where(x => x.PlanVersionId == versionId)
                    .ToArrayAsync(ct));
            db.CatalogPlanPriceOffers.RemoveRange(
                await db.CatalogPlanPriceOffers.Where(x => x.PlanVersionId == versionId)
                    .ToArrayAsync(ct));
            db.CatalogPlanDisplayItems.RemoveRange(
                await db.CatalogPlanDisplayItems.Where(x => x.PlanVersionId == versionId)
                    .ToArrayAsync(ct));
            await db.SaveChangesAsync(ct);

            version.DisplayTitle = input.DisplayTitle.Trim();
            version.DisplaySubtitle = input.DisplaySubtitle?.Trim();
            version.BadgeText = input.BadgeText?.Trim();
            version.IsRecommended = input.IsRecommended;
            version.DisplayOrder = input.DisplayOrder;
            db.CatalogPlanVersionModules.AddRange(moduleRows.Select(x =>
                new CatalogPlanVersionModule { PlanVersionId = versionId, ModuleId = x.Id }));
            db.CatalogPlanPriceOffers.AddRange(input.Prices!.Select(x => new CatalogPlanPriceOffer
            {
                PlanVersionId = versionId, BillingCycle = x.BillingCycle,
                Currency = x.Currency, Amount = x.Amount, IsActive = true
            }));
            db.CatalogPlanVersionCapabilityValues.AddRange(input.Capabilities.Select(x =>
                new CatalogPlanVersionCapabilityValue
                {
                    PlanVersionId = versionId,
                    CapabilityDefinitionId = capLookup[x.Code].Id,
                    BooleanValue = x.BooleanValue, IntegerValue = x.IntegerValue,
                    IsUnlimited = x.IsUnlimited,
                    ModeSetJson = x.Modes is null ? null : JsonSerializer.Serialize(
                        x.Modes.OrderBy(mode => mode, StringComparer.Ordinal))
                }));
            db.CatalogPlanDisplayItems.AddRange(input.DisplayItems.Select(x =>
                new CatalogPlanDisplayItem
                {
                    PlanVersionId = versionId, Kind = x.Kind,
                    ModuleId = x.Kind == "Module" ? moduleLookup[x.Code!] : null,
                    CapabilityDefinitionId = x.Kind == "Capability" ? capLookup[x.Code!].Id : null,
                    Title = x.Title.Trim(), Description = x.Description?.Trim(),
                    IsHighlighted = x.IsHighlighted, SortOrder = x.SortOrder
                }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OperationResult(true, "draft_saved", versionId);
        });
    }

    /// <summary>Read-only validation and stable operator-review fingerprint.</summary>
    public async Task<DraftPreview> PreviewAsync(Guid versionId, CancellationToken ct)
    {
        var issues = new List<string>();
        var sourceIssues = ModuleResourceContracts.Validate(resources, capabilities);
        if (sourceIssues.Count != 0) issues.Add("invalid_code_owned_resource_contract");

        var version = await db.CatalogPlanVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null)
            return new(versionId, 0, "", false, ["version_not_found"], []);
        if (version.Status != "Draft") issues.Add("not_a_draft");
        var plan = await db.CatalogPlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == version.PlanId, ct);
        if (plan is null || !plan.IsActive) issues.Add("plan_missing_or_inactive");

        var allowedCodes = resources
            .Where(x => x.ProductClass == ModuleProductClass.Commercial)
            .Select(x => x.ModuleCode).ToHashSet(StringComparer.Ordinal);
        var effective = new SortedSet<string>(StringComparer.Ordinal);
        var ancestors = new List<CatalogPlanVersion>();
        var parentId = version.ParentPlanVersionId;
        var seen = new HashSet<Guid> { version.Id };
        if (plan is not null && (plan.ParentPlanId is null) != (parentId is null))
            issues.Add("plan_parent_version_mismatch");

        while (parentId.HasValue && ancestors.Count < 16 && seen.Add(parentId.Value))
        {
            var parent = await db.CatalogPlanVersions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == parentId.Value, ct);
            if (parent is null || parent.Status is not ("Published" or "Retired") ||
                parent.PublishedAtUtc is null || parent.ContentFingerprint is not { Length: 64 })
            {
                issues.Add("invalid_parent_publication");
                break;
            }
            if (ancestors.Count == 0 && plan?.ParentPlanId != parent.PlanId)
                issues.Add("parent_version_wrong_plan");
            ancestors.Add(parent);
            parentId = parent.ParentPlanVersionId;
        }
        if (parentId.HasValue && (ancestors.Count == 16 || !seen.Add(parentId.Value)))
            issues.Add("parent_cycle_or_excessive_depth");

        var modules = await db.Modules.AsNoTracking().ToArrayAsync(ct);
        var allVersionIds = ancestors.Select(x => x.Id).Append(version.Id).ToArray();
        var bindings = await db.CatalogPlanVersionModules.AsNoTracking()
            .Where(x => allVersionIds.Contains(x.PlanVersionId)).ToArrayAsync(ct);
        foreach (var binding in bindings)
        {
            var module = modules.SingleOrDefault(x => x.Id == binding.ModuleId);
            if (module is null || !module.IsActive || !allowedCodes.Contains(module.Code))
                issues.Add("unregistered_or_noncommercial_module");
            else effective.Add(module.Code);
        }
        if (effective.Count == 0) issues.Add("no_commercial_module");
        var prices = await db.CatalogPlanPriceOffers.AsNoTracking()
            .Where(x => x.PlanVersionId == version.Id).ToArrayAsync(ct);
        if (prices.Length == 0 || !prices.Any(x => x.IsActive &&
            x.Amount >= 0 && x.Currency == "IRR" &&
            x.BillingCycle is "monthly" or "yearly" &&
            (x.SaleEndsAtUtc is null || x.SaleEndsAtUtc > DateTime.UtcNow)))
            issues.Add("no_current_valid_price");

        var display = await db.CatalogPlanDisplayItems.AsNoTracking()
            .Where(x => x.PlanVersionId == version.Id).ToArrayAsync(ct);
        if (string.IsNullOrWhiteSpace(version.DisplayTitle))
            issues.Add("missing_title");
        foreach (var row in display)
        {
            if (row.Kind == "Module" &&
                (row.ModuleId is null ||
                 !bindings.Any(x => x.ModuleId == row.ModuleId)))
                issues.Add("display_unowned_module");
            if (row.Kind == "Capability" &&
                (row.CapabilityDefinitionId is null ||
                 !await db.CatalogPlanVersionCapabilityValues.AsNoTracking()
                     .AnyAsync(x => allVersionIds.Contains(x.PlanVersionId) &&
                                    x.CapabilityDefinitionId == row.CapabilityDefinitionId, ct)))
                issues.Add("display_unowned_capability");
        }

        // SHA256 canonical JSON includes immutable parent pin and content.
        // Not a permission grant: it binds an operator's approval to reviewed
        // draft content, prices, display copy and deployed capability sources.
        var caps = await db.CatalogPlanVersionCapabilityValues.AsNoTracking()
            .Where(x => x.PlanVersionId == version.Id).ToArrayAsync(ct);
        var snapshot = new
        {
            Plan = new { plan?.Id, plan?.Code, plan?.ParentPlanId, plan?.IsActive },
            version.Id, version.VersionNumber, version.ParentPlanVersionId,
            version.DisplayTitle, version.DisplaySubtitle, version.BadgeText,
            version.IsRecommended, version.DisplayOrder, version.Status,
            Ancestors = ancestors.Select(x => new { x.Id, x.PlanId, x.ContentFingerprint }),
            Modules = bindings.OrderBy(x => x.PlanVersionId).ThenBy(x => x.ModuleId)
                .Select(x => new { x.PlanVersionId, x.ModuleId }),
            Prices = prices.OrderBy(x => x.BillingCycle).ThenBy(x => x.Currency)
                .Select(x => new { x.BillingCycle, x.Currency, x.Amount, x.IsActive,
                    x.SaleStartsAtUtc, x.SaleEndsAtUtc }),
            Capabilities = caps.OrderBy(x => x.CapabilityDefinitionId)
                .Select(x => new { x.CapabilityDefinitionId, x.BooleanValue,
                    x.IntegerValue, x.ModeSetJson, x.IsUnlimited }),
            Display = display.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Kind, x.ModuleId, x.CapabilityDefinitionId,
                    x.Title, x.Description, x.SortOrder, x.IsHighlighted }),
            Source = resources.OrderBy(x => x.ModuleCode).ThenBy(x => x.ResourceCode)
                .Select(x => new { x.ModuleCode, x.ResourceCode, Class = x.ProductClass,
                    Permissions = x.PermissionCodes.OrderBy(p => p, StringComparer.Ordinal) })
        };
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)))).ToLowerInvariant();
        return new(version.Id, version.VersionNumber, hash,
            issues.Count == 0, issues.Distinct().ToArray(), effective.ToArray());
    }

    public async Task<OperationResult> PublishAsync(
        Guid versionId, string expectedFingerprint, CancellationToken ct)
    {
        if (expectedFingerprint is not { Length: 64 } ||
            expectedFingerprint.Any(c => !Uri.IsHexDigit(c)))
            return new(false, "invalid_fingerprint");
        if (!db.Database.IsSqlServer())
            return new(false, "sql_server_required");
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var preview = await PreviewAsync(versionId, ct);
            if (!preview.CanPublish)
                return new OperationResult(false, "catalog_not_publishable", versionId, preview);
            if (!string.Equals(preview.Fingerprint, expectedFingerprint,
                    StringComparison.Ordinal))
                return new OperationResult(false, "stale_preview", versionId, preview);

            var version = await db.CatalogPlanVersions
                .SingleAsync(x => x.Id == versionId && x.Status == "Draft", ct);
            if (await db.CatalogPlanVersions.AsNoTracking().AnyAsync(x =>
                x.PlanId == version.PlanId && x.Status == "Published" &&
                x.VersionNumber >= version.VersionNumber, ct))
                return new OperationResult(false, "newer_version_already_published", versionId);

            version.Status = "Published";
            version.PublishedAtUtc = DateTime.UtcNow;
            version.ContentFingerprint = preview.Fingerprint;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OperationResult(true, "published", versionId, preview);
        });
    }

    private static string? ValidateInput(DraftInput input)
    {
        if (input is null || !ValidText(input.DisplayTitle, 200) ||
            !OptionalText(input.DisplaySubtitle, 1000) ||
            !OptionalText(input.BadgeText, 80) ||
            input.DisplayOrder is < 0 or > 10000 ||
            input.ModuleCodes is null || input.ModuleCodes.Length > 50 ||
            input.Prices is null || input.Prices.Length > 20 ||
            input.Capabilities is null || input.Capabilities.Length > 100 ||
            input.DisplayItems is null || input.DisplayItems.Length > 100)
            return "invalid_draft_fields";
        if (input.ModuleCodes.Any(x => !ValidText(x, 80) ||
                                       !PlanCode.IsMatch(x)) ||
            input.ModuleCodes.Distinct(StringComparer.Ordinal).Count() !=
                input.ModuleCodes.Length)
            return "duplicate_or_invalid_modules";
        if (input.Prices.Any(x => x.Amount < 0 || x.Amount > 1_000_000_000_000m ||
                x.BillingCycle is not ("monthly" or "yearly") || x.Currency != "IRR") ||
            input.Prices.Select(x => x.BillingCycle + "/" + x.Currency)
                .Distinct(StringComparer.Ordinal).Count() != input.Prices.Length)
            return "invalid_prices";
        if (input.DisplayItems.Any(x =>
                x.Kind is not ("Module" or "Capability" or "Benefit") ||
                !ValidText(x.Title, 200) || !OptionalText(x.Description, 1000) ||
                x.SortOrder is < 0 or > 10000 ||
                (x.Kind == "Benefit" && x.Code is not null)))
            return "invalid_display_items";
        return null;
    }

    private static bool ValidCapabilityInput(CapabilityInput x, CapabilityContract source)
    {
        var count = (x.BooleanValue.HasValue ? 1 : 0) +
                    (x.IntegerValue.HasValue ? 1 : 0) +
                    (x.IsUnlimited ? 1 : 0) +
                    (x.Modes is not null ? 1 : 0);
        if (count != 1) return false;
        return source.Kind switch
        {
            CapabilityKind.Feature => x.BooleanValue.HasValue,
            CapabilityKind.CountLimit or CapabilityKind.PeriodQuota =>
                (x.IntegerValue is >= 0 || x.IsUnlimited),
            CapabilityKind.AllowedModes =>
                x.Modes is { Length: > 0 and <= 24 } &&
                x.Modes.All(m => m is { Length: > 0 and <= 40 } && PlanCode.IsMatch(m)) &&
                x.Modes.Distinct(StringComparer.Ordinal).Count() == x.Modes.Length,
            _ => false
        };
    }

    private static bool ValidText(string? s, int max) =>
        !string.IsNullOrWhiteSpace(s) && s.Length <= max && !s.Contains('<') && !s.Contains('>');
    private static bool OptionalText(string? s, int max) =>
        s is null || s.Length <= max && !s.Contains('<') && !s.Contains('>');
}
