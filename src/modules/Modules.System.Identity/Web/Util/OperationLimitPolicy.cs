namespace Modules.System.Identity.Web.Util;

/// <summary>
/// A trusted domain adapter supplies authoritative usage. Neither Current nor Change
/// may be taken from the HTTP client. Change is signed for CountLimit (e.g. disable),
/// and strictly positive for PeriodQuota (a committed billable operation).
/// This is an evaluation input, NOT a quota reservation or ledger entry.
/// </summary>
public sealed record OperationUsageSample(string CapabilityCode, long Current, long Change);

/// <summary>
/// Capability/limit decision ONLY. Permitted never implies a valid subscription,
/// an active Module, a delegated Permission, or authorization to mutate data.
/// </summary>
public sealed record OperationLimitDecision(
    bool Permitted, string Code, string? CapabilityCode = null);

/// <summary>
/// FR-17D-1: Pure, fail-closed decision rules for a source-owned operation.
/// No SQL writes, quota consumption, HTTP middleware, or rollout side effects.
/// Operation has no limit bindings => unrestricted USAGE, not unrestricted ACCESS.
/// A required declared capability absent from the resolved PlanVersion => DENY.
/// </summary>
public static class OperationLimitPolicy
{
    public static OperationLimitDecision Evaluate(
        ResourceOperationContract operation,
        IReadOnlyCollection<CapabilityContract> declarations,
        IReadOnlyCollection<ResolvedCatalogCapability> resolved,
        IReadOnlyCollection<OperationUsageSample> usage,
        string? selectedMode = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(usage);

        if (operation.RequiredCapabilities is null ||
            string.IsNullOrWhiteSpace(operation.ModuleCode) ||
            string.IsNullOrWhiteSpace(operation.ResourceCode))
            return Deny("invalid_operation_contract");

        // An unbound operation has no consumption ceiling. The caller MUST
        // separately check its license, Permission, scope and business policy.
        if (operation.RequiredCapabilities.Count == 0)
            return new(true, "no_usage_limit");

        var definitions = new Dictionary<string, CapabilityContract>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in declarations)
        {
            if (string.IsNullOrWhiteSpace(item.Code) ||
                !definitions.TryAdd(item.Code, item))
                return Deny("invalid_capability_catalog", item.Code);
        }

        var allowances = new Dictionary<string, ResolvedCatalogCapability>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in resolved)
        {
            if (string.IsNullOrWhiteSpace(item.Code) ||
                !allowances.TryAdd(item.Code, item))
                return Deny("invalid_plan_capabilities", item.Code);
        }

        var usageByCode = new Dictionary<string, OperationUsageSample>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in usage)
        {
            if (string.IsNullOrWhiteSpace(item.CapabilityCode) ||
                !usageByCode.TryAdd(item.CapabilityCode, item))
                return Deny("invalid_usage_snapshot", item.CapabilityCode);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in operation.RequiredCapabilities)
        {
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code) ||
                !definitions.TryGetValue(code, out var definition) ||
                !string.Equals(definition.ModuleCode, operation.ModuleCode,
                    StringComparison.OrdinalIgnoreCase) ||
                (definition.ResourceCode is not null &&
                 !string.Equals(definition.ResourceCode, operation.ResourceCode,
                     StringComparison.OrdinalIgnoreCase)))
                return Deny("invalid_operation_capability_binding", code);

            // A capability mentioned by an operation is REQUIRED. This is
            // different from an operation with NO bound capability at all.
            if (!allowances.TryGetValue(code, out var allowance))
                return Deny("required_capability_missing", code);

            if (!ValidValue(definition.Kind, allowance))
                return Deny("invalid_capability_value", code);

            switch (definition.Kind)
            {
                case CapabilityKind.Feature:
                    if (allowance.BooleanValue != true)
                        return Deny("feature_disabled", code);
                    break;

                case CapabilityKind.AllowedModes:
                    if (string.IsNullOrWhiteSpace(selectedMode))
                        return Deny("mode_required", code);
                    if (!allowance.Modes.Contains(selectedMode, StringComparer.Ordinal))
                        return Deny("mode_unavailable", code);
                    break;

                case CapabilityKind.CountLimit:
                case CapabilityKind.PeriodQuota:
                    // Unlimited is an explicit tagged PlanVersion value, never
                    // inferred from null, zero, -1, or a missing allowance.
                    if (allowance.IsUnlimited)
                        break;
                    if (!usageByCode.TryGetValue(code, out var sample))
                        return Deny("authoritative_usage_missing", code);
                    if (sample.Current < 0 ||
                        (definition.Kind == CapabilityKind.PeriodQuota &&
                         sample.Change <= 0))
                        return Deny("invalid_usage_change", code);

                    long projected;
                    try
                    {
                        projected = checked(sample.Current + sample.Change);
                    }
                    catch (OverflowException)
                    {
                        return Deny("invalid_usage_change", code);
                    }
                    if (projected < 0)
                        return Deny("invalid_usage_change", code);

                    // A downgrade may leave existing usage above its ceiling.
                    // Always allow a valid non-growth CountLimit adjustment;
                    // never block cleanup/deactivation due to an exceeded cap.
                    if (definition.Kind == CapabilityKind.CountLimit &&
                        sample.Change <= 0)
                        break;

                    if (projected > allowance.IntegerValue!.Value)
                        return Deny("limit_exceeded", code);
                    break;

                default:
                    return Deny("unsupported_capability_kind", code);
            }
        }

        return new(true, "capability_requirements_satisfied");
    }

    private static bool ValidValue(
        CapabilityKind kind, ResolvedCatalogCapability value)
    {
        var hasModes = value.Modes is { Count: > 0 };
        return kind switch
        {
            CapabilityKind.Feature =>
                value.BooleanValue.HasValue && !value.IntegerValue.HasValue &&
                !value.IsUnlimited && !hasModes,

            CapabilityKind.CountLimit or CapabilityKind.PeriodQuota =>
                !value.BooleanValue.HasValue && !hasModes &&
                (value.IsUnlimited
                    ? !value.IntegerValue.HasValue
                    : value.IntegerValue is >= 0),

            CapabilityKind.AllowedModes =>
                !value.BooleanValue.HasValue && !value.IntegerValue.HasValue &&
                !value.IsUnlimited && hasModes &&
                value.Modes.All(x => !string.IsNullOrWhiteSpace(x)) &&
                value.Modes.Distinct(StringComparer.Ordinal).Count() ==
                value.Modes.Count,

            _ => false
        };
    }

    private static OperationLimitDecision Deny(string reason, string? code = null) =>
        new(false, reason, code);
}
