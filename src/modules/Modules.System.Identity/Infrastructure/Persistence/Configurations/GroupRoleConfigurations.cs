using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class GroupRoleConfigurations : AuditEntityConfiguration<GroupRole>
    {
        public override void Configure(EntityTypeBuilder<GroupRole> builder)
        {
            builder.HasIndex(x => new { x.TenantId, x.GroupId, x.RoleId }).IsUnique();

            builder.HasOne(x => x.Group)
                .WithMany(x => x.GroupRoles)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Role)
                .WithMany(x => x.GroupRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            base.Configure(builder);
        }
    }
}
