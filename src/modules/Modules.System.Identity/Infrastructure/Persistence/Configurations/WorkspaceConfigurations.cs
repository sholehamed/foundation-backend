using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations;

internal sealed class BusinessProfileConfiguration : AuditEntityConfiguration<BusinessProfile>
{
    public override void Configure(EntityTypeBuilder<BusinessProfile> builder)
    {
        base.Configure(builder);
        builder.ToTable("BusinessProfiles");
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ActivityType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.City).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(400).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.Instagram).HasMaxLength(80);
        builder.Property(x => x.Culture).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Timezone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RequestedPlanKey).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedBillingCycle).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PromoCode).HasMaxLength(40);
    }
}

internal sealed class BranchConfiguration : AuditEntityConfiguration<Branch>
{
    public override void Configure(EntityTypeBuilder<Branch> builder)
    {
        base.Configure(builder);
        builder.ToTable("Branches");
        builder.HasIndex(x => new { x.TenantId, x.IsMain })
            .IsUnique().HasFilter("[IsMain] = 1");
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.City).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(400).IsRequired();
    }
}
