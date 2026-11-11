using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations;

internal sealed class CatalogDiscountCouponConfiguration
    : AuditEntityConfiguration<CatalogDiscountCoupon>
{
    public override void Configure(EntityTypeBuilder<CatalogDiscountCoupon> b)
    {
        base.Configure(b);
        b.ToTable("CatalogDiscountCoupons", t =>
        {
            t.HasCheckConstraint("CK_CatalogDiscountCoupons_Kind",
                "[Kind] IN ('Percentage','FixedIRR')");
            t.HasCheckConstraint("CK_CatalogDiscountCoupons_Value",
                "[Value] > 0 AND ([Kind] <> 'Percentage' OR [Value] <= 100)");
            t.HasCheckConstraint("CK_CatalogDiscountCoupons_Dates",
                "[EndsAtUtc] > [StartsAtUtc]");
            t.HasCheckConstraint("CK_CatalogDiscountCoupons_Uses",
                "[MaxUses] > 0 AND [MaxUsesPerTenant] > 0 AND [MaxUsesPerTenant] <= [MaxUses]");
            t.HasCheckConstraint("CK_CatalogDiscountCoupons_Currency", "[Currency] = 'IRR'");
        });
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Value).HasPrecision(19, 2);
        b.HasIndex(x => x.CodeHash).IsUnique();
        b.HasIndex(x => new { x.IsActive, x.StartsAtUtc, x.EndsAtUtc });
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogCouponRedemptionConfiguration
    : AuditEntityConfiguration<CatalogCouponRedemption>
{
    public override void Configure(EntityTypeBuilder<CatalogCouponRedemption> b)
    {
        base.Configure(b);
        b.ToTable("CatalogCouponRedemptions", t =>
            t.HasCheckConstraint("CK_CatalogCouponRedemptions_Discount",
                "[DiscountAmount] > 0"));
        b.Property(x => x.DiscountAmount).HasPrecision(19, 2);
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => new { x.CouponId, x.TenantId, x.RedeemedAtUtc });
        b.HasOne<CatalogDiscountCoupon>().WithMany()
            .HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TenantPlanOrder>().WithMany()
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
