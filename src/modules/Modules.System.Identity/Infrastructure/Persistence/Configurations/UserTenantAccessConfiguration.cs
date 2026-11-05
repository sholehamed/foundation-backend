using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    public class UserTenantAccessConfiguration : AuditEntityConfiguration<UserTenantAccess>
    {
        public override  void Configure(EntityTypeBuilder<UserTenantAccess> builder)
        {
            builder.ToTable("UserTenantAccess",x=>x.HasCheckConstraint(
                "CK_UserTenantAccess_ReadWrite",
                "[CanRead] = 1 OR [CanWrite] = 1"));
            builder.Property(x => x.CanRead)
                .IsRequired();

            builder.Property(x => x.CanWrite)
                .IsRequired();

            builder.Property(x => x.IncludeDescendants)
                .IsRequired();

            builder.Property(x => x.IsDenied)
                .IsRequired();

            builder.Property(x => x.Reason)
                .HasMaxLength(300);


            builder.HasIndex(x => new { x.UserId, x.TenantId })
                .IsUnique()
                .HasDatabaseName("UX_UserTenantAccess_UserId_TenantId");

            builder.HasIndex(x => x.UserId)
                .HasDatabaseName("IX_UserTenantAccess_UserId");

            builder.HasIndex(x => x.TenantId)
                .HasDatabaseName("IX_UserTenantAccess_TenantId");

            builder.HasIndex(x => new { x.UserId, x.ExpiresAt })
                .HasDatabaseName("IX_UserTenantAccess_UserId_ExpiresAt");

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }

}
