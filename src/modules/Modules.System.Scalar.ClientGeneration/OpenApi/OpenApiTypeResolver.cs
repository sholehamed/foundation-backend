using Microsoft.OpenApi;
using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.OpenApi;

internal sealed class OpenApiTypeResolver
{
    public ClientType Resolve(
        IOpenApiSchema? schema)
    {
        if (schema is null)
        {
            return new ClientType("unknown");
        }

        var nullable =
            schema.Type?.HasFlag(
                JsonSchemaType.Null) == true;

        //
        // $ref
        //
        if (schema is OpenApiSchemaReference reference)
        {
            var name =
                reference.Reference?.Id;

            if (!string.IsNullOrWhiteSpace(name))
            {
                return new ClientType(
                    Name: name!,
                    IsNullable: nullable);
            }
        }

        //
        // Array
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.Array) == true)
        {
            var itemType =
                Resolve(schema.Items);

            return new ClientType(
                Name: itemType.Name,
                IsArray: true,
                IsNullable: nullable);
        }

        //
        // String
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.String) == true)
        {
            return new ClientType(
                "string",
                IsNullable: nullable);
        }

        //
        // Integer
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.Integer) == true)
        {
            return new ClientType(
                "number",
                IsNullable: nullable);
        }

        //
        // Number
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.Number) == true)
        {
            return new ClientType(
                "number",
                IsNullable: nullable);
        }

        //
        // Boolean
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.Boolean) == true)
        {
            return new ClientType(
                "boolean",
                IsNullable: nullable);
        }

        //
        // Inline object
        //
        if (schema.Type?.HasFlag(
                JsonSchemaType.Object) == true)
        {
            return new ClientType(
                "object",
                IsNullable: nullable);
        }

        return new ClientType(
            "unknown",
            IsNullable: nullable);
    }
}