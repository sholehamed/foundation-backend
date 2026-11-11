using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities;

/// <summary>
/// A server-owned commercial quote/order. This table is NOT a receipt:
/// entitlement requires a separately verified Settled state. No public
/// endpoint in FR-17C-1 writes or settles an order.
/// </summary>
public sealed class TenantPlanOrder : AuditableEntity, IMustHaveTenant
{
    public TenantPlanOrder() : base(48) { }

    public Guid TenantId { get; set; }
    public Guid PlanVersionId { get; set; }
    public Guid? CouponId { get; set; }
    public string Status { get; set; } = "Pending";
    public string BillingCycle { get; set; } = "monthly";
    public string Currency { get; set; } = "IRR";
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime QuotedAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public string? SettlementKind { get; set; }
    public string? SettlementReference { get; set; }
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// One commercial license period, referencing an immutable PlanVersion and
/// the server-settled order that justified activation. No UserPermission,
/// RolePermission or legacy TenantModule flags are ever issued by this row.
/// </summary>
public sealed class TenantSubscription : AuditableEntity, IMustHaveTenant
{
    public TenantSubscription() : base(49) { }

    public Guid TenantId { get; set; }
    public Guid PlanVersionId { get; set; }
    public Guid? SourceOrderId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
