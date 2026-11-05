using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class UserPermissionConfigurations:AuditEntityConfiguration<UserPermission>
    {
        public override void Configure(EntityTypeBuilder<UserPermission> builder)
        {
            builder.HasIndex(x => new { x.TenantId, x.UserId, x.PermissionId }).IsUnique();

            builder.HasOne(x => x.User)
                .WithMany(x => x.UserPermissions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Permission)
                .WithMany(x => x.UserPermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
            base.Configure(builder);
        }
    }
}
