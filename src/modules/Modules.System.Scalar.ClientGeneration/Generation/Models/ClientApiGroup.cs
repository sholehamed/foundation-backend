namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientApiGroup(
    string Name,
    IReadOnlyList<ClientOperation> Operations);