using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

public sealed record TenantCommercialEntitlementSnapshot(
    string Status, string Reason,
    Guid? SubscriptionId, Guid? PlanVersionId,
    DateTime? StartsAtUtc, DateTime? EndsAtUtc,
    bool HasCommercialEntitlement,
    IReadOnlyList<string> ModuleCodes,
    IReadOnlyList<ResolvedCatalogCapability> Capabilities);

/// <summary>
/// FR-17C-1. Shadow-only commercial licensing. This does NOT gate or grant
/// Tenant permissions and does not write legacy TenantModules. The tenant is
/// ALWAYS resolved from the authenticated scoped context, not HTTP arguments.
/// </summary>
public sealed class TenantCommercialEntitlementReader(
    IdentityDbContext db, ITenantContext tenantContext)
{
    public static TenantCommercialEntitlementSnapshot Unlicensed(string status, string reason,
        Guid? subscriptionId = null, Guid? versionId = null,
        DateTime? starts = null, DateTime? ends = null) =>
        new(status, reason, subscriptionId, versionId, starts, ends, false, [], []);

    public async Task<TenantCommercialEntitlementSnapshot> ReadCurrentAsync(
        DateTime nowUtc,
        IReadOnlyCollection<ResourceContract>? resourceContracts = null,
        IReadOnlyCollection<CapabilityContract>? capabilityContracts = null,
        CancellationToken ct = default)
    {
        if (tenantContext.IsSystemContext ||
            tenantContext.ActiveTenantId is not Guid tenantId ||
            !tenantContext.ReadableTenantIds.Contains(tenantId))
            throw new UnauthorizedAccessException("Current Tenant context is required.");

        // Tenant-level read permissions are necessary, but not sufficient to
        // turn a closed, inactive or missing business into a licensed Tenant.
        if (!await db.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == tenantId && x.IsActive, ct))
            return Unlicensed("Inactive", "tenant_inactive");

        var current = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == "Active")
            .OrderByDescending(x => x.ActivatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (current is null)
        {
            var hasPending = await db.TenantSubscriptions.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Status == "Pending", ct);
            return Unlicensed(hasPending ? "Pending" : "NotSubscribed",
                hasPending ? "awaiting_verified_settlement" : "no_active_subscription");
        }

        if (current.StartsAtUtc > nowUtc || current.EndsAtUtc <= nowUtc ||
            current.ActivatedAtUtc is null || current.ActivatedAtUtc > nowUtc)
            return Unlicensed("Inactive", "outside_subscription_period",
                current.Id, current.PlanVersionId, current.StartsAtUtc, current.EndsAtUtc);

        // The paid license requires evidence of a server-verified SETTLED
        // order for the exact same Tenant and immutable PlanVersion. A draft
        // order, a promo code, a legacy TenantModule or a requested PlanKey
        // is never evidence of settlement.
        if (current.SourceOrderId is not Guid orderId)
            return Unlicensed("Invalid", "missing_order",
                current.Id, current.PlanVersionId, current.StartsAtUtc, current.EndsAtUtc);

        var order = await db.TenantPlanOrders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orderId && x.TenantId == tenantId, ct);
        if (order is null || order.Status != "Settled" ||
            order.PlanVersionId != current.PlanVersionId ||
            order.SettledAtUtc is not DateTime settled ||
            settled > nowUtc || settled < order.QuotedAtUtc ||
            current.ActivatedAtUtc < settled ||
            string.IsNullOrWhiteSpace(order.SettlementReference) ||
            (order.NetAmount == 0 && order.SettlementKind != "ZeroTotalCoupon") ||
            (order.NetAmount > 0 && order.SettlementKind is not ("GatewayVerified" or "ExternalManual")) ||
            order.GrossAmount < order.DiscountAmount ||
            order.NetAmount != order.GrossAmount - order.DiscountAmount)
            return Unlicensed("Invalid", "no_verified_settled_order",
                current.Id, current.PlanVersionId, current.StartsAtUtc, current.EndsAtUtc);

        try
        {
            var plan = await new PublishedPlanCatalogReader(db).ReadAsync(
                current.PlanVersionId,
                resourceContracts ?? ModuleResourceContracts.Current(),
                capabilityContracts ?? [], nowUtc, ct);

            if (plan.ModuleCodes.Count == 0)
                return Unlicensed("Unavailable", "no_code_owned_commercial_modules",
                    current.Id, current.PlanVersionId, current.StartsAtUtc, current.EndsAtUtc);

            return new("Active", "verified_subscription_shadow",
                current.Id, current.PlanVersionId,
                current.StartsAtUtc, current.EndsAtUtc, true,
                plan.ModuleCodes, plan.Capabilities);
        }
        catch (InvalidOperationException)
        {
            // Stale or undeployed commercial source definitions must not
            // appear as entitlements, even if the payment is settled.
            return Unlicensed("Unavailable", "commercial_catalog_unavailable",
                current.Id, current.PlanVersionId, current.StartsAtUtc, current.EndsAtUtc);
        }
    }
}
