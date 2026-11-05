using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.FileStorage.Domain.Entities;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Configurations
{
    internal class MediaBinaryConfiguration:EntityBaseConfiguration<MediaBinary>
    {
        public override void Configure(EntityTypeBuilder<MediaBinary> builder)
        {
            builder.ToTable("MediaBinary");

            builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            builder.Property(x => x.Extension).HasMaxLength(20).IsRequired();
            builder.Property(x => x.Data).HasColumnType("varbinary(max)").IsRequired();
            builder.Property(x => x.FileSize).IsRequired();
            builder.Property(x => x.VariantType).IsRequired();
            base.Configure(builder);
        }
    }
}
