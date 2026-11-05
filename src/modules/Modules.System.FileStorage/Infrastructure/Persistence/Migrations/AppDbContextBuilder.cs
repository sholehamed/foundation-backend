using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.System.FileStorage.Infrastructure.Persistence.Contexts;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Migrations
{
    internal class AppDbContextBuilder : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var DBNAME = "MyRestafeDb";

            var SqlConnectionString =
                $"Server={Environment.GetEnvironmentVariable("DataBaseServer")};Database={DBNAME};User Id={Environment.GetEnvironmentVariable("DataBaseUsername")};password={Environment.GetEnvironmentVariable("DataBasePassword")};MultipleActiveResultSets=true;TrustServerCertificate=True";

            var opt =new DbContextOptionsBuilder<AppDbContext>();
            opt.UseSqlServer(SqlConnectionString, s => s.MigrationsHistoryTable($"__EFMigrationsHistory_MediaStorage")
                .MigrationsAssembly(typeof(AppDbContextBuilder).Assembly.FullName));
            var db=new AppDbContext(opt.Options);
            return db;
        }
    }
}
