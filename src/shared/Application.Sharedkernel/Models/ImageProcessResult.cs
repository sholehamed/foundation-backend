namespace Application.SharedKernel.Models
{
    public class ImageProcessResult
    {
        public Stream OriginalNormalizedStream { get; set; } = default!;
        public Stream OptimizedStream { get; set; } = default!;
        public Stream ThumbnailStream { get; set; } = default!;

        public string OptimizedExtension { get; set; } = ".webp";
        public string ThumbnailExtension { get; set; } = ".webp";

        public int Width { get; set; }
        public int Height { get; set; }
    }
}
