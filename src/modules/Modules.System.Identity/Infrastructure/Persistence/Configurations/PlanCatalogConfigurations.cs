using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations;

// All commercial catalog definitions are global/System-owned; NO tenant
// query filter and no assignment of user permissions or TenantModules.
internal sealed class CatalogPlanConfiguration : AuditEntityConfiguration<CatalogPlan>
{
    public override void Configure(EntityTypeBuilder<CatalogPlan> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlans", t =>
        {
            t.HasTrigger("TR_CatalogPlans_ImmutablePublishedIdentity");
            t.HasCheckConstraint("CK_CatalogPlans_Parent", "[ParentPlanId] IS NULL OR [ParentPlanId] <> [Id]");
        });
        b.Property(x => x.Code).HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne<CatalogPlan>().WithMany()
            .HasForeignKey(x => x.ParentPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogPlanVersionConfiguration : AuditEntityConfiguration<CatalogPlanVersion>
{
    public override void Configure(EntityTypeBuilder<CatalogPlanVersion> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlanVersions", t =>
        {
            t.HasTrigger("TR_CatalogPlanVersions_Immutable");
            t.HasCheckConstraint("CK_CatalogPlanVersions_Version", "[VersionNumber] > 0");
            t.HasCheckConstraint("CK_CatalogPlanVersions_Status",
                "[Status] IN ('Draft','Published','Retired')");
            t.HasCheckConstraint("CK_CatalogPlanVersions_PublishedAt",
                "[Status] <> 'Published' OR [PublishedAtUtc] IS NOT NULL");
        });
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.ContentFingerprint).HasMaxLength(64);
        b.Property(x => x.DisplayTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplaySubtitle).HasMaxLength(1000);
        b.Property(x => x.BadgeText).HasMaxLength(80);
        b.HasIndex(x => new { x.PlanId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => new { x.Status, x.DisplayOrder });
        b.HasOne<CatalogPlan>().WithMany()
            .HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.ParentPlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogPlanVersionModuleConfiguration
    : AuditEntityConfiguration<CatalogPlanVersionModule>
{
    public override void Configure(EntityTypeBuilder<CatalogPlanVersionModule> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlanVersionModules", t =>
            t.HasTrigger("TR_CatalogPlanVersionModules_Immutable"));
        b.HasIndex(x => new { x.PlanVersionId, x.ModuleId }).IsUnique();
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Module>().WithMany()
            .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogCapabilityDefinitionConfiguration
    : AuditEntityConfiguration<CatalogCapabilityDefinition>
{
    public override void Configure(EntityTypeBuilder<CatalogCapabilityDefinition> b)
    {
        base.Configure(b);
        b.ToTable("CatalogCapabilityDefinitions", t =>
        {
            t.HasCheckConstraint("CK_CatalogCapabilityDefinitions_Kind",
                "[Kind] IN ('Feature','CountLimit','PeriodQuota','AllowedModes')");
            t.HasCheckConstraint("CK_CatalogCapabilityDefinitions_ValueKind",
                "[ValueKind] IN ('Boolean','Integer','ModeSet')");
            t.HasCheckConstraint("CK_CatalogCapabilityDefinitions_Merge",
                "[MergePolicy] IN ('Override','Additive','Maximum','SetUnion','DenyWins')");
        });
        b.Property(x => x.Code).HasMaxLength(150).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        b.Property(x => x.ValueKind).HasMaxLength(32).IsRequired();
        b.Property(x => x.MergePolicy).HasMaxLength(32).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne<Module>().WithMany()
            .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Resource>().WithMany()
            .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogPlanVersionCapabilityValueConfiguration
    : AuditEntityConfiguration<CatalogPlanVersionCapabilityValue>
{
    public override void Configure(EntityTypeBuilder<CatalogPlanVersionCapabilityValue> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlanVersionCapabilityValues", t =>
        {
            t.HasTrigger("TR_CatalogPlanVersionCapabilityValues_Immutable");
            t.HasCheckConstraint("CK_CatalogPlanVersionCapabilityValues_ExactlyOne",
                "(CASE WHEN [BooleanValue] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [IntegerValue] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [ModeSetJson] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [IsUnlimited] = 1 THEN 1 ELSE 0 END) = 1");
            t.HasCheckConstraint("CK_CatalogPlanVersionCapabilityValues_NonNegative",
                "[IntegerValue] IS NULL OR [IntegerValue] >= 0");
        });
        b.Property(x => x.ModeSetJson).HasMaxLength(2000);
        b.HasIndex(x => new { x.PlanVersionId, x.CapabilityDefinitionId }).IsUnique();
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CatalogCapabilityDefinition>().WithMany()
            .HasForeignKey(x => x.CapabilityDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogPlanPriceOfferConfiguration
    : AuditEntityConfiguration<CatalogPlanPriceOffer>
{
    public override void Configure(EntityTypeBuilder<CatalogPlanPriceOffer> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlanPriceOffers", t =>
        {
            t.HasTrigger("TR_CatalogPlanPriceOffers_Immutable");
            t.HasCheckConstraint("CK_CatalogPlanPriceOffers_Amount", "[Amount] >= 0");
            t.HasCheckConstraint("CK_CatalogPlanPriceOffers_Dates",
                "[SaleEndsAtUtc] IS NULL OR [SaleStartsAtUtc] IS NULL OR [SaleEndsAtUtc] > [SaleStartsAtUtc]");
        });
        b.Property(x => x.BillingCycle).HasMaxLength(24).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 2);
        b.HasIndex(x => new { x.PlanVersionId, x.BillingCycle, x.Currency }).IsUnique();
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogPlanDisplayItemConfiguration
    : AuditEntityConfiguration<CatalogPlanDisplayItem>
{
    public override void Configure(EntityTypeBuilder<CatalogPlanDisplayItem> b)
    {
        base.Configure(b);
        b.ToTable("CatalogPlanDisplayItems", t =>
        {
            t.HasTrigger("TR_CatalogPlanDisplayItems_Immutable");
            t.HasCheckConstraint("CK_CatalogPlanDisplayItems_Kind",
                "[Kind] IN ('Module','Capability','Benefit')");
            t.HasCheckConstraint("CK_CatalogPlanDisplayItems_Owner",
                "([Kind] = 'Module' AND [ModuleId] IS NOT NULL AND [CapabilityDefinitionId] IS NULL) OR ([Kind] = 'Capability' AND [CapabilityDefinitionId] IS NOT NULL AND [ModuleId] IS NULL) OR ([Kind] = 'Benefit' AND [ModuleId] IS NULL AND [CapabilityDefinitionId] IS NULL)");
        });
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.PlanVersionId, x.SortOrder });
        b.HasOne<CatalogPlanVersion>().WithMany()
            .HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Module>().WithMany()
            .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CatalogCapabilityDefinition>().WithMany()
            .HasForeignKey(x => x.CapabilityDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
