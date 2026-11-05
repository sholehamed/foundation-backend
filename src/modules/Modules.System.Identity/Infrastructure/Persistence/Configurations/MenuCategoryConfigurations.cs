using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class MenuCategoryConfigurations:AuditEntityConfiguration<MenuCategory>
    {
        public override void Configure(EntityTypeBuilder<MenuCategory> builder)
        {
            builder.HasIndex(x => x.IsActive);
            builder.HasMany(x => x.Menus)
                .WithOne(x => x.Category)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.NoAction);
            base.Configure(builder);
        }
    }
}
