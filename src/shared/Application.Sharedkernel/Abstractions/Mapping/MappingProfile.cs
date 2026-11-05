using AutoMapper;
using System.Reflection;

namespace Application.SharedKernel.Abstractions.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        var assembly = Assembly.GetCallingAssembly();
        var types = Assembly.GetCallingAssembly().GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapFrom<>)))
            .ToList();

        foreach (var type in types)
        {
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Mapping");
            method?.Invoke(instance, new object[] { this });
        }
    }
}