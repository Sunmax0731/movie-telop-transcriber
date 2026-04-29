using System.Text;
using System.Text.Json;
using System.Diagnostics;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class RunLogWriter
{
    public async Task<RunLogWriteResult> WriteSuccessAsync(
        FrameExtractionResult frameExtractionResult,
        VideoMetadata sourceVideo,
        ExportWriteResult exportWriteResult,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        double frameIntervalSeconds,
        string ocrEngine,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int frameCount,
        int detectionCount,
        int segmentCount,
        int warningCount,
        int errorCount,
        RunPerformanceSummaryRecord performance,
        CancellationToken cancellationToken = default)
    {
        var logWriteStopwatch = Stopwatch.StartNew();
        var logsDirectory = Path.Combine(frameExtractionResult.RunDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        var status = errorCount > 0 ? "warning" : "success";
        var ocrPerformancePath = Path.Combine(logsDirectory, "ocr-performance.json");
        await using (var stream = File.Create(ocrPerformancePath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                frameAnalyses.Select(analysis => analysis.Performance).ToArray(),
                OcrContractJson.OcrFramePerformanceRecords,
                cancellationToken);
        }

        var summary = new RunSummaryRecord(
            frameExtractionResult.RunId,
            startedAt,
            completedAt,
            status,
            sourceVideo.FilePath,
            frameIntervalSeconds,
            ocrEngine,
            frameCount,
            detectionCount,
            segmentCount,
            warningCount,
            errorCount,
            frameExtractionResult.RunDirectory,
            exportWriteResult.OutputDirectory,
            exportWriteResult.JsonPath,
            exportWriteResult.SegmentsCsvPath,
            exportWriteResult.FramesCsvPath,
            exportWriteResult.SrtPath,
            exportWriteResult.VttPath,
            exportWriteResult.AssPath,
            ocrPerformancePath,
            performance);

        var summaryPath = Path.Combine(logsDirectory, "summary.json");
        await using (var stream = File.Create(summaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, summary, OcrContractJson.RunSummaryRecord, cancellationToken);
        }

        var logPath = Path.Combine(logsDirectory, "run.log");
        await File.WriteAllTextAsync(logPath, BuildRunLog(summary), new UTF8Encoding(false), cancellationToken);
        logWriteStopwatch.Stop();

        var completedSummary = summary with
        {
            Performance = summary.Performance with
            {
                LogWriteMs = logWriteStopwatch.Elapsed.TotalMilliseconds
            }
        };

        await using (var stream = File.Create(summaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, completedSummary, OcrContractJson.RunSummaryRecord, cancellationToken);
        }

        await File.WriteAllTextAsync(logPath, BuildRunLog(completedSummary), new UTF8Encoding(false), cancellationToken);

        return new RunLogWriteResult(logsDirectory, logPath, summaryPath);
    }

    private static string BuildRunLog(RunSummaryRecord summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"run_id={summary.RunId}");
        builder.AppendLine($"status={summary.Status}");
        builder.AppendLine($"started_at={summary.StartedAt:O}");
        builder.AppendLine($"completed_at={summary.CompletedAt:O}");
        builder.AppendLine($"source_video={summary.SourceVideoPath}");
        builder.AppendLine($"frame_interval_seconds={summary.FrameIntervalSeconds}");
        builder.AppendLine($"ocr_engine={summary.OcrEngine}");
        builder.AppendLine($"frame_count={summary.FrameCount}");
        builder.AppendLine($"detection_count={summary.DetectionCount}");
        builder.AppendLine($"segment_count={summary.SegmentCount}");
        builder.AppendLine($"warning_count={summary.WarningCount}");
        builder.AppendLine($"error_count={summary.ErrorCount}");
        builder.AppendLine($"work_directory={summary.WorkDirectory}");
        builder.AppendLine($"output_directory={summary.OutputDirectory}");
        builder.AppendLine($"json_path={summary.JsonPath}");
        builder.AppendLine($"segments_csv_path={summary.SegmentsCsvPath}");
        builder.AppendLine($"frames_csv_path={summary.FramesCsvPath}");
        builder.AppendLine($"srt_path={summary.SrtPath}");
        builder.AppendLine($"vtt_path={summary.VttPath}");
        builder.AppendLine($"ass_path={summary.AssPath}");
        builder.AppendLine($"ocr_performance_path={summary.OcrPerformancePath}");
        builder.AppendLine($"ocr_warmup_status={summary.Performance.OcrWarmupStatus}");
        builder.AppendLine($"frame_extraction_ms={summary.Performance.FrameExtractionMs:F1}");
        builder.AppendLine($"ocr_total_ms={summary.Performance.OcrTotalMs:F1}");
        builder.AppendLine($"segment_merge_ms={summary.Performance.SegmentMergeMs:F1}");
        builder.AppendLine($"export_write_ms={summary.Performance.ExportWriteMs:F1}");
        builder.AppendLine($"log_write_ms={summary.Performance.LogWriteMs:F1}");
        builder.AppendLine($"ocr_warmup_ms={summary.Performance.OcrWarmupMs:F1}");
        builder.AppendLine($"ocr_request_write_ms={summary.Performance.OcrRequestWriteMs:F1}");
        builder.AppendLine($"ocr_worker_initialization_ms={summary.Performance.OcrWorkerInitializationMs:F1}");
        builder.AppendLine($"ocr_worker_execution_ms={summary.Performance.OcrWorkerExecutionMs:F1}");
        builder.AppendLine($"ocr_response_read_ms={summary.Performance.OcrResponseReadMs:F1}");
        builder.AppendLine($"attribute_analysis_ms={summary.Performance.AttributeAnalysisMs:F1}");
        builder.AppendLine($"attribute_write_ms={summary.Performance.AttributeWriteMs:F1}");
        builder.AppendLine($"ocr_first_frame_ms={summary.Performance.OcrFirstFrameMs:F1}");
        builder.AppendLine($"ocr_average_frame_ms={summary.Performance.OcrAverageFrameMs:F1}");
        builder.AppendLine($"ocr_max_frame_ms={summary.Performance.OcrMaxFrameMs:F1}");
        return builder.ToString();
    }
}
