using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.FileStorage.Application;
using Modules.System.FileStorage.Infrastructure;
using Web.SharedKernel;
using Web.SharedKernel.Util;
namespace Modules.System.FileStorage
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddMediaStorage(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMediaStorageInfra(configuration);
            services.AddMediaStorageAppServices();
            services.AddBaseApiServices();
            return services;
        }
        public static WebApplication UseMediaStorage(this WebApplication app, IConfiguration configuration)
        {
            app.Services.InitilizeDb();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
            app.MapHangfireDashboardWithNoAuthorizationFilters("/hangfire");
            app.MapEndpoints($"{configuration["BackendPrefix"]}System/MediaStorage");
            return app;
        }
    }
}
