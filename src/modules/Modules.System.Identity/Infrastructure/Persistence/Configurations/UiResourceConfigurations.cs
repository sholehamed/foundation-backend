using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class UiResourceConfigurations:AuditEntityConfiguration<UiResource>
    {
        public override void Configure(EntityTypeBuilder<UiResource> builder)
        {
           
            base.Configure(builder);
        }
    }
}
