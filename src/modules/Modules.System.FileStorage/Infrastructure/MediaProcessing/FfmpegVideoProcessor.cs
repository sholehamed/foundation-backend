using FFMpegCore;
using FFMpegCore.Enums;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Application.Models;
using System.Drawing;
namespace Modules.System.FileStorage.Infrastructure.MediaProcessing
{
    public class FfmpegVideoProcessor : IVideoProcessor
    {
        private readonly string _outputFormat;
        public FfmpegVideoProcessor(IConfiguration configuration)
        {
            _outputFormat = configuration["videoOutput"];
        }
        public async Task<VideoProcessResult> ProcessAsync(string inputFilePath, string workingDirectory, CancellationToken cancellationToken = default)
        {

            var thumbnailPath = Path.Combine(workingDirectory, "thumb.jpg");

            var analysis = await FFProbe.AnalyseAsync(inputFilePath, cancellationToken: cancellationToken);
            var videoStream = analysis.VideoStreams.FirstOrDefault();
            var optimizedPath = string.Empty;
            switch (_outputFormat)
            {
                case "mp4":
                    optimizedPath = Path.Combine(workingDirectory, $"optimized.{_outputFormat}");
                    await ProcessMp4(inputFilePath, optimizedPath);
                    break;
                case "webm":
                    optimizedPath = Path.Combine(workingDirectory, $"optimized.{_outputFormat}");
                    await ProcessWebm(inputFilePath, optimizedPath);

                    break;
                default:
                    break;
            }

            await FFMpeg.SnapshotAsync(
                inputFilePath,
                thumbnailPath,
                new Size(320, 180),
                TimeSpan.FromSeconds(1));

            var optimizedBytes = await File.ReadAllBytesAsync(optimizedPath, cancellationToken);
            var thumbBytes = await File.ReadAllBytesAsync(thumbnailPath, cancellationToken);

            return new VideoProcessResult
            {
                OptimizedContentType=$"video/{_outputFormat}",
                OptimizedExtension=$".{_outputFormat}",
                OptimizedFileName=$"optimized.{_outputFormat}",
                OptimizedBytes = optimizedBytes,
                ThumbnailBytes = thumbBytes,
                Width = videoStream?.Width,
                Height = videoStream?.Height,
                DurationSeconds = analysis.Duration.TotalSeconds
            };
        }

        private static async Task ProcessMp4(string inputFilePath, string optimizedPath)
        {
            var analysis = await FFProbe.AnalyseAsync(inputFilePath);
            var duration = analysis.Duration;
            var startedAt = DateTime.Now;

            await FFMpegArguments
                            .FromFileInput(inputFilePath)
                            .OutputToFile(optimizedPath, true, options => options
                                .WithVideoCodec(VideoCodec.LibX264)
                                .WithAudioCodec(AudioCodec.Aac)
                                .WithConstantRateFactor(28)
                                .WithFastStart()).NotifyOnProgress(percent =>
                                {
                                    var elapsed = DateTime.UtcNow - startedAt;

                                    TimeSpan? remaining = null;

                                    if (percent > 0)
                                    {
                                        var totalEstimatedSeconds = elapsed.TotalSeconds / (percent / 100.0);
                                        var remainingSeconds = totalEstimatedSeconds - elapsed.TotalSeconds;

                                        if (remainingSeconds > 0)
                                            remaining = TimeSpan.FromSeconds(remainingSeconds);
                                    }

                                    Console.WriteLine(
                                        $"Progress: {percent:0.00}% | " +
                                        $"Elapsed: {elapsed:mm\\:ss} | " +
                                        $"Remaining: {(remaining.HasValue ? remaining.Value.ToString(@"mm\:ss") : "calculating...")}"
                                    );
                                }, duration)
                            .ProcessAsynchronously();
        }
        private static async Task ProcessWebm(string inputFilePath, string optimizedPath)
        {
            var analysis = await FFProbe.AnalyseAsync(inputFilePath);
            var duration = analysis.Duration;
            var startedAt = DateTime.Now;

            await FFMpegArguments
           .FromFileInput(inputFilePath)
           .OutputToFile(optimizedPath, true, options => options
                .WithCustomArgument("-c:v libvpx-vp9")
        .WithCustomArgument("-crf 32")
        .WithCustomArgument("-b:v 0")
        .WithCustomArgument("-c:a libopus")).NotifyOnProgress(percent =>
        {
            var elapsed = DateTime.Now - startedAt;

            TimeSpan? remaining = null;

            if (percent > 0)
            {
                var totalEstimatedSeconds = elapsed.TotalSeconds / (percent / 100.0);
                var remainingSeconds = totalEstimatedSeconds - elapsed.TotalSeconds;

                if (remainingSeconds > 0)
                    remaining = TimeSpan.FromSeconds(remainingSeconds);
            }

            Console.WriteLine(
                $"Progress: {percent:0.00}% | " +
                $"Elapsed: {elapsed:mm\\:ss} | " +
                $"Remaining: {(remaining.HasValue ? remaining.Value.ToString(@"mm\:ss") : "calculating...")}"
            );
        }, duration)
           .ProcessAsynchronously();
        }
    }
}
