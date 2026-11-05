namespace Modules.System.FileStorage.Application.Models
{
    public class ImageProcessResult
    {
        public byte[] OptimizedBytes { get; set; } = default!;
        public byte[] ThumbnailBytes { get; set; } = default!;
        public string OptimizedFileName { get; set; } = "optimized.webp";
        public string ThumbnailFileName { get; set; } = "thumb.webp";
        public string OptimizedContentType { get; set; } = "image/webp";
        public string ThumbnailContentType { get; set; } = "image/webp";
        public string OptimizedExtension { get; set; } = ".webp";
        public string ThumbnailExtension { get; set; } = ".webp";
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
