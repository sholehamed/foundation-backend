using Infrastructure.SharedKernel.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modules.System.FileStorage.Domain.Entities;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Configurations
{
    internal class MediaConfiguration:EntityBaseConfiguration<Media>
    {
        public override void Configure(EntityTypeBuilder<Media> builder)
        {
            builder.ToTable("Media");

            builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            builder.Property(x => x.FileSize).IsRequired();
            builder.Property(x => x.MediaType).IsRequired();
            builder.Property(x => x.ProcessingStatus).IsRequired();
            builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

            builder.HasMany(x => x.Binaries)
                .WithOne(x => x.Media)
                .HasForeignKey(x => x.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
            base.Configure(builder);
        }
    }
}
