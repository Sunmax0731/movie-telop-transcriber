using System.Diagnostics;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class MainPageAnalysisOutputCoordinator
{
    private readonly ExportPackageWriter _exportPackageWriter;
    private readonly RunLogWriter _runLogWriter;

    public MainPageAnalysisOutputCoordinator(
        ExportPackageWriter? exportPackageWriter = null,
        RunLogWriter? runLogWriter = null)
    {
        _exportPackageWriter = exportPackageWriter ?? new ExportPackageWriter();
        _runLogWriter = runLogWriter ?? new RunLogWriter();
    }

    public async Task<MainPageAnalysisOutputResult> WriteAsync(
        MainPageAnalysisOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        var detectionCount = request.FrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count);
        var errorCount = request.FrameAnalyses.Count(analysis => analysis.Ocr.Status == "error");
        var warningCount = request.FrameAnalyses.Count(analysis => analysis.Ocr.Status == "warning");
        var firstOcrError = request.FrameAnalyses
            .Select(analysis => analysis.Ocr.Error)
            .FirstOrDefault(error => error is not null);

        var processingStopwatch = Stopwatch.StartNew();
        var export = await _exportPackageWriter.WriteAsync(
            request.Metadata,
            request.FrameExtractionResult,
            request.FrameAnalyses,
            request.Segments,
            request.TimelineEdits,
            request.FrameIntervalSeconds,
            request.OcrEngine,
            processingStopwatch.ElapsedMilliseconds,
            warningCount,
            errorCount,
            cancellationToken);
        processingStopwatch.Stop();

        var performanceSummary = BuildPerformanceSummary(
            request.OcrWorkerCount,
            request.WarmupResult,
            request.FrameAnalyses,
            request.FrameExtractionDurationMs,
            request.OcrDurationMs,
            request.SegmentMergeDurationMs,
            processingStopwatch.Elapsed.TotalMilliseconds,
            logWriteDurationMs: 0d);

        var logWriteStopwatch = Stopwatch.StartNew();
        var logWriteResult = await _runLogWriter.WriteSuccessAsync(
            request.FrameExtractionResult,
            request.Metadata,
            export,
            request.FrameAnalyses,
            request.FrameIntervalSeconds,
            request.OcrEngine,
            request.StartedAt,
            DateTimeOffset.Now,
            request.FrameExtractionResult.Frames.Count,
            detectionCount,
            request.Segments.Count,
            warningCount,
            errorCount,
            performanceSummary,
            cancellationToken);
        logWriteStopwatch.Stop();

        return new MainPageAnalysisOutputResult(
            export,
            logWriteResult,
            performanceSummary with { LogWriteMs = logWriteStopwatch.Elapsed.TotalMilliseconds },
            detectionCount,
            warningCount,
            errorCount,
            firstOcrError);
    }

    private static RunPerformanceSummaryRecord BuildPerformanceSummary(
        int ocrWorkerCount,
        OcrWorkerWarmupResult warmupResult,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        double frameExtractionDurationMs,
        double ocrDurationMs,
        double segmentMergeDurationMs,
        double exportWriteDurationMs,
        double logWriteDurationMs)
    {
        var framePerformances = frameAnalyses.Select(analysis => analysis.Performance).ToArray();
        var firstFrameMs = framePerformances.FirstOrDefault()?.TotalMs ?? 0d;
        var averageFrameMs = framePerformances.Length == 0
            ? 0d
            : framePerformances.Average(performance => performance.TotalMs);
        var maxFrameMs = framePerformances.Length == 0
            ? 0d
            : framePerformances.Max(performance => performance.TotalMs);

        return new RunPerformanceSummaryRecord(
            warmupResult.Status,
            ocrWorkerCount,
            frameExtractionDurationMs,
            ocrDurationMs,
            segmentMergeDurationMs,
            exportWriteDurationMs,
            logWriteDurationMs,
            warmupResult.TotalMs,
            framePerformances.Count(performance => performance.OcrExecuted),
            framePerformances.Count(performance => !performance.OcrExecuted),
            framePerformances.Sum(performance => performance.SelectionMs),
            framePerformances.Sum(performance => performance.RequestWriteMs),
            framePerformances.Sum(performance => performance.WorkerInitializationMs),
            framePerformances.Sum(performance => performance.WorkerExecutionMs),
            framePerformances.Sum(performance => performance.ResponseReadMs),
            framePerformances.Sum(performance => performance.AttributeAnalysisMs),
            framePerformances.Sum(performance => performance.AttributeWriteMs),
            firstFrameMs,
            averageFrameMs,
            maxFrameMs);
    }
}
