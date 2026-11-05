namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientType(
    string Name,
    bool IsArray = false,
    bool IsNullable = false);