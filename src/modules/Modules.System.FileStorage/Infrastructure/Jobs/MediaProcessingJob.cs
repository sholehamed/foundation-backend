using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Domain.Common;
using Modules.System.FileStorage.Domain.Entities;

namespace Modules.System.FileStorage.Infrastructure.Jobs
{
    public class MediaProcessingJob : IMediaProcessingJob
    {
        private readonly IMediaRepository _mediaRepository;
        private readonly IImageProcessor _imageProcessor;
        private readonly IVideoProcessor _videoProcessor;
        private readonly ITempFileWorkspace _tempFileWorkspace;

        public MediaProcessingJob(
            IMediaRepository mediaRepository,
            IImageProcessor imageProcessor,
            IVideoProcessor videoProcessor,
            ITempFileWorkspace tempFileWorkspace)
        {
            _mediaRepository = mediaRepository;
            _imageProcessor = imageProcessor;
            _videoProcessor = videoProcessor;
            _tempFileWorkspace = tempFileWorkspace;
        }

        public async Task ProcessAsync(Guid mediaId, CancellationToken cancellationToken = default)
        {
            var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken);
            if (media == null)
                return;

            var originalBinary = await _mediaRepository.GetBinaryAsync(mediaId, MediaVariantType.Original, cancellationToken);
            if (originalBinary == null)
            {
                media.MarkFailed("Original binary not found.");
                await _mediaRepository.SaveChangesAsync(cancellationToken);
                return;
            }

            var workspace = _tempFileWorkspace.CreateWorkspace(mediaId);

            try
            {
                media.MarkProcessing();
                await _mediaRepository.SaveChangesAsync(cancellationToken);

                if (media.MediaType == MediaType.Image)
                {
                    var imageResult = await _imageProcessor.ProcessAsync(originalBinary.Data, cancellationToken);

                    await _mediaRepository.DeleteVariantsAsync(mediaId, MediaVariantType.Optimized, MediaVariantType.Thumbnail);

                    await _mediaRepository.AddBinaryAsync(MediaBinary.Create(
                        Guid.NewGuid(),
                        mediaId,
                        MediaVariantType.Optimized,
                        imageResult.OptimizedFileName,
                        imageResult.OptimizedContentType,
                        imageResult.OptimizedExtension,
                        imageResult.OptimizedBytes), cancellationToken);

                    await _mediaRepository.AddBinaryAsync(MediaBinary.Create(
                        Guid.NewGuid(),
                        mediaId,
                        MediaVariantType.Thumbnail,
                        imageResult.ThumbnailFileName,
                        imageResult.ThumbnailContentType,
                        imageResult.ThumbnailExtension,
                        imageResult.ThumbnailBytes), cancellationToken);

                    media.MarkReady(imageResult.Width, imageResult.Height, null);
                }
                else if (media.MediaType == MediaType.Video)
                {
                    var originalExt = Path.GetExtension(originalBinary.FileName);
                    var originalPath = _tempFileWorkspace.GetFilePath(workspace, $"original{originalExt}");

                    await _tempFileWorkspace.WriteAsync(originalPath, originalBinary.Data, cancellationToken);

                    var videoResult = await _videoProcessor.ProcessAsync(originalPath, workspace, cancellationToken);

                    await _mediaRepository.DeleteVariantsAsync(mediaId, MediaVariantType.Optimized, MediaVariantType.Thumbnail);

                    await _mediaRepository.AddBinaryAsync(MediaBinary.Create(
                        Guid.NewGuid(),
                        mediaId,
                        MediaVariantType.Optimized,
                        videoResult.OptimizedFileName,
                        videoResult.OptimizedContentType,
                        videoResult.OptimizedExtension,
                        videoResult.OptimizedBytes), cancellationToken);

                    await _mediaRepository.AddBinaryAsync(MediaBinary.Create(
                        Guid.NewGuid(),
                        mediaId,
                        MediaVariantType.Thumbnail,
                        videoResult.ThumbnailFileName,
                        videoResult.ThumbnailContentType,
                        videoResult.ThumbnailExtension,
                        videoResult.ThumbnailBytes), cancellationToken);

                    media.MarkReady(videoResult.Width, videoResult.Height, videoResult.DurationSeconds);
                }
                else
                {
                    media.MarkReady(null, null, null);
                }

                await _mediaRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                media.MarkFailed(ex.Message);
                await _mediaRepository.SaveChangesAsync(cancellationToken);
                throw;
            }
            finally
            {
                _tempFileWorkspace.Cleanup(workspace);
            }
        }
    }
}