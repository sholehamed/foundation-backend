namespace Modules.System.FileStorage.Application.Abstraction.Interfaces
{
    public interface IMediaProcessingJob
    {
        Task ProcessAsync(Guid mediaId, CancellationToken cancellationToken = default);
    }
}
