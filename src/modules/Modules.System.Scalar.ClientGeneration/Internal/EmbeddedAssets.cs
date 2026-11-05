using System.Reflection;

namespace Scalar.ClientGeneration.Internal;

internal static class EmbeddedAssets
{
    private static readonly Assembly Assembly =
        typeof(EmbeddedAssets).Assembly;

    public static byte[] Plugin { get; } =
        Read("Scalar.ClientGeneration.Assets.scalar-client-plugin.js");

    public static byte[] Config { get; } =
        Read("Scalar.ClientGeneration.Assets.scalar-client-config.js");

    private static byte[] Read(string resourceName)
    {
        using var stream =
            Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found.\n" +
                $"Available resources:\n{string.Join(
                    "\n",
                    Assembly.GetManifestResourceNames())}");

        using var memory = new MemoryStream();

        stream.CopyTo(memory);

        return memory.ToArray();
    }
}