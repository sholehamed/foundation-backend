using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Configurations
{
    internal class ApiResourceConfigurations : AuditEntityConfiguration<ApiResource>
    {
        public override void Configure(EntityTypeBuilder<ApiResource> builder)
        {
            builder.HasIndex(x => x.ResourceId);

            builder.Property(x => x.IsDeprecated).HasDefaultValue(false);

            builder.Property(x => x.AllowAnonymous).HasDefaultValue(false);

            builder.Property(x => x.IsActive).HasDefaultValue(true);


            builder.Property(x => x.Title).IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.Description)
                .HasMaxLength(500);

            builder.Property(x => x.Key)
                .IsRequired()
                .HasMaxLength(100);
                 builder.HasIndex(x => x.Key)
                .IsUnique()
                .HasDatabaseName("IX_ApiResource_Key_Unique");
                 
            builder.Property(x => x.RouteTemplate).IsRequired()
                .HasMaxLength(500);
                 builder
                .Property(e => e.HttpMethod)
                .HasConversion<string>(); // ذخیره به صورت "GET", "POST" در دیتابیس

            builder
                .Property(e => e.Source)
                .HasConversion<string>().HasDefaultValue(ApiSource.Auto);

            builder.HasOne(x => x.Resource)
                .WithMany(x => x.ApiResources)
                .HasForeignKey(x => x.ResourceId);

            builder
                .HasIndex(x => new { x.ResourceId,x.HttpMethod, x.RouteTemplate })
                .IsUnique()
                .HasDatabaseName("IX_Resource_ApiResource_Method_Route");
            base.Configure(builder);
        }
    }
}
