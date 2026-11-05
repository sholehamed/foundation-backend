using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiGroupReader(
    [FromKeyedServices("v1")]
    IOpenApiDocumentProvider documentProvider,
    OpenApiGroupExtractor groupExtractor,
    OpenApiSchemaCollector schemaCollector)
{
    public async Task<ClientApiDefinition>
        ReadAsync(
            string group,
            CancellationToken cancellationToken =
                default)
    {
        var document =
            await documentProvider
                .GetOpenApiDocumentAsync(
                    cancellationToken);

        var apiGroup =
            groupExtractor.Extract(
                document,
                group);

        var schemas =
     schemaCollector.Collect(
         document,
         apiGroup);

        return new ClientApiDefinition(
            Group: apiGroup,
            Models: schemas.Models,
            Enums: schemas.Enums);
    }
}