using MovieTelopTranscriber.App.Models;
using OpenCvSharp;

namespace MovieTelopTranscriber.App.Services;

public sealed class OpenCvVideoProcessingService
{
    public Task<VideoMetadata> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var capture = new VideoCapture(filePath);
            if (!capture.IsOpened())
            {
                throw new InvalidOperationException($"Failed to open video file: {filePath}");
            }

            var width = Convert.ToInt32(capture.FrameWidth);
            var height = Convert.ToInt32(capture.FrameHeight);
            var fps = capture.Fps;
            var frameCount = capture.FrameCount;
            var durationMs = fps > 0 ? (long)Math.Round((frameCount / fps) * 1000d) : 0L;
            var codec = DecodeFourCc(Convert.ToInt32(capture.Get(VideoCaptureProperties.FourCC)));

            return new VideoMetadata(
                filePath,
                Path.GetFileName(filePath),
                durationMs,
                width,
                height,
                fps,
                codec);
        }, cancellationToken);
    }

    public Task<FrameExtractionResult> ExtractFramesAsync(
        VideoMetadata metadata,
        double intervalSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (intervalSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be greater than zero.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var capture = new VideoCapture(metadata.FilePath);
            if (!capture.IsOpened())
            {
                throw new InvalidOperationException($"Failed to open video file: {metadata.FilePath}");
            }

            var runId = CreateRunId();
            var runDirectory = CreateRunDirectory(runId);
            var framesDirectory = Path.Combine(runDirectory, "frames");
            Directory.CreateDirectory(framesDirectory);

            var durationMs = metadata.DurationMs > 0 ? metadata.DurationMs : EstimateDurationMs(capture);
            var timestamps = BuildCaptureTimestamps(durationMs, intervalSeconds);
            var frames = new List<ExtractedFrameRecord>(timestamps.Count);

            for (var i = 0; i < timestamps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var timestampMs = timestamps[i];
                capture.Set(VideoCaptureProperties.PosMsec, timestampMs);

                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    continue;
                }

                var frameIndex = Convert.ToInt32(capture.Get(VideoCaptureProperties.PosFrames));
                var imagePath = Path.Combine(framesDirectory, $"frame_{frameIndex:D6}_{timestampMs:D8}ms.png");
                Cv2.ImWrite(imagePath, frame);

                frames.Add(new ExtractedFrameRecord(frameIndex, timestampMs, imagePath));
                progress?.Report(((double)(i + 1) / timestamps.Count) * 100d);
            }

            return new FrameExtractionResult(runId, runDirectory, framesDirectory, frames);
        }, cancellationToken);
    }

    private static long EstimateDurationMs(VideoCapture capture)
    {
        var fps = capture.Fps;
        var frameCount = capture.FrameCount;
        return fps > 0 ? (long)Math.Round((frameCount / fps) * 1000d) : 0L;
    }

    private static List<long> BuildCaptureTimestamps(long durationMs, double intervalSeconds)
    {
        var intervalMs = (long)Math.Round(intervalSeconds * 1000d);
        var timestamps = new List<long>();

        if (durationMs <= 0)
        {
            timestamps.Add(0);
            return timestamps;
        }

        for (long timestampMs = 0; timestampMs <= durationMs; timestampMs += intervalMs)
        {
            timestamps.Add(timestampMs);
        }

        if (timestamps.Count == 0 || timestamps[^1] != durationMs)
        {
            timestamps.Add(durationMs);
        }

        return timestamps;
    }

    private static string CreateRunDirectory(string runId)
    {
        var projectRoot = ResolveProjectRoot();
        var runDirectory = Path.Combine(projectRoot, "work", "runs", runId);
        Directory.CreateDirectory(runDirectory);
        return runDirectory;
    }

    private static string ResolveProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var docs = Path.Combine(current.FullName, "docs");
            var src = Path.Combine(current.FullName, "src");
            if (Directory.Exists(docs) && Directory.Exists(src))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string CreateRunId()
    {
        return $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..20];
    }

    private static string DecodeFourCc(int fourCc)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)(fourCc & 0xFF);
        chars[1] = (char)((fourCc >> 8) & 0xFF);
        chars[2] = (char)((fourCc >> 16) & 0xFF);
        chars[3] = (char)((fourCc >> 24) & 0xFF);

        var value = new string(chars).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
