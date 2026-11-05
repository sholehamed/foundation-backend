using System.Text;

namespace Scalar.ClientGeneration.Generation.TypeScript;

internal static class TypeScriptNameHelper
{
    public static string Property(
        string name)
    {
        if (IsValidIdentifier(name))
        {
            return name;
        }

        return $"'{name.Replace("'", "\\'")}'";
    }

    public static string ToKebabCase(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "client";
        }

        var builder =
            new StringBuilder();

        for (var i = 0;
             i < value.Length;
             i++)
        {
            var current =
                value[i];

            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(
                    char.ToLowerInvariant(current));

                continue;
            }

            if (current is ' ' or '_')
            {
                if (builder.Length > 0
                    && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            builder.Append(
                char.ToLowerInvariant(current));
        }

        return builder
            .ToString()
            .Trim('-');
    }

    private static bool IsValidIdentifier(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0])
              || value[0] is '_' or '$'))
        {
            return false;
        }

        for (var i = 1;
             i < value.Length;
             i++)
        {
            var character =
                value[i];

            if (!(char.IsLetterOrDigit(character)
                  || character is '_' or '$'))
            {
                return false;
            }
        }

        return true;
    }
    public static string ToPascalCase(
    string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Api";

        var parts = value
            .Split(
                ['-', '_', ' '],
                StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(
            parts.Select(part =>
                char.ToUpperInvariant(part[0]) +
                part[1..]));
    }

    public static string ToCamelCase(
        string value)
    {
        var pascal =
            ToPascalCase(value);

        if (string.IsNullOrEmpty(pascal))
            return "operation";

        return
            char.ToLowerInvariant(pascal[0]) +
            pascal[1..];
    }
}