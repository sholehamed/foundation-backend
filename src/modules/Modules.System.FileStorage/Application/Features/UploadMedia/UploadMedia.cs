using Application.SharedKernel.Abstractions.Messaging;
using FluentValidation;
using Hangfire;
using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Domain.Common;
using Modules.System.FileStorage.Domain.Entities;

namespace Modules.System.FileStorage.Application.Features.UploadMedia
{
    public record UploadMediaCommand(
    string FileName,
    string ContentType,
    byte[] Data) : ICommand<Guid>;
    public class UploadMediaCommandValidator : AbstractValidator<UploadMediaCommand>
    {
        public UploadMediaCommandValidator()
        {
            RuleFor(x => x.FileName)
                .NotEmpty()
                .MaximumLength(255);

            RuleFor(x => x.ContentType)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.Data)
                .NotNull()
                .Must(x => x.Length > 0)
                .WithMessage("File data is empty.");
        }
    }
    public class UploadMediaCommandHandler : ICommandHandler<UploadMediaCommand, Guid>
    {
        private readonly IMediaRepository _mediaRepository;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public UploadMediaCommandHandler(
            IMediaRepository mediaRepository,
            IBackgroundJobClient backgroundJobClient)
        {
            _mediaRepository = mediaRepository;
            _backgroundJobClient = backgroundJobClient;
        }

        public async Task<Guid> Handle(UploadMediaCommand request, CancellationToken cancellationToken)
        {
            var mediaId = Guid.CreateVersion7();
            var mediaType = DetectMediaType(request.ContentType, request.FileName);

            var media = Media.Create(
                mediaId,
                request.FileName,
                request.ContentType,
                request.Data.LongLength,
                mediaType);

            var originalBinary = MediaBinary.Create(
                Guid.NewGuid(),
                mediaId,
                MediaVariantType.Original,
                request.FileName,
                request.ContentType,
                Path.GetExtension(request.FileName),
                request.Data);

            media.AddBinary(originalBinary);

            await _mediaRepository.AddAsync(media, cancellationToken);
            await _mediaRepository.SaveChangesAsync(cancellationToken);

            _backgroundJobClient.Enqueue<IMediaProcessingJob>(x =>
                x.ProcessAsync(mediaId, CancellationToken.None));

            return mediaId;
        }

        private static MediaType DetectMediaType(string contentType, string fileName)
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return MediaType.Image;

            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return MediaType.Video;

            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext))
                return MediaType.Image;

            if (new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" }.Contains(ext))
                return MediaType.Video;

            return MediaType.File;
        }
    }

}
