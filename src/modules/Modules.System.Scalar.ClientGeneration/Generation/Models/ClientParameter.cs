namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientParameter(
    string Name,
    ClientParameterLocation Location,
    ClientType Type,
    bool Required);