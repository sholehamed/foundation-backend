namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientSchemaGraph(
    IReadOnlyList<ClientModel> Models,
    IReadOnlyList<ClientEnum> Enums);