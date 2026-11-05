using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiParameterExtractor(
    OpenApiTypeResolver typeResolver)
{
    public IReadOnlyList<ClientParameter> Extract(
        IOpenApiPathItem pathItem,
        OpenApiOperation operation)
    {
        var parameters =
            new Dictionary<
                (string Name, ParameterLocation Location),
                IOpenApiParameter>();

        //
        // Path-level parameters
        //
        if (pathItem.Parameters is not null)
        {
            foreach (var parameter
                     in pathItem.Parameters)
            {
                AddOrReplace(
                    parameters,
                    parameter);
            }
        }

        //
        // Operation-level overrides
        //
        if (operation.Parameters is not null)
        {
            foreach (var parameter
                     in operation.Parameters)
            {
                AddOrReplace(
                    parameters,
                    parameter);
            }
        }

        return parameters
            .Values
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Name)
                && x.In is not null)
            .Select(Map)
            .ToArray();
    }

    private ClientParameter Map(
        IOpenApiParameter parameter)
    {
        return new ClientParameter(
            Name: parameter.Name!,
            Location:
                MapLocation(parameter.In!.Value),
            Type:
                typeResolver.Resolve(
                    parameter.Schema),
            Required:
                parameter.Required
                || parameter.In ==
                    ParameterLocation.Path);
    }

    private static void AddOrReplace(
        IDictionary<
            (string Name, ParameterLocation Location),
            IOpenApiParameter> target,
        IOpenApiParameter parameter)
    {
        if (string.IsNullOrWhiteSpace(
                parameter.Name))
            return;

        if (parameter.In is null)
            return;

        target[
            (
                parameter.Name!,
                parameter.In.Value
            )
        ] = parameter;
    }

    private static ClientParameterLocation
        MapLocation(
            ParameterLocation location)
    {
        return location switch
        {
            ParameterLocation.Path =>
                ClientParameterLocation.Path,

            ParameterLocation.Query =>
                ClientParameterLocation.Query,

            ParameterLocation.Header =>
                ClientParameterLocation.Header,

            ParameterLocation.Cookie =>
                ClientParameterLocation.Cookie,

            _ => throw new
                NotSupportedException(
                    $"Parameter location " +
                    $"'{location}' is not supported.")
        };
    }
}