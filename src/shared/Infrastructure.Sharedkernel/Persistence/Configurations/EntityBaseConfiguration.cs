using Domain.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.SharedKernel.Persistence.Configurations
{
    public class EntityBaseConfiguration<T> : IEntityTypeConfiguration<T> where T : class,IEntity
    {
        public virtual void Configure(EntityTypeBuilder<T> builder)
        {
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        }
    }
}
