namespace Scalar.ClientGeneration.Generation.Models;

internal sealed record ClientRequestBody(
    ClientType Type,
    bool Required,
    string ContentType);