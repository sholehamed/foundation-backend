using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class GroupConfigurations:AuditEntityConfiguration<Group>
    {
        public override void Configure(EntityTypeBuilder<Group> builder)
        {
            builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
            builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
            builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
            base.Configure(builder);
        }
    }
}
