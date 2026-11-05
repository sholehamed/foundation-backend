using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class ResourceConfigurations:AuditEntityConfiguration<Resource>
    {
        public override void Configure(EntityTypeBuilder<Resource> builder)
        {

            builder.Property(x => x.ModuleId)
                .IsRequired();

            builder.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.Description)
                .HasMaxLength(1000);

            builder.Property(x => x.SortOrder)
                .HasDefaultValue(0);

            builder.Property(x => x.IsActive)
                .HasDefaultValue(true);

            builder.HasIndex(x => new { x.ModuleId, x.Code })
                .IsUnique();

            builder.HasIndex(x => new { x.ModuleId, x.IsActive, x.SortOrder });

            builder.HasOne(x => x.Module)
                .WithMany(x => x.Resources)
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
            base.Configure(builder);
        }
    }
}
