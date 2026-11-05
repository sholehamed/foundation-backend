using Infrastructure.SharedKernel.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Modules.System.FileStorage.Domain.Entities;
using System.Reflection;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Contexts
{
    public class AppDbContext : BaseDbContext
    {
        public DbSet<Media> Media => Set<Media>();
        public DbSet<MediaBinary> MediaBinaries => Set<MediaBinary>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
           
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = Assembly.GetExecutingAssembly();
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
