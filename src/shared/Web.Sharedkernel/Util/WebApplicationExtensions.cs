using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using Web.SharedKernel.Util;

namespace Web.SharedKernel.Util;

public static class WebApplicationExtensions
{
    public static RouteGroupBuilder MapGroup(this WebApplication app, EndpointGroupBase group)
    {
        var groupName = group.GetType().Name;

        return app
            .MapGroup($"/{group.AppName}/{groupName}")
            .WithTags(groupName);
    }

    public static WebApplication MapEndpoints(this WebApplication app,string appName)
    {
        var endpointGroupType = typeof(EndpointGroupBase);

        var assembly = Assembly.GetCallingAssembly();

        var endpointGroupTypes = assembly.GetExportedTypes()
            .Where(t => t.IsSubclassOf(endpointGroupType));

        foreach (var type in endpointGroupTypes)
            if (Activator.CreateInstance(type) is EndpointGroupBase instance)
            {
                instance.AppName = appName;
                instance.Map(app);
            }

        return app;
    }
}