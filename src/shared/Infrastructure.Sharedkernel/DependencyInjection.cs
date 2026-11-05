using Infrastructure.SharedKernel.Persistence.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Infrastructure.SharedKernel
{
    public static class DependencyInjection
    {
        public static void AddBaseInfrastructureServices<TDbContext>(this IServiceCollection services, IConfiguration configuration,string appName, Action<IServiceProvider,DbContextOptionsBuilder>? configureOptions = null) where TDbContext : DbContext
        {


            var DBNAME = configuration["DBNAME"];
            
            var SqlConnectionString =
                $"Server={Environment.GetEnvironmentVariable("DataBaseServer")};Database={DBNAME};User Id={Environment.GetEnvironmentVariable("DataBaseUsername")};password={Environment.GetEnvironmentVariable("DataBasePassword")};MultipleActiveResultSets=true;TrustServerCertificate=True";





            services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
            services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
            services.AddDbContext<TDbContext>((sp, options) =>
            {
                options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());



                options.UseSqlServer(SqlConnectionString,s=>s.MigrationsHistoryTable($"__EFMigrationsHistory_{appName}")
                .MigrationsAssembly(typeof(TDbContext).Assembly.FullName));
                configureOptions?.Invoke(sp,options);

            });

            services.AddTransient<IDbConnection>(_ =>
                new SqlConnection(SqlConnectionString));






        }

    }
}
