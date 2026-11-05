namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientResponse(
    int StatusCode,
    ClientType Type,
    string? ContentType);