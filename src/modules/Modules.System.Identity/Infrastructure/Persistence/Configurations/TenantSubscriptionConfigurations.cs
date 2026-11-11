using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Billing records are per-Tenant and queried only through the current
/// tenant context. Plans/PlanVersions remain globally System owned.
/// </summary>
internal sealed class TenantPlanOrderConfiguration : AuditEntityConfiguration<TenantPlanOrder>
{
    public override void Configure(EntityTypeBuilder<TenantPlanOrder> b)
    {
        base.Configure(b);
        b.ToTable("TenantPlanOrders", t =>
        {
            t.HasCheckConstraint("CK_TenantPlanOrders_Status",
                "[Status] IN ('Pending','Settled','Cancelled','Refunded')");
            t.HasCheckConstraint("CK_TenantPlanOrders_Amounts",
                "[GrossAmount] >= 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] <= [GrossAmount] AND [NetAmount] = [GrossAmount] - [DiscountAmount]");
            t.HasCheckConstraint("CK_TenantPlanOrders_BillingCycle",
                "[BillingCycle] IN ('monthly','yearly')");
            t.HasCheckConstraint("CK_TenantPlanOrders_Currency",
                "[Currency] = 'IRR'");
            t.HasCheckConstraint("CK_TenantPlanOrders_Settlement",
                "([Status] <> 'Settled') OR ([SettledAtUtc] IS NOT NULL AND [SettlementKind] IS NOT NULL AND [SettlementReference] IS NOT NULL AND LEN([SettlementReference]) > 0 AND (([NetAmount] = 0 AND [SettlementKind] = 'ZeroTotalCoupon') OR ([NetAmount] > 0 AND [SettlementKind] IN ('GatewayVerified','ExternalManual'))))");
        });
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.BillingCycle).HasMaxLength(24).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.GrossAmount).HasPrecision(19,2);
        b.Property(x => x.DiscountAmount).HasPrecision(19,2);
        b.Property(x => x.NetAmount).HasPrecision(19,2);
        b.Property(x => x.SettlementKind).HasMaxLength(32);
        b.Property(x => x.SettlementReference).HasMaxLength(160);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.Status, x.SettledAtUtc });
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CatalogPlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenantSubscriptionConfiguration : AuditEntityConfiguration<TenantSubscription>
{
    public override void Configure(EntityTypeBuilder<TenantSubscription> b)
    {
        base.Configure(b);
        b.ToTable("TenantSubscriptions", t =>
        {
            t.HasCheckConstraint("CK_TenantSubscriptions_Status",
                "[Status] IN ('Pending','Active','Expired','Cancelled')");
            t.HasCheckConstraint("CK_TenantSubscriptions_Period",
                "[EndsAtUtc] > [StartsAtUtc]");
            t.HasCheckConstraint("CK_TenantSubscriptions_Activation",
                "[Status] <> 'Active' OR ([SourceOrderId] IS NOT NULL AND [ActivatedAtUtc] IS NOT NULL)");
        });
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Status, x.EndsAtUtc });
        // FR-17C-1 supports one active commercial plan per Tenant;
        // upgrades require an atomic replace lifecycle before this invariant
        // is relaxed. Expired rows remain historical/auditable.
        b.HasIndex(x => x.TenantId).IsUnique()
            .HasDatabaseName("UX_TenantSubscriptions_OneActive")
            .HasFilter("[Status] = 'Active'");
        b.HasIndex(x => x.SourceOrderId).IsUnique()
            .HasFilter("[SourceOrderId] IS NOT NULL");
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CatalogPlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TenantPlanOrder>().WithMany().HasForeignKey(x => x.SourceOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
