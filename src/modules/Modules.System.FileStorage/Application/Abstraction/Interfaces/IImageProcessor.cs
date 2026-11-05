using Modules.System.FileStorage.Application.Models;

namespace Modules.System.FileStorage.Application.Abstraction.Interfaces
{
    public interface IImageProcessor
    {

        Task<ImageProcessResult> ProcessAsync(byte[] input, CancellationToken cancellationToken = default);

    }
}
