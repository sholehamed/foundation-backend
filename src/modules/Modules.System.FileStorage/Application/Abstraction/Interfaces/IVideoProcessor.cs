using Modules.System.FileStorage.Application.Models;

namespace Modules.System.FileStorage.Application.Abstraction.Interfaces
{
    public interface IVideoProcessor
    {
        Task<VideoProcessResult> ProcessAsync(string inputFilePath, string workingDirectory, CancellationToken cancellationToken = default);
    }
}
