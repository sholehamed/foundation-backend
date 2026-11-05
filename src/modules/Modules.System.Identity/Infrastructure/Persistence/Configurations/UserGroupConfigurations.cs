using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class UserGroupConfigurations:AuditEntityConfiguration<UserGroup>
    {
        public override void Configure(EntityTypeBuilder<UserGroup> builder)
        {
            builder.HasIndex(x => new { x.TenantId, x.UserId, x.GroupId }).IsUnique();

            builder.HasOne(x => x.User)
                .WithMany(x => x.UserGroups)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Group)
                .WithMany(x => x.UserGroups)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            base.Configure(builder);
        }
    }
}
