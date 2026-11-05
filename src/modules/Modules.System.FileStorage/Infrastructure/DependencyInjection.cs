using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.SharedKernel;
using Modules.System.FileStorage.Infrastructure.Jobs;
using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Infrastructure.Persistence.Contexts;
using Modules.System.FileStorage.Infrastructure.MediaProcessing;
using Modules.System.FileStorage.Infrastructure.Services;
using Modules.System.FileStorage.Infrastructure.Persistence.Repositories;
namespace Modules.System.FileStorage.Infrastructure
{
    internal static class DependencyInjection
    {
        public static IServiceCollection AddMediaStorageInfra(this IServiceCollection services, IConfiguration configuration)
        {
            var DBNAME = configuration["DBNAME"];

            var SqlConnectionString =
                $"Server={Environment.GetEnvironmentVariable("DataBaseServer")};Database={DBNAME};User Id={Environment.GetEnvironmentVariable("DataBaseUsername")};password={Environment.GetEnvironmentVariable("DataBasePassword")};MultipleActiveResultSets=true;TrustServerCertificate=True";

            services.AddBaseInfrastructureServices<AppDbContext>(configuration, "mediastorage");
            services.AddScoped<IMediaRepository, MediaRepository>();
            services.AddScoped<IImageProcessor, SkiaSharpImageProcessor>();
            services.AddScoped<IVideoProcessor, FfmpegVideoProcessor>();
            services.AddSingleton<ITempFileWorkspace, TempFileWorkspace>();
            services.AddScoped<IMediaProcessingJob, MediaProcessingJob>();
            services.AddHangfire(config =>
               config.UseSqlServerStorage(SqlConnectionString));

            services.AddHangfireServer();
            return services;

        }

        public static IServiceProvider InitilizeDb(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            return serviceProvider;
        }
    }
}