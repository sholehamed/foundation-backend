using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class ClientGenerationDocumentTransformer
    : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var tagNames = document.Paths
            .Values
            .Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values)
            .Where(operation => operation.Tags is not null)
            .SelectMany(operation => operation.Tags!)
            .Select(tag => tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        document.Tags ??= new HashSet<OpenApiTag>();

        foreach (var tagName in tagNames)
        {
            var name = tagName!;

            var tag = document.Tags.FirstOrDefault(x =>
                string.Equals(
                    x.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));

            if (tag is null)
            {
                tag = new OpenApiTag
                {
                    Name = name
                };

                document.Tags.Add(tag);
            }

            tag.Extensions ??=
                new Dictionary<string, IOpenApiExtension>();

            var extensionValue = new JsonObject
            {
                ["group"] = name,

                ["downloadUrl"] =
                    $"/__scalar-client/generate/{Uri.EscapeDataString(name)}"
            };

            tag.Extensions["x-client-generation"] =
                new JsonNodeExtension(extensionValue);
        }

        return Task.CompletedTask;
    }
}