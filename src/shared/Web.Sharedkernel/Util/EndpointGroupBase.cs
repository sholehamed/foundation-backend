using Microsoft.AspNetCore.Builder;

namespace Web.SharedKernel.Util;

public abstract class EndpointGroupBase
{
    public string AppName { get; set; }
    public abstract void Map(WebApplication app);
}