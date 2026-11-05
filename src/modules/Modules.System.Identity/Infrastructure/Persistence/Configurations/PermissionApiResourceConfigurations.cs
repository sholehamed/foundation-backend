using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class PermissionApiResourceConfigurations:AuditEntityConfiguration<PermissionApiResource>
    {
        public override void Configure(EntityTypeBuilder<PermissionApiResource> builder)
        {
            builder.HasKey(x => new { x.PermissionId, x.ApiResourceId });

            builder.Property(x => x.PermissionId)
                .IsRequired();

            builder.Property(x => x.ApiResourceId)
                .IsRequired();

            builder.HasOne(x => x.Permission)
                .WithMany(x => x.ApiResources)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(x => x.ApiResource)
                .WithMany(x => x.PermissionApiResources)
                .HasForeignKey(x => x.ApiResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasIndex(x => x.ApiResourceId);
            base.Configure(builder);
        }
    }
}
