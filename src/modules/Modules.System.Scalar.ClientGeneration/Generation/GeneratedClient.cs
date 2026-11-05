namespace Scalar.ClientGeneration.Generation;

internal sealed record GeneratedClient(
    string FileName,
    byte[] Content,
    string ContentType);