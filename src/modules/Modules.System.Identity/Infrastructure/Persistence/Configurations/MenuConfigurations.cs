using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class MenuConfigurations : AuditEntityConfiguration<Menu>
    {
        public override void Configure(EntityTypeBuilder<Menu> builder)
        {
            builder.HasIndex(x => x.IsActive);
            builder.HasIndex(x => x.IsHidden);
            builder.HasIndex(x => x.ParentId);
            builder.Property(x => x.Icon)
            .IsRequired()
            .HasMaxLength(50);
            builder.HasMany(x => x.Childs)
               .WithOne(x => x.Parent)
               .HasForeignKey(x => x.ParentId)
               .OnDelete(DeleteBehavior.NoAction);
            builder.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);
            

            base.Configure(builder);
        }
    }
}
