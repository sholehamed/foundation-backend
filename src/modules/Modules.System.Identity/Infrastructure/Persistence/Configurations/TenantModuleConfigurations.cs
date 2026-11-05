using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class TenantModuleConfigurations:AuditEntityConfiguration<TenantModule>
    {
        public override void Configure(EntityTypeBuilder<TenantModule> builder)
        {
            builder.HasIndex(x => new { x.TenantId, x.ModuleId }).IsUnique();
          
            base.Configure(builder);
        }
    }
}
