using Modules.System.FileStorage.Domain.Common;
using Modules.System.FileStorage.Domain.Entities;

namespace Modules.System.FileStorage.Application.Abstraction.Interfaces
{
    public interface IMediaRepository
    {
        Task AddAsync(Media media, CancellationToken cancellationToken = default);
        Task<Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<MediaBinary?> GetBinaryAsync(Guid mediaId, MediaVariantType variantType, CancellationToken cancellationToken = default);
        Task AddBinaryAsync(MediaBinary binary, CancellationToken cancellationToken = default);
        Task DeleteVariantsAsync(Guid mediaId, params MediaVariantType[] variants);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
        Task<MediaDownloadResult?> OpenDownloadStreamAsync(
       Guid mediaId,
       MediaVariantType variantType,
       CancellationToken cancellationToken = default);
    }
    public sealed class MediaDownloadResult : IAsyncDisposable
    {
        public string FileName { get; init; } = default!;
        public string ContentType { get; init; } = "application/octet-stream";
        public long ContentLength { get; init; }
        public Stream Content { get; init; } = default!;

        public async ValueTask DisposeAsync()
        {
            await Content.DisposeAsync();
        }
    }
}
