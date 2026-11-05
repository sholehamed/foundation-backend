using Modules.System.FileStorage.Application.Abstraction.Interfaces;

namespace Modules.System.FileStorage.Infrastructure.Services
{
    public class TempFileWorkspace : ITempFileWorkspace
    {
        private readonly string _rootPath;

        public TempFileWorkspace()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "myapp-media");
            Directory.CreateDirectory(_rootPath);
        }

        public string CreateWorkspace(Guid mediaId)
        {
            var path = Path.Combine(_rootPath, mediaId.ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public string GetFilePath(string workspace, string fileName)
        {
            return Path.Combine(workspace, fileName);
        }

        public async Task WriteAsync(string filePath, byte[] data, CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(filePath, data, cancellationToken);
        }

        public Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        public void Cleanup(string workspace)
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, true);
        }
    }
}
