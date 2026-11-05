using Domain.SharedKernel.Entities;
using Modules.System.FileStorage.Domain.Common;

namespace Modules.System.FileStorage.Domain.Entities
{
    public class Media : Entity
    {
        public string OriginalFileName { get; private set; } = default!;
        public string ContentType { get; private set; } = default!;
        public long FileSize { get; private set; }
        public MediaType MediaType { get; private set; }
        public MediaProcessingStatus ProcessingStatus { get; private set; }
        public int? Width { get; private set; }
        public int? Height { get; private set; }
        public double? DurationSeconds { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ProcessedAt { get; private set; }
        public string? ErrorMessage { get; private set; }

        public virtual ICollection<MediaBinary> Binaries {  get; private set; }=new List<MediaBinary>();

        private Media() { }

        public Media(Guid id) : base(id)
        {
        }

        public static Media Create(
            Guid id,
            string originalFileName,
            string contentType,
            long fileSize,
            MediaType mediaType)
        {
            return new Media(id)
            {
                OriginalFileName = originalFileName,
                ContentType = contentType,
                FileSize = fileSize,
                MediaType = mediaType,
                ProcessingStatus = MediaProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
        }

        public void MarkProcessing()
        {
            ProcessingStatus = MediaProcessingStatus.Processing;
            ErrorMessage = null;
        }

        public void MarkReady(int? width, int? height, double? durationSeconds)
        {
            Width = width;
            Height = height;
            DurationSeconds = durationSeconds;
            ProcessingStatus = MediaProcessingStatus.Ready;
            ProcessedAt = DateTime.UtcNow;
            ErrorMessage = null;
        }

        public void MarkFailed(string? errorMessage)
        {
            ProcessingStatus = MediaProcessingStatus.Failed;
            ErrorMessage = errorMessage;
        }

        public void AddBinary(MediaBinary binary)
        {
            Binaries.Add(binary);
        }
    }
}