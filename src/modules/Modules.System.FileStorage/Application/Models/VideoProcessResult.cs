namespace Modules.System.FileStorage.Application.Models
{
    public class VideoProcessResult
    {
        public byte[] OptimizedBytes { get; set; } = default!;
        public byte[] ThumbnailBytes { get; set; } = default!;
        public string OptimizedFileName { get; set; } = "optimized.mp4";
        public string ThumbnailFileName { get; set; } = "thumb.jpg";
        public string OptimizedContentType { get; set; } = "video/mp4";
        public string ThumbnailContentType { get; set; } = "image/jpeg";
        public string OptimizedExtension { get; set; } = ".mp4";
        public string ThumbnailExtension { get; set; } = ".jpg";
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
