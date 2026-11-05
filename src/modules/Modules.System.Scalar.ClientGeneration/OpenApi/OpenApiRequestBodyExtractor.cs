using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiRequestBodyExtractor(
    OpenApiTypeResolver typeResolver)
{
    public ClientRequestBody? Extract(
        OpenApiOperation operation)
    {
        var requestBody =
            operation.RequestBody;

        if (requestBody?.Content is null
            || requestBody.Content.Count == 0)
        {
            return null;
        }

        var content =
            SelectContent(
                requestBody.Content);

        if (content.Value?.Schema is null)
        {
            return null;
        }

        return new ClientRequestBody(
            Type:
                typeResolver.Resolve(
                    content.Value.Schema),

            Required:
                requestBody.Required,

            ContentType:
                content.Key);
    }

    private static KeyValuePair<string, TMediaType>
        SelectContent<TMediaType>(
            IDictionary<string, TMediaType> content)
        where TMediaType : Microsoft.OpenApi.OpenApiMediaType
    {
        if (content.TryGetValue(
                "application/json",
                out var json))
        {
            return new KeyValuePair<
                string,
                TMediaType>(
                    "application/json",
                    json);
        }

        var jsonLike =
            content.FirstOrDefault(x =>
                x.Key.EndsWith(
                    "+json",
                    StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(
                jsonLike.Key))
        {
            return jsonLike;
        }

        return content.First();
    }
}