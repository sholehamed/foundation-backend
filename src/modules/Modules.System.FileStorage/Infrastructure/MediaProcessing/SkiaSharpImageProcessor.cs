using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Application.Models;
using SkiaSharp;

namespace Modules.System.FileStorage.Infrastructure.MediaProcessing
{
    public class SkiaSharpImageProcessor : IImageProcessor
    {
        public Task<ImageProcessResult> ProcessAsync(byte[] input, CancellationToken cancellationToken = default)
        {
            using var originalBitmap = SKBitmap.Decode(input);
            if (originalBitmap == null)
                throw new InvalidOperationException("Invalid image.");

            var width = originalBitmap.Width;
            var height = originalBitmap.Height;

            using var originalImage = SKImage.FromBitmap(originalBitmap);

            using var optimizedData = originalImage.Encode(SKEncodedImageFormat.Webp, 75);
            var optimizedBytes = optimizedData.ToArray();

            var thumbSize = ResizeKeepAspect(width, height, 320, 320);
            using var thumbBitmap = originalBitmap.Resize(
                new SKImageInfo(thumbSize.Width, thumbSize.Height),
                SKSamplingOptions.Default);

            using var thumbImage = SKImage.FromBitmap(thumbBitmap!);
            using var thumbData = thumbImage.Encode(SKEncodedImageFormat.Webp, 70);
            var thumbBytes = thumbData.ToArray();

            return Task.FromResult(new ImageProcessResult
            {
                OptimizedBytes = optimizedBytes,
                ThumbnailBytes = thumbBytes,
                Width = width,
                Height = height
            });
        }

        private static (int Width, int Height) ResizeKeepAspect(int width, int height, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / width;
            var ratioY = (double)maxHeight / height;
            var ratio = Math.Min(ratioX, ratioY);

            if (ratio >= 1)
                return (width, height);

            return ((int)(width * ratio), (int)(height * ratio));
        }
    }
}
