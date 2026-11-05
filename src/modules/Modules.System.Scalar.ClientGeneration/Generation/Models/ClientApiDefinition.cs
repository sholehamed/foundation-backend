namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientApiDefinition(
    ClientApiGroup Group,
    IReadOnlyList<ClientModel> Models,
    IReadOnlyList<ClientEnum> Enums);