using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Preflight is NOT a write authorization for metered operations: those must
/// still perform atomic CountLimit/PeriodQuota checks inside the SAME business
/// transaction. "Ready" is only possible when no metered caps are required.
/// </summary>
public sealed record CommercialOperationPreflight(
    string State, string Code, IReadOnlyList<string> MeteredCapabilities)
{
    public bool CanRunWithoutMeter => State == "Ready";
    public bool RequiresAtomicMetering => State == "MeteringRequired";
}

/// <summary>
/// FR-17C-3A: compositional commercial WRITE preflight.
/// Source-owned Module/Resource/Permission/EndpointKey, live DB user grants,
/// current tenant's settled subscription and typed Feature/Mode are ALL needed.
/// Does not register or expose HTTP endpoints, mutate grants, or change
/// existing System/Tenant-core behavior. Default production manifest contains
/// NO commercial operations and therefore denies every request.
/// </summary>
public sealed class TenantCommercialOperationPreflight(
    IdentityDbContext db, ITenantContext tenantContext,
    IReadOnlyCollection<ResourceContract>? sourceResources = null,
    IReadOnlyCollection<CapabilityContract>? sourceCapabilities = null,
    IReadOnlyCollection<ResourceOperationContract>? sourceOperations = null)
{
    private readonly IReadOnlyCollection<ResourceContract> resources =
        sourceResources ?? ModuleResourceContracts.Current();
    private readonly IReadOnlyCollection<CapabilityContract> capabilities =
        sourceCapabilities ?? [];
    private readonly IReadOnlyCollection<ResourceOperationContract> operations =
        sourceOperations ?? [];

    private static CommercialOperationPreflight Deny(string code) =>
        new("Denied", code, []);

    /// <summary>
    /// Caller must pass an EndpointKey from its OWN registered code; do not
    /// take it from a browser or route parameter. The caller must still check
    /// business policies and (for metered operations) execute atomic metering
    /// before committing any write.
    /// </summary>
    public async Task<CommercialOperationPreflight> CheckWriteAsync(
        string endpointKey, string? selectedMode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
            return Deny("operation_not_registered");

        if (ModuleResourceContracts.Validate(resources, capabilities, operations).Count > 0)
            return Deny("invalid_source_catalog");

        var operation = operations.SingleOrDefault(x =>
            string.Equals(x.EndpointKey, endpointKey, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
            return Deny("operation_not_registered");

        var owner = resources.SingleOrDefault(x =>
            string.Equals(x.ModuleCode, operation.ModuleCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ResourceCode, operation.ResourceCode, StringComparison.OrdinalIgnoreCase));
        if (owner is null || owner.ProductClass != ModuleProductClass.Commercial ||
            !owner.PermissionCodes.Contains(operation.PermissionCode,
                StringComparer.OrdinalIgnoreCase))
            return Deny("operation_not_commercial");

        // This first slice targets tenant-local business writes. No implicit
        // access from System/Master, read-only scopes or another home tenant.
        if (tenantContext.IsSystemContext ||
            tenantContext.UserId is not Guid userId || userId == Guid.Empty ||
            tenantContext.ActiveTenantId is not Guid tenantId ||
            tenantId == Guid.Empty || tenantContext.HomeTenantId != tenantId ||
            !tenantContext.WritableTenantIds.Contains(tenantId) ||
            !tenantContext.ReadableTenantIds.Contains(tenantId))
            return Deny("writable_tenant_context_required");

        // The source manifest is necessary but not sufficient: the current
        // live SQL Resource/Permission must still belong to the same module,
        // be active, and be registered under this exact operation's owner.
        var registered = await db.Permissions.AsNoTracking().AnyAsync(x =>
            x.Code == operation.PermissionCode && x.IsActive &&
            x.Resource != null && x.Resource.IsActive &&
            x.Resource.Code == operation.ResourceCode &&
            x.Resource.Module != null && x.Resource.Module.IsActive &&
            x.Resource.Module.Code == operation.ModuleCode, ct);
        if (!registered)
            return Deny("inactive_or_unregistered_resource");

        var granted = await new EffectivePermissionEvaluator(db).HasPermissionAsync(
            userId, tenantId, UserType.Tenant, operation.PermissionCode, ct);
        if (!granted)
            return Deny("permission_not_granted");

        TenantCommercialEntitlementSnapshot entitlement;
        try
        {
            entitlement = await new TenantCommercialEntitlementReader(db, tenantContext)
                .ReadCurrentAsync(DateTime.UtcNow, resources, capabilities, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return Deny("tenant_context_invalid");
        }
        if (!entitlement.HasCommercialEntitlement ||
            !entitlement.ModuleCodes.Contains(operation.ModuleCode,
                StringComparer.OrdinalIgnoreCase))
            return Deny("module_not_entitled");

        var byCode = capabilities.ToDictionary(x => x.Code,
            StringComparer.OrdinalIgnoreCase);
        var resolved = entitlement.Capabilities.ToDictionary(x => x.Code,
            StringComparer.OrdinalIgnoreCase);
        var unmetered = new List<string>();
        var metered = new List<string>();

        foreach (var required in operation.RequiredCapabilities)
        {
            // A missing bound PlanVersion limit is NOT Unlimited.
            if (!byCode.TryGetValue(required, out var definition) ||
                !resolved.TryGetValue(required, out var allowance))
                return Deny("required_capability_missing");

            if (definition.Kind is CapabilityKind.CountLimit or CapabilityKind.PeriodQuota)
            {
                // Validate the signed typed allowance even though the
                // authoritative usage is only known in the transaction.
                if (allowance.BooleanValue is not null ||
                    (allowance.Modes?.Count ?? 0) != 0 ||
                    (allowance.IsUnlimited
                        ? allowance.IntegerValue is not null
                        : allowance.IntegerValue is not >= 0))
                    return Deny("invalid_capability_value");
                metered.Add(required);
            }
            else
            {
                unmetered.Add(required);
            }
        }

        // Reuse the central typed Feature/AllowedModes decision rules.
        // Do NOT supply made-up usage=0 to the policy as proof of capacity.
        var partial = operation with { RequiredCapabilities = unmetered };
        var nonMeterDecision = OperationLimitPolicy.Evaluate(
            partial, capabilities, entitlement.Capabilities, [], selectedMode);
        if (!nonMeterDecision.Permitted)
            return Deny(nonMeterDecision.Code);

        if (metered.Count > 0)
            return new("MeteringRequired", "atomic_usage_checks_required",
                metered.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        return new("Ready", "commercial_operation_ready", []);
    }
}
