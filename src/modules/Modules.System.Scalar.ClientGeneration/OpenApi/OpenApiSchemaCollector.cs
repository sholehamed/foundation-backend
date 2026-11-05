using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiSchemaCollector(
    OpenApiTypeResolver typeResolver)
{
    private static readonly HashSet<string>
        BuiltInTypes =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "string",
            "number",
            "boolean",
            "object",
            "unknown",
            "void",
            "any"
        };

    public ClientSchemaGraph Collect(
        OpenApiDocument document,
        ClientApiGroup group)
    {
        if (document.Components?.Schemas is null)
        {
            return new ClientSchemaGraph(
                [],
                []);
        }

        var models =
            new Dictionary<
                string,
                ClientModel>(
                    StringComparer.OrdinalIgnoreCase);

        var enums =
            new Dictionary<
                string,
                ClientEnum>(
                    StringComparer.OrdinalIgnoreCase);

        var visiting =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var rootType
                 in GetRootTypes(group))
        {
            Visit(
                rootType,
                document,
                models,
                enums,
                visiting);
        }

        return new ClientSchemaGraph(
            Models:
                models.Values
                    .OrderBy(x => x.Name)
                    .ToArray(),

            Enums:
                enums.Values
                    .OrderBy(x => x.Name)
                    .ToArray());
    }

    private void Visit(
        ClientType type,
        OpenApiDocument document,
        IDictionary<string, ClientModel> models,
        IDictionary<string, ClientEnum> enums,
        ISet<string> visiting)
    {
        if (IsBuiltIn(type))
            return;

        Visit(
            type.Name,
            document,
            models,
            enums,
            visiting);
    }

    private void Visit(
        string schemaName,
        OpenApiDocument document,
        IDictionary<string, ClientModel> models,
        IDictionary<string, ClientEnum> enums,
        ISet<string> visiting)
    {
        if (BuiltInTypes.Contains(schemaName))
            return;

        if (models.ContainsKey(schemaName)
            || enums.ContainsKey(schemaName))
        {
            return;
        }

        if (!visiting.Add(schemaName))
            return;

        var schemas =
            document.Components?.Schemas;

        if (schemas is null
            || !schemas.TryGetValue(
                schemaName,
                out var schema))
        {
            visiting.Remove(schemaName);
            return;
        }

        //
        // ENUM
        //
        if (schema.Enum is
            { Count: > 0 })
        {
            enums[schemaName] =
                CreateEnum(
                    schemaName,
                    schema);

            visiting.Remove(schemaName);
            return;
        }

        //
        // MODEL
        //
        var properties =
            new List<ClientProperty>();

        if (schema.Properties is not null)
        {
            foreach (var propertyEntry
                     in schema.Properties)
            {
                var propertyName =
                    propertyEntry.Key;

                var propertySchema =
                    propertyEntry.Value;

                var propertyType =
                    typeResolver.Resolve(
                        propertySchema);

                var required =
                    schema.Required?.Contains(
                        propertyName) == true;

                properties.Add(
                    new ClientProperty(
                        Name: propertyName,
                        Type: propertyType,
                        Required: required));

                Visit(
                    propertyType,
                    document,
                    models,
                    enums,
                    visiting);
            }
        }

        models[schemaName] =
            new ClientModel(
                schemaName,
                properties);

        visiting.Remove(schemaName);
    }

    private static ClientEnum CreateEnum(
    string name,
    IOpenApiSchema schema)
    {
        var enumNames =
            GetEnumNames(schema);

        var values =
            new List<ClientEnumValue>();

        for (var i = 0;
             i < schema.Enum!.Count;
             i++)
        {
            var node =
                schema.Enum[i];

            var enumName =
                i < enumNames.Count
                    ? enumNames[i]
                    : $"Value{i}";

            if (node is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(
                    out var stringValue))
            {
                values.Add(
                    new ClientEnumValue(
                        Name: enumName,
                        Value: stringValue,
                        IsString: true));

                continue;
            }

            values.Add(
                new ClientEnumValue(
                    Name: enumName,
                    Value: node.ToJsonString(),
                    IsString: false));
        }

        return new ClientEnum(
            name,
            values);
    }

    private static IReadOnlyList<ClientType>
        GetRootTypes(
            ClientApiGroup group)
    {
        var types =
            new List<ClientType>();

        foreach (var operation
                 in group.Operations)
        {
            foreach (var parameter
                     in operation.Parameters)
            {
                types.Add(
                    parameter.Type);
            }

            if (operation.RequestBody
                is not null)
            {
                types.Add(
                    operation
                        .RequestBody
                        .Type);
            }

            types.Add(
                operation.Response.Type);
        }

        return types;
    }
    private static IReadOnlyList<string>
    GetEnumNames(
        IOpenApiSchema schema)
    {
        if (schema.Extensions is null)
        {
            return [];
        }

        if (!schema.Extensions.TryGetValue(
                "x-enumNames",
                out var extension))
        {
            return [];
        }

        if (extension
            is not JsonNodeExtension jsonExtension)
        {
            return [];
        }

        if (jsonExtension.Node
            is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(x =>
                x?.GetValue<string>())
            .Where(x =>
                !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }
    private static bool IsBuiltIn(
        ClientType type)
    {
        return BuiltInTypes.Contains(
            type.Name);
    }
}