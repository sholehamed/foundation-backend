using Scalar.ClientGeneration.Generation.Models;

namespace Scalar.ClientGeneration.Generation.TypeScript;

internal sealed class TypeScriptTypeFormatter
{
    public string Format(
        ClientType type)
    {
        var result =
            MapBaseType(type.Name);

        if (type.IsArray)
        {
            result = $"{result}[]";
        }

        if (type.IsNullable)
        {
            result += " | null";
        }

        return result;
    }

    private static string MapBaseType(
        string type)
    {
        return type switch
        {
            "string" => "string",
            "number" => "number",
            "boolean" => "boolean",
            "object" => "Record<string, unknown>",
            "unknown" => "unknown",
            "any" => "any",
            "void" => "void",

            _ => type
        };
    }
}