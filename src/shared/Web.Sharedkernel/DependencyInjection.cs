using Application.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Web.SharedKernel.Util;

namespace Web.SharedKernel
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBaseApiServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            return services;
        }
    }
}