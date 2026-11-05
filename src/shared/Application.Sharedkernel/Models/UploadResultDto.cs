namespace Application.SharedKernel.Models
{
    public class UploadResultDto
    {
        public Guid Id { get; set; }
        public string OriginalUrl { get; set; } = default!;
        public string? OptimizedUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string FileName { get; set; } = default!;
        public long Size { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }

}
