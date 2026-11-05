using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;
namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{

    public class TenantClosureConfiguration : EntityBaseConfiguration<TenantClosure>
    {
        public override void Configure(EntityTypeBuilder<TenantClosure> builder)
        {
            builder.ToTable("TenantClosures", x => x.HasCheckConstraint("CK_TenantClosures_Depth", "[Depth] >= 0"));

            builder.HasKey(x => new { x.AncestorTenantId, x.DescendantTenantId });

            builder.Property(x => x.Depth)
                .IsRequired();


            builder.HasOne(x => x.AncestorTenant)
                .WithMany()
                .HasForeignKey(x => x.AncestorTenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.DescendantTenant)
                .WithMany()
                .HasForeignKey(x => x.DescendantTenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.DescendantTenantId)
                .HasDatabaseName("IX_TenantClosures_DescendantTenantId");

            builder.HasIndex(x => new { x.AncestorTenantId, x.Depth })
                .HasDatabaseName("IX_TenantClosures_AncestorTenantId_Depth");
            base.Configure(builder);
        }
    }

}
