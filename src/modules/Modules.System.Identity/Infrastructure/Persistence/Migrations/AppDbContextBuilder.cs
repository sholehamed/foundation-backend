using Microsoft.EntityFrameworkCore.Design;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence.Migrations
{
    internal class AppDbContextBuilder : IDesignTimeDbContextFactory<IdentityDbContext>
    {
        public IdentityDbContext CreateDbContext(string[] args)
        {
            var DBNAME = "MyHshkDb";

            var SqlConnectionString =
                $"Server={Environment.GetEnvironmentVariable("DataBaseServer")};Database={DBNAME};User Id={Environment.GetEnvironmentVariable("DataBaseUsername")};password={Environment.GetEnvironmentVariable("DataBasePassword")};MultipleActiveResultSets=true;TrustServerCertificate=True";

            var opt =new DbContextOptionsBuilder<IdentityDbContext>();
            opt.UseSqlServer(SqlConnectionString, s => s.MigrationsHistoryTable($"__EFMigrationsHistory_Identity")
                .MigrationsAssembly(typeof(AppDbContextBuilder).Assembly.FullName));
            var db=new IdentityDbContext(opt.Options,null);
            return db;
        }
    }
}
