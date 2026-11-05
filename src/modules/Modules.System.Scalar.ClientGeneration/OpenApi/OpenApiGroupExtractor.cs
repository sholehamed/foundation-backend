using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiGroupExtractor(
    OpenApiParameterExtractor parameterExtractor,
    OpenApiRequestBodyExtractor requestBodyExtractor,
    OpenApiResponseExtractor responseExtractor)
{
    public ClientApiGroup Extract(
        OpenApiDocument document,
        string group)
    {
        ArgumentNullException.ThrowIfNull(
            document);

        if (string.IsNullOrWhiteSpace(group))
        {
            throw new ArgumentException(
                "Group cannot be empty.",
                nameof(group));
        }

        var operations =
            new List<ClientOperation>();

        foreach (var pathEntry
                 in document.Paths)
        {
            var path =
                pathEntry.Key;

            var pathItem =
                pathEntry.Value;

            if (pathItem.Operations is null)
                continue;

            foreach (var operationEntry
                     in pathItem.Operations)
            {
                var httpMethod =
                    operationEntry.Key;

                var operation =
                    operationEntry.Value;

                var belongsToGroup =
                    operation.Tags?.Any(tag =>
                        string.Equals(
                            tag.Name,
                            group,
                            StringComparison.OrdinalIgnoreCase))
                    == true;

                if (!belongsToGroup)
                    continue;

                var name =
                    !string.IsNullOrWhiteSpace(
                        operation.OperationId)
                        ? operation.OperationId!
                        : CreateFallbackName(
                            httpMethod.Method,
                            path);

                var parameters =
                    parameterExtractor.Extract(
                        pathItem,
                        operation);

                var requestBody =
                    requestBodyExtractor.Extract(
                        operation);

                var response =
                    responseExtractor.Extract(
                        operation);

                operations.Add(
                    new ClientOperation(
                        Name: name,
                        Method:
                            httpMethod.Method
                                .ToUpperInvariant(),
                        Path: path,
                        Summary:
                            operation.Summary,
                        Deprecated:
                            operation.Deprecated,
                        Parameters:
                            parameters,
                        RequestBody:
                            requestBody,
                        Response:
                            response));
            }
        }

        if (operations.Count == 0)
        {
            throw new InvalidOperationException(
                $"No OpenAPI operations found " +
                $"for group '{group}'.");
        }

        return new ClientApiGroup(
            group,
            operations);
    }

    private static string CreateFallbackName(
        string method,
        string path)
    {
        var parts = path
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
                part
                    .Replace("{", string.Empty)
                    .Replace("}", string.Empty)
                    .Replace("-", " ")
                    .Replace("_", " "))
            .SelectMany(part =>
                part.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .Select(part =>
                char.ToUpperInvariant(part[0]) +
                part[1..]);

        return
            method.ToUpperInvariant() +
            string.Concat(parts);
    }
}