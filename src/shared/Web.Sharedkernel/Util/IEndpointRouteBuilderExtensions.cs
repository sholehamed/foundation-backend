using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics.CodeAnalysis;
using Web.SharedKernel.Util;

namespace Web.SharedKernel.Util;

public sealed class EndpointTitleMetadata
{
    
    public EndpointTitleMetadata(string title)
    {
        Title = title;
    }

    public string Title { get; }
}

public static class EndpointConventionBuilderExtensions
{
    public static TBuilder WithTitle<TBuilder>(this TBuilder builder, string title)
        where TBuilder : IEndpointConventionBuilder
    {
        builder = builder.WithMetadata(new EndpointTitleMetadata(title));
        return builder;
    }
}
public static class IEndpointRouteBuilderExtensions
{

    public static IEndpointRouteBuilder MapGet(this IEndpointRouteBuilder builder, Delegate handler,
        [StringSyntax("Route")] string pattern = "",
       string displayName = "",Action<RouteHandlerBuilder>? configure = null)
    {
        
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
       var endpoint = builder.MapGet(pattern, handler).WithTitle(displayName)
            .WithName($"{actualClassName}{handler.Method.Name}");
        configure?.Invoke(endpoint);

        return builder;
    }
   
    
    public static IEndpointRouteBuilder MapMethods(this IEndpointRouteBuilder builder, Delegate handler, IEnumerable<string> httpMethods,
        [StringSyntax("Route")] string pattern = "",
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
        var endpoint = builder.MapMethods(pattern, httpMethods, handler).WithTitle(displayName)
            .WithName($"{actualClassName}{handler.Method.Name}");
        configure?.Invoke(endpoint);

        return builder;
    }
    public static IEndpointRouteBuilder MapPost(
       this IEndpointRouteBuilder builder,
       Delegate handler,
       [StringSyntax("Route")] string pattern = "",
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);

        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;

      var endPoint=  builder.MapPost(pattern, handler)
            .WithName(endpointName)
            .WithDisplayName(displayName)
            .WithTitle(displayName);
        configure?.Invoke(endPoint);
        return builder;
    }
    


    public static IEndpointRouteBuilder MapFile(this IEndpointRouteBuilder builder, Delegate handler,
        [StringSyntax("Route")] string pattern = "",
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
        var endpoint = builder.MapPost(pattern, handler)
            .WithName($"{actualClassName}{handler.Method.Name}").WithTitle(displayName)
            .WithMetadata(new RequestSizeLimitAttribute(500 * 1024 * 1024)).DisableAntiforgery();
        ;
        configure?.Invoke(endpoint);

        return builder;
    }


    public static IEndpointRouteBuilder MapPut(this IEndpointRouteBuilder builder, Delegate handler,
        [StringSyntax("Route")] string pattern = "",
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
        var endpoint = builder.MapPut(pattern, handler).WithTitle(displayName)
            .WithName($"{actualClassName}{handler.Method.Name}");
        configure?.Invoke(endpoint);

        return builder;
    }

    public static IEndpointRouteBuilder MapDelete(this IEndpointRouteBuilder builder, Delegate handler,
        [StringSyntax("Route")] string pattern,
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
        var endpoint = builder.MapDelete(pattern, handler).WithTitle(displayName)
            .WithName($"{actualClassName}{handler.Method.Name}");
        configure?.Invoke(endpoint);

        return builder;
    }
    public static IEndpointRouteBuilder MapPatch(this IEndpointRouteBuilder builder, Delegate handler,
        [StringSyntax("Route")] string pattern,
       string displayName = "", Action<RouteHandlerBuilder>? configure = null)
    {
        Guard.Against.AnonymousMethod(handler);
        string actualClassName = handler.Target!.GetType().Name;
        var declaringTypeName = handler.Method.DeclaringType?.Name ?? "Unknown";
        var methodName = handler.Method.Name;
        var endpointName = $"{declaringTypeName}{methodName}";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = endpointName;
        var endpoint = builder.MapPatch(pattern, handler).WithTitle(displayName)
            .WithName($"{actualClassName}{handler.Method.Name}");
        configure?.Invoke(endpoint);

        return builder;
    }
}