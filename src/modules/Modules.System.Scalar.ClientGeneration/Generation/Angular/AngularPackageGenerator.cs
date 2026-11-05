using System.IO.Compression;
using System.Text;
using Scalar.ClientGeneration.Generation.Models;
using Scalar.ClientGeneration.Generation.TypeScript;

namespace Scalar.ClientGeneration.Generation.Angular;

internal sealed class AngularPackageGenerator(
    TypeScriptModelsGenerator modelsGenerator,
    AngularClientGenerator clientGenerator,
    AngularIndexGenerator indexGenerator)
{
    public GeneratedClient Generate(
        ClientApiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(
            definition);

        var modelsFile =
            modelsGenerator.Generate(
                definition);

        var clientFile =
            clientGenerator.Generate(
                definition);

        var indexFile =
            indexGenerator.Generate(
                definition.Group.Name);

        using var memoryStream =
            new MemoryStream();

        using (
            var archive = new ZipArchive(
                memoryStream,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            AddFile(
                archive,
                modelsFile);

            AddFile(
                archive,
                clientFile);

            AddFile(
                archive,
                indexFile);
        }

        var groupName =
            TypeScriptNameHelper
                .ToKebabCase(
                    definition.Group.Name);

        return new GeneratedClient(
            FileName:
                $"{groupName}-angular-client.zip",

            Content:
                memoryStream.ToArray(),

            ContentType:
                "application/zip");
    }

    private static void AddFile(
        ZipArchive archive,
        GeneratedSourceFile file)
    {
        var entry =
            archive.CreateEntry(
                file.FileName,
                CompressionLevel.Optimal);

        using var stream =
            entry.Open();

        using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));

        writer.Write(
            file.Content);
    }
}