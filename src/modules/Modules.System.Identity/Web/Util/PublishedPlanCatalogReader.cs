using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-17B-1: read-only resolution of a *published* commercial PlanVersion.
/// It does NOT activate a Subscription, grant a role, or check Tenant usage.
/// Vetted source-owned Commercial Resource/Capability descriptions are required
/// explicitly; DB rows alone never register executable software features.
/// </summary>
public sealed record ResolvedCatalogCapability(
    string Code, bool? BooleanValue, long? IntegerValue,
    bool IsUnlimited, IReadOnlyList<string> Modes);

public sealed record ResolvedCatalogPrice(
    string BillingCycle, string Currency, decimal Amount);

public sealed record ResolvedCatalogDisplayItem(
    string Kind, string Title, string? Description, int SortOrder,
    bool IsHighlighted);

public sealed record ResolvedCatalogPlan(
    Guid PlanId, Guid PlanVersionId, string PlanCode,
    string DisplayTitle, string? DisplaySubtitle, string? BadgeText,
    bool IsRecommended, int DisplayOrder,
    IReadOnlyList<string> ModuleCodes,
    IReadOnlyList<ResolvedCatalogCapability> Capabilities,
    IReadOnlyList<ResolvedCatalogPrice> Prices,
    IReadOnlyList<ResolvedCatalogDisplayItem> DisplayItems);

public sealed class PublishedPlanCatalogReader(IdentityDbContext db)
{
    private static readonly Regex ModeCode = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ResolvedCatalogPlan> ReadAsync(
        Guid planVersionId,
        IReadOnlyCollection<ResourceContract> codeResources,
        IReadOnlyCollection<CapabilityContract> codeCapabilities,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(codeResources);
        ArgumentNullException.ThrowIfNull(codeCapabilities);
        var catalogErrors = ModuleResourceContracts.Validate(
            codeResources, codeCapabilities);
        if (catalogErrors.Count != 0)
            throw new InvalidOperationException(
                "Code-owned Module/Capability catalog is inconsistent: " +
                catalogErrors[0].Code);

        var classified = codeResources
            .GroupBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().ProductClass,
                StringComparer.OrdinalIgnoreCase);
        var registered = codeCapabilities.ToDictionary(
            x => x.Code, StringComparer.OrdinalIgnoreCase);

        var chain = new List<CatalogPlanVersion>();
        var seen = new HashSet<Guid>();
        Guid? cursor = planVersionId;
        while (cursor.HasValue)
        {
            if (chain.Count >= 16 || !seen.Add(cursor.Value))
                throw new InvalidOperationException(
                    "Plan inheritance cycle or excessive depth");
            var version = await db.CatalogPlanVersions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == cursor.Value, ct)
                ?? throw new InvalidOperationException("PlanVersion not found");
            if (version.Status != "Published" && !(chain.Count > 0 &&
                                                    version.Status == "Retired"))
                throw new InvalidOperationException("PlanVersion not published");
            if (version.PublishedAtUtc is null ||
                version.PublishedAtUtc > nowUtc ||
                version.ContentFingerprint is not { Length: 64 } ||
                version.ContentFingerprint.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidOperationException(
                    "Published PlanVersion is missing valid publication metadata");
            chain.Add(version);
            cursor = version.ParentPlanVersionId;
        }
        chain.Reverse();

        var selected = chain[^1];
        var plan = await db.CatalogPlans.AsNoTracking()
            .SingleAsync(x => x.Id == selected.PlanId, ct);
        if (!plan.IsActive)
            throw new InvalidOperationException("Plan is not available");

        var modulesById = await db.Modules.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, ct);
        var resourcesById = await db.Resources.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, ct);
        var allowedModules = new HashSet<Guid>();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var values = new Dictionary<string, ResolvedCatalogCapability>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var version in chain)
        {
            var memberships = await db.CatalogPlanVersionModules.AsNoTracking()
                .Where(x => x.PlanVersionId == version.Id).ToArrayAsync(ct);
            foreach (var membership in memberships)
            {
                if (!modulesById.TryGetValue(membership.ModuleId, out var module) ||
                    !module.IsActive ||
                    !classified.TryGetValue(module.Code, out var productClass) ||
                    productClass != ModuleProductClass.Commercial)
                    throw new InvalidOperationException(
                        "Plan contains an inactive, non-commercial or unknown Module");
                allowedModules.Add(module.Id);
                codes.Add(module.Code);
            }

            var declaredValues = await db.CatalogPlanVersionCapabilityValues
                .AsNoTracking().Where(x => x.PlanVersionId == version.Id)
                .ToArrayAsync(ct);
            foreach (var row in declaredValues)
            {
                var definition = await db.CatalogCapabilityDefinitions.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == row.CapabilityDefinitionId, ct)
                    ?? throw new InvalidOperationException("Capability definition missing");
                if (!definition.IsActive ||
                    !allowedModules.Contains(definition.ModuleId) ||
                    !modulesById.TryGetValue(definition.ModuleId, out var module) ||
                    !registered.TryGetValue(definition.Code, out var source) ||
                    !string.Equals(module.Code, source.ModuleCode, StringComparison.Ordinal) ||
                    definition.Kind != source.Kind.ToString() ||
                    definition.ValueKind != source.ValueKind.ToString() ||
                    definition.MergePolicy != source.MergePolicy.ToString())
                    throw new InvalidOperationException(
                        "Capability value missing active, vetted Commercial ownership");
                if (definition.ResourceId is Guid resourceId &&
                    (!resourcesById.TryGetValue(resourceId, out var resource) ||
                     resource.ModuleId != module.Id ||
                     !resource.IsActive ||
                     !string.Equals(resource.Code, source.ResourceCode,
                         StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "Capability bound to foreign/inactive Resource");
                if ((definition.ResourceId is null) != (source.ResourceCode is null))
                    throw new InvalidOperationException(
                        "Capability Resource scope differs from source definition");

                var next = ParseValue(row, source);
                values[definition.Code] = values.TryGetValue(definition.Code,
                    out var previous)
                    ? Merge(previous, next, source.MergePolicy)
                    : next;
            }
        }

        var prices = await db.CatalogPlanPriceOffers.AsNoTracking()
            .Where(x => x.PlanVersionId == selected.Id && x.IsActive)
            .ToArrayAsync(ct);
        var activeOffers = prices.Where(x =>
            (x.SaleStartsAtUtc is null || x.SaleStartsAtUtc <= nowUtc) &&
            (x.SaleEndsAtUtc is null || nowUtc < x.SaleEndsAtUtc))
            .Select(x => new ResolvedCatalogPrice(
                x.BillingCycle, x.Currency, x.Amount))
            .OrderBy(x => x.BillingCycle, StringComparer.Ordinal)
            .ThenBy(x => x.Currency, StringComparer.Ordinal)
            .ToArray();

        // Cards inherit published parent-version display items as well as
        // purchased modules. A child may override the display row of the same
        // Module/Capability, but cannot inject an unentitled feature.
        var displayByKey = new Dictionary<string, (int Depth, CatalogPlanDisplayItem Item)>(
            StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < chain.Count; depth++)
        {
            var ancestor = chain[depth];
            var rows = await db.CatalogPlanDisplayItems.AsNoTracking()
                .Where(x => x.PlanVersionId == ancestor.Id)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToArrayAsync(ct);
            foreach (var item in rows)
            {
                string key;
                if (item.Kind == "Module")
                {
                    if (item.ModuleId is not Guid moduleId ||
                        !allowedModules.Contains(moduleId))
                        throw new InvalidOperationException(
                            "Plan card advertises an unentitled Module");
                    key = "M:" + moduleId;
                }
                else if (item.Kind == "Capability")
                {
                    if (item.CapabilityDefinitionId is not Guid capabilityId)
                        throw new InvalidOperationException("Unbound card capability");
                    var definition = await db.CatalogCapabilityDefinitions
                        .AsNoTracking().SingleOrDefaultAsync(x => x.Id == capabilityId, ct)
                        ?? throw new InvalidOperationException("Missing display capability");
                    if (!values.TryGetValue(definition.Code, out var allowance))
                        throw new InvalidOperationException(
                            "Plan card advertises an unentitled Capability");
                    // Disabled features/zero allowances must not appear as
                    // positive claims inherited from an older parent version.
                    if (allowance.BooleanValue == false ||
                        (allowance.IntegerValue == 0 && !allowance.IsUnlimited))
                        continue;
                    key = "C:" + capabilityId;
                }
                else if (item.Kind == "Benefit")
                {
                    key = "B:" + item.Title.Trim();
                }
                else
                    throw new InvalidOperationException("Unknown plan card item kind");

                displayByKey[key] = (depth, item);
            }
        }
        var displayRows = displayByKey.Values
            .OrderBy(x => x.Depth)
            .ThenBy(x => x.Item.SortOrder)
            .ThenBy(x => x.Item.Id)
            .Select(x => x.Item)
            .ToArray();

        return new ResolvedCatalogPlan(
            plan.Id, selected.Id, plan.Code,
            selected.DisplayTitle, selected.DisplaySubtitle, selected.BadgeText,
            selected.IsRecommended, selected.DisplayOrder,
            codes.ToArray(),
            values.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray(),
            activeOffers,
            displayRows.Select(x => new ResolvedCatalogDisplayItem(
                x.Kind, x.Title, x.Description, x.SortOrder, x.IsHighlighted))
                .ToArray());
    }

    private static ResolvedCatalogCapability ParseValue(
        CatalogPlanVersionCapabilityValue value, CapabilityContract source)
    {
        var count = (value.BooleanValue.HasValue ? 1 : 0) +
                    (value.IntegerValue.HasValue ? 1 : 0) +
                    (value.ModeSetJson is not null ? 1 : 0) +
                    (value.IsUnlimited ? 1 : 0);
        if (count != 1)
            throw new InvalidOperationException("Capability must have exactly one typed value");
        if (source.Kind is CapabilityKind.Feature &&
            value.BooleanValue is null ||
            source.Kind is CapabilityKind.AllowedModes &&
            value.ModeSetJson is null ||
            source.Kind is CapabilityKind.CountLimit or CapabilityKind.PeriodQuota &&
            (value.BooleanValue.HasValue || value.ModeSetJson is not null))
            throw new InvalidOperationException("Capability value kind mismatch");

        if (value.IntegerValue is long amount && amount < 0)
            throw new InvalidOperationException("Capability cannot be negative");

        string[] modes = [];
        if (value.ModeSetJson is not null)
        {
            try
            {
                modes = JsonSerializer.Deserialize<string[]>(value.ModeSetJson) ??
                    throw new InvalidOperationException("Null allowed modes");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Malformed allowed modes JSON", ex);
            }
            if (modes.Length is 0 or > 24 ||
                modes.Any(x => x is null || !ModeCode.IsMatch(x)) ||
                modes.Distinct(StringComparer.Ordinal).Count() != modes.Length)
                throw new InvalidOperationException("Invalid or duplicate allowed mode");
        }
        return new ResolvedCatalogCapability(
            source.Code, value.BooleanValue, value.IntegerValue,
            value.IsUnlimited, modes);
    }

    private static ResolvedCatalogCapability Merge(
        ResolvedCatalogCapability prior, ResolvedCatalogCapability next,
        CapabilityMergePolicy policy)
    {
        return policy switch
        {
            CapabilityMergePolicy.Override => next,
            CapabilityMergePolicy.Additive when prior.IsUnlimited || next.IsUnlimited =>
                next with { IsUnlimited = true, IntegerValue = null },
            CapabilityMergePolicy.Additive => next with
            {
                IntegerValue = checked(prior.IntegerValue!.Value +
                                       next.IntegerValue!.Value)
            },
            CapabilityMergePolicy.Maximum => prior.IsUnlimited ? prior :
                next.IsUnlimited ? next :
                next with { IntegerValue =
                    Math.Max(prior.IntegerValue!.Value, next.IntegerValue!.Value) },
            CapabilityMergePolicy.SetUnion => next with
            {
                Modes = prior.Modes.Concat(next.Modes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray()
            },
            CapabilityMergePolicy.DenyWins => next with
            {
                BooleanValue = prior.BooleanValue == true &&
                               next.BooleanValue == true
            },
            _ => throw new InvalidOperationException(
                "Unsupported capability merge for this value kind")
        };
    }
}
