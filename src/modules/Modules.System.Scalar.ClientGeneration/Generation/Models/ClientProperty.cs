namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientProperty(
    string Name,
    ClientType Type,
    bool Required);