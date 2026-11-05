using Domain.SharedKernel.Entities;
using Modules.System.FileStorage.Domain.Common;

namespace Modules.System.FileStorage.Domain.Entities
{
    public class MediaBinary : Entity
    {
        public Guid MediaId { get; private set; }
        public MediaVariantType VariantType { get; private set; }
        public string FileName { get; private set; } = default!;
        public string ContentType { get; private set; } = default!;
        public string Extension { get; private set; } = default!;
        public byte[] Data { get; private set; } = default!;
        public long FileSize { get; private set; }
        public DateTime CreatedAt { get; private set; }

        public Media Media { get; private set; } = default!;

        private MediaBinary() { }

        public MediaBinary(Guid id) : base(id)
        {
        }

        public static MediaBinary Create(
            Guid id,
            Guid mediaId,
            MediaVariantType variantType,
            string fileName,
            string contentType,
            string extension,
            byte[] data)
        {
            return new MediaBinary(id)
            {
                MediaId = mediaId,
                VariantType = variantType,
                FileName = fileName,
                ContentType = contentType,
                Extension = extension,
                Data = data,
                FileSize = data.LongLength,
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}