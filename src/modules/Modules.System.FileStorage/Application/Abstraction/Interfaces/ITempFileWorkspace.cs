namespace Modules.System.FileStorage.Application.Abstraction.Interfaces
{
    public interface ITempFileWorkspace
    {
        string CreateWorkspace(Guid mediaId);
        string GetFilePath(string workspace, string fileName);
        Task WriteAsync(string filePath, byte[] data, CancellationToken cancellationToken = default);
        Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default);
        void Cleanup(string workspace);
    }
}
