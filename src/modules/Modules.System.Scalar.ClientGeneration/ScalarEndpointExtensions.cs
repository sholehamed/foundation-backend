using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Scalar.AspNetCore;
using Scalar.ClientGeneration.Generation.Angular;
using Scalar.ClientGeneration.Generation.Models;
using Scalar.ClientGeneration.Generation.TypeScript;
using Scalar.ClientGeneration.Internal;
using Scalar.ClientGeneration.OpenApi;
using System.Text;

namespace Scalar.ClientGeneration;

public static class ScalarEndpointExtensions
{
    public static IEndpointRouteBuilder
        MapScalarClientGenerationAssets(
            this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/__scalar-client/plugin.js",
            () =>
                Results.Bytes(
                    EmbeddedAssets.Plugin,
                    "text/javascript; charset=utf-8")).ExcludeFromDescription();

        endpoints.MapGet(
            "/__scalar-client/config.js",
            () =>
                Results.Bytes(
                    EmbeddedAssets.Config,
                    "text/javascript; charset=utf-8")).ExcludeFromDescription();

        endpoints.MapGet(
     "/__scalar-client/generate/{group}",
     async (
         string group,
                 string? target,
         OpenApiGroupReader groupReader,
 AngularPackageGenerator packageGenerator,
 CancellationToken cancellationToken) =>
     {
         target ??= "angular";

         if (!string.Equals(
                 target,
                 "angular",
                 StringComparison.OrdinalIgnoreCase))
         {
             return Results.BadRequest(
                 $"Client target '{target}' is not supported.");
         }

         var definition =
             await groupReader.ReadAsync(
                 group,
                 cancellationToken);

         var package =
             packageGenerator.Generate(
                 definition);

         return Results.File(
             package.Content,
             package.ContentType,
             package.FileName);
     }).ExcludeFromDescription();
        return endpoints;
    }


    public static IEndpointConventionBuilder
        MapScalarWithClientGeneration(
            this IEndpointRouteBuilder endpoints,
            string route = "/scalar")
    {
        endpoints
            .MapScalarClientGenerationAssets();

        return endpoints.MapScalarApiReference(
            route,
            options =>
            {
                options.WithJavaScriptConfiguration(
                    "/__scalar-client/config.js");
            });
    }


    private static string ToTypeName(
        string value)
    {
        var parts = value
            .Split(
                [
                    '-',
                    '_',
                    ' '
                ],
                StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(
            parts.Select(part =>
                char.ToUpperInvariant(part[0]) +
                part[1..]));
    }
}