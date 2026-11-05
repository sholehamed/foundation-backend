using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class PermissionUiResourceConfigurations:AuditEntityConfiguration<PermissionUiResource>
    {
        public override void Configure(EntityTypeBuilder<PermissionUiResource> builder)
        {
            builder.HasKey(x => new { x.PermissionId, x.UiResourceId });

            builder.Property(x => x.PermissionId)
                .IsRequired();

            builder.Property(x => x.UiResourceId)
                .IsRequired();

            builder.HasOne(x => x.Permission)
                .WithMany(x => x.UiResources)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(x => x.UiResource)
                .WithMany(x => x.PermissionUiResources)
                .HasForeignKey(x => x.UiResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasIndex(x => x.UiResourceId);
            base.Configure(builder);
        }
    }
}
