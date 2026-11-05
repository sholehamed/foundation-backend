using Application.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Modules.System.FileStorage.Application
{
    internal static class DependencyInjection
    {
        public static IServiceCollection AddMediaStorageAppServices(this IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();
            services.AddCustomCqrs(assembly);
            return services;
        }
    }
}
