using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class MainPageAnalysisOutputCoordinatorTests
{
    [Fact]
    public async Task WriteAsync_WritesExportAndRunLog_WithAggregatedCounts()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "movie-telop-transcriber-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var runDirectory = Path.Combine(tempRoot, "run");
            var framesDirectory = Path.Combine(runDirectory, "frames");
            Directory.CreateDirectory(framesDirectory);

            var frame0Path = Path.Combine(framesDirectory, "frame-0000.png");
            var frame1Path = Path.Combine(framesDirectory, "frame-0001.png");
            await File.WriteAllBytesAsync(frame0Path, []);
            await File.WriteAllBytesAsync(frame1Path, []);

            var metadata = new VideoMetadata(
                Path.Combine(tempRoot, "sample.mp4"),
                "sample.mp4",
                2_000,
                1920,
                1080,
                30d,
                "h264");
            var frames = new[]
            {
                new ExtractedFrameRecord(0, 0, frame0Path),
                new ExtractedFrameRecord(1, 1_000, frame1Path)
            };
            var frameExtractionResult = new FrameExtractionResult("test-run", runDirectory, framesDirectory, frames);
            var warningError = new ProcessingError("WARN", "worker warning", null, true);
            var frameAnalyses = new[]
            {
                new FrameAnalysisResult(
                    frames[0],
                    new OcrWorkerResponse(
                        "req-0",
                        "success",
                        0,
                        0,
                        [new OcrDetectionRecord("det-0", "こんにちは", 0.99d, [])],
                        null),
                    new AttributeAnalysisResult(
                        0,
                        0,
                        "success",
                        [new TelopAttributeRecord("det-0", "こんにちは", 0.99d, "Arial", 42d, "px", "#FFFFFF", "#000000", "#00000000", "dialogue")],
                        null),
                    new OcrFramePerformanceRecord(0, 0, true, "selected", 10d, 0.2d, 2d, 5d, 20d, 3d, 4d, 1d, 45d)),
                new FrameAnalysisResult(
                    frames[1],
                    new OcrWorkerResponse(
                        "req-1",
                        "warning",
                        1,
                        1_000,
                        [],
                        warningError),
                    new AttributeAnalysisResult(
                        1,
                        1_000,
                        "warning",
                        [],
                        warningError),
                    new OcrFramePerformanceRecord(1, 1_000, false, "reused", 8d, 0.1d, 0d, 0d, 0d, 0d, 1d, 0d, 9d))
            };
            SegmentRecord[] segments =
            [
                new SegmentRecord("seg-0", 0, 1_000, "こんにちは", "dialogue", "Arial", 42d, "px", "#FFFFFF", "#000000", "#00000000", 0.99d, 1)
            ];
            EditOperationRecord[] edits =
            [
                new EditOperationRecord("update-text", "seg-0", null, "det-0", "こんにちわ", "こんにちは", 0, 1_000, DateTimeOffset.Parse("2026-04-30T12:00:00+09:00"), "normalized")
            ];
            var request = new MainPageAnalysisOutputRequest(
                metadata,
                frameExtractionResult,
                frameAnalyses,
                segments,
                edits,
                1.0d,
                "paddleocr",
                2,
                DateTimeOffset.Parse("2026-04-30T12:00:00+09:00"),
                100d,
                new OcrWorkerWarmupResult("success", 1d, 2d, 3d, 4d, 10d, null),
                200d,
                30d);

            var coordinator = new MainPageAnalysisOutputCoordinator();

            var result = await coordinator.WriteAsync(request);

            Assert.Equal(1, result.DetectionCount);
            Assert.Equal(1, result.WarningCount);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(warningError, result.FirstOcrError);
            Assert.Equal("success", result.PerformanceSummary.OcrWarmupStatus);
            Assert.Equal(2, result.PerformanceSummary.OcrWorkerCount);
            Assert.Equal(1, result.PerformanceSummary.OcrExecutedFrameCount);
            Assert.Equal(1, result.PerformanceSummary.OcrReusedFrameCount);
            Assert.True(File.Exists(result.Export.JsonPath));
            Assert.True(File.Exists(result.LogWriteResult.SummaryPath));
            Assert.True(File.Exists(result.LogWriteResult.LogPath));

            var runLog = await File.ReadAllTextAsync(result.LogWriteResult.LogPath);
            Assert.Contains("status=success", runLog, StringComparison.Ordinal);
            Assert.Contains("detection_count=1", runLog, StringComparison.Ordinal);
            Assert.Contains("warning_count=1", runLog, StringComparison.Ordinal);
            Assert.Contains("error_count=0", runLog, StringComparison.Ordinal);
            Assert.Contains("ocr_worker_count=2", runLog, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
