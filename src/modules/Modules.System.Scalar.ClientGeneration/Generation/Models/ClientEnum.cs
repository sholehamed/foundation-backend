namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientEnum(
    string Name,
    IReadOnlyList<ClientEnumValue> Values);