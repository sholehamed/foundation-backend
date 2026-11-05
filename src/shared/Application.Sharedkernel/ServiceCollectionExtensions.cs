using Application.SharedKernel.Abstractions;
using Application.SharedKernel.Abstractions.Mapping;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using DispatcherClass = Application.SharedKernel.Dispatcher.Dispatcher;
namespace Application.SharedKernel
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCustomCqrs<TProfile>(
            this IServiceCollection services,
            params Assembly[] assemblies) where TProfile : MappingProfile, new()
        {
            services.AddAutoMapper(x => x.AddProfile<TProfile>());

            services.AddScoped<IDispatcher, DispatcherClass>();

            RegisterHandlers(services, assemblies);

            return services;
        }
        public static IServiceCollection AddCustomCqrs(
            this IServiceCollection services,
            params Assembly[] assemblies)
        {
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
            services.AddScoped<IDispatcher, DispatcherClass>();

            RegisterHandlers(services, assemblies);

            return services;
        }

        private static void RegisterHandlers(IServiceCollection services, Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    var interfaces = type.GetInterfaces();

                    foreach (var @interface in interfaces)
                    {
                        if (!@interface.IsGenericType)
                            continue;

                        var genericTypeDefinition = @interface.GetGenericTypeDefinition();

                        if (genericTypeDefinition == typeof(ICommandHandler<,>) ||
                            genericTypeDefinition == typeof(ICommandHandler<>) ||
                            genericTypeDefinition == typeof(IQueryHandler<,>) ||
                            genericTypeDefinition == typeof(INotificationHandler<>) ||
                            genericTypeDefinition == typeof(IDomainEventHandler<>))
                        {
                            services.AddScoped(@interface, type);
                        }
                    }
                }
            }
        }
    }
}
