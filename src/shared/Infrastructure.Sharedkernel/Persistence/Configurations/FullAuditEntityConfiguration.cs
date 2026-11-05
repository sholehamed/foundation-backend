using Domain.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.SharedKernel.Persistence.Configurations
{
    public class FullAuditEntityConfiguration<T> : AuditEntityConfiguration<T> where T : class, IEntity, IAuditable, ISoftDelete
    {
        public override void Configure(EntityTypeBuilder<T> builder)
        {
            base.Configure(builder);
            builder.Property(e => e.IsDeleted)
           .IsRequired()
           .HasDefaultValue(false);

            builder.Property(e => e.DeletedBy)
                .IsRequired(false);

            builder.Property(e => e.DeletedAt)
                .IsRequired(false);

            builder.Property(e => e.DeletedByIp)
                .IsRequired(false)
                .HasMaxLength(45);

            // فیلتر Global Query Filter برای Soft Delete
            builder.HasQueryFilter(e => !e.IsDeleted);
            builder.HasIndex(e => new { e.IsDeleted, e.CreatedAt });

        }
    }
}
