namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientOperation(
    string Name,
    string Method,
    string Path,
    string? Summary,
    bool Deprecated,
    IReadOnlyList<ClientParameter> Parameters,
    ClientRequestBody? RequestBody,
    ClientResponse Response);