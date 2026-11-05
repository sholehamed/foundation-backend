using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiResponseExtractor(
    OpenApiTypeResolver typeResolver)
{
    public ClientResponse Extract(
        OpenApiOperation operation)
    {
        if (operation.Responses is null
            || operation.Responses.Count == 0)
        {
            return VoidResponse(200);
        }

        var successResponse =
            operation.Responses
                .Select(x => new
                {
                    RawStatus = x.Key,
                    Response = x.Value,
                    StatusCode =
                        ParseStatusCode(x.Key)
                })
                .Where(x =>
                    x.StatusCode is >= 200
                        and <= 299)
                .OrderBy(x => x.StatusCode)
                .FirstOrDefault();

        if (successResponse is null)
        {
            return VoidResponse(200);
        }

        //
        // 204 has no body
        //
        if (successResponse.StatusCode == 204)
        {
            return VoidResponse(204);
        }

        var content =
            successResponse.Response.Content;

        if (content is null
            || content.Count == 0)
        {
            return VoidResponse(
                successResponse.StatusCode);
        }

        var selected =
            SelectContent(content);

        if (selected.Value?.Schema is null)
        {
            return VoidResponse(
                successResponse.StatusCode);
        }

        return new ClientResponse(
            StatusCode:
                successResponse.StatusCode,

            Type:
                typeResolver.Resolve(
                    selected.Value.Schema),

            ContentType:
                selected.Key);
    }

    private static ClientResponse
        VoidResponse(
            int statusCode)
    {
        return new ClientResponse(
            StatusCode: statusCode,
            Type: new ClientType("void"),
            ContentType: null);
    }

    private static int ParseStatusCode(
        string value)
    {
        return int.TryParse(
            value,
            out var code)
                ? code
                : -1;
    }

    private static
        KeyValuePair<string, Microsoft.OpenApi.OpenApiMediaType>
        SelectContent(
            IDictionary<
                string,
                Microsoft.OpenApi.OpenApiMediaType> content)
    {
        if (content.TryGetValue(
                "application/json",
                out var json))
        {
            return new(
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