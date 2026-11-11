using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities;

/// <summary>
/// Centrally generated high-entropy one-time-revealed code. Only SHA-256
/// fingerprint is retained. Never use weak, customer-selected code values.
/// </summary>
public sealed class CatalogDiscountCoupon : AuditableEntity
{
    public CatalogDiscountCoupon() : base(50) { }
    public string CodeHash { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "Percentage";
    public decimal Value { get; set; }
    public Guid? PlanVersionId { get; set; }
    public string Currency { get; set; } = "IRR";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int MaxUses { get; set; }
    public int MaxUsesPerTenant { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A redemption is created atomically only on definitive settlement.
/// Pending orders holding a coupon reference do not consume any usage.
/// </summary>
public sealed class CatalogCouponRedemption : AuditableEntity, IMustHaveTenant
{
    public CatalogCouponRedemption() : base(51) { }
    public Guid TenantId { get; set; }
    public Guid CouponId { get; set; }
    public Guid OrderId { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
}
