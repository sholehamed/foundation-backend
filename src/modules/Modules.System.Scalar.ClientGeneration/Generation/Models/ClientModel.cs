namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientModel(
    string Name,
    IReadOnlyList<ClientProperty> Properties);