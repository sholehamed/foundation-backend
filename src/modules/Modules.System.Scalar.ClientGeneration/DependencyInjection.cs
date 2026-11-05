using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Angular;
using Scalar.ClientGeneration.Generation.TypeScript;
using Scalar.ClientGeneration.OpenApi;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scalar.ClientGeneration;

public static class DependencyInjection
{
    public static IServiceCollection AddScalarClientGeneration(
        this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer(
    (schema, context, cancellationToken) =>
    {
        var type =
            Nullable.GetUnderlyingType(
                context.JsonTypeInfo.Type)
            ?? context.JsonTypeInfo.Type;

        if (!type.IsEnum)
        {
            return Task.CompletedTask;
        }

        var names =
            Enum.GetNames(type);

        //
        // اگر OpenAPI خودش enum values را نساخته،
        // numeric values را اضافه می‌کنیم.
        //
        if (schema.Enum is null
            || schema.Enum.Count == 0)
        {
            var underlyingType =
                Enum.GetUnderlyingType(type);

            schema.Enum =
                names
                    .Select(name =>
                    {
                        var enumValue =
                            Enum.Parse(
                                type,
                                name);

                        var numericValue =
                            Convert.ChangeType(
                                enumValue,
                                underlyingType,
                                CultureInfo.InvariantCulture);

                        return JsonSerializer
                            .SerializeToNode(
                                numericValue)!;
                    })
                    .ToList();
        }

        //
        // CLR enum names
        //
        var enumNames =
            new JsonArray();

        foreach (var name in names)
        {
            enumNames.Add(name);
        }

        schema.Extensions ??=
            new Dictionary<
                string,
                IOpenApiExtension>();

        schema.Extensions[
            "x-enumNames"
        ] =
            new JsonNodeExtension(
                enumNames);

        return Task.CompletedTask;
    });
            options.AddDocumentTransformer(
                (document, context, cancellationToken) =>
                {
                    var tagNames = context.DescriptionGroups
                        .SelectMany(group => group.Items)
                        .SelectMany(description =>
                            description.ActionDescriptor.EndpointMetadata
                                ?.OfType<ITagsMetadata>()
                                ?? Enumerable.Empty<ITagsMetadata>())
                        .SelectMany(metadata => metadata.Tags)
                        .Where(tag =>
                            !string.IsNullOrWhiteSpace(tag))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    document.Tags ??=
                        new HashSet<OpenApiTag>();

                    foreach (var tagName in tagNames)
                    {
                        var tag = document.Tags
                            .FirstOrDefault(x =>
                                string.Equals(
                                    x.Name,
                                    tagName,
                                    StringComparison.OrdinalIgnoreCase));

                        if (tag is null)
                        {
                            tag = new OpenApiTag
                            {
                                Name = tagName
                            };

                            document.Tags.Add(tag);
                        }

                        tag.Extensions ??=
                            new Dictionary<
                                string,
                                IOpenApiExtension>();

                        tag.Extensions[
                            "x-client-generation"
                        ] =
                            new JsonNodeExtension(
                                new JsonObject
                                {
                                    ["group"] = tagName,

                                    ["downloadUrl"] =
                                        $"/__scalar-client/generate/" +
                                        Uri.EscapeDataString(tagName)
                                });
                    }

                    return Task.CompletedTask;
                });
        });
        services.AddSingleton<
            OpenApiGroupExtractor>();


        services.AddSingleton<
    OpenApiSchemaCollector>();
        services.AddScoped<
            OpenApiGroupReader>();
        services.AddSingleton<OpenApiTypeResolver>();

        services.AddSingleton<
            OpenApiParameterExtractor>();

        services.AddSingleton<
            OpenApiRequestBodyExtractor>();

        services.AddSingleton<
            OpenApiResponseExtractor>();

        services.AddSingleton<
            OpenApiGroupExtractor>();

        services.AddScoped<
            OpenApiGroupReader>();
        services.AddSingleton<
    TypeScriptTypeFormatter>();

        services.AddSingleton<
            TypeScriptModelsGenerator>();
        services.AddSingleton<
    AngularClientGenerator>();
        services.AddSingleton<
    AngularIndexGenerator>();

        services.AddSingleton<
            AngularPackageGenerator>();
        return services;
    }
}