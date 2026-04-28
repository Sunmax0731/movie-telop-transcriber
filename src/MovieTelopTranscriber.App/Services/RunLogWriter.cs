using System.Text;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class RunLogWriter
{
    public async Task<RunLogWriteResult> WriteSuccessAsync(
        FrameExtractionResult frameExtractionResult,
        VideoMetadata sourceVideo,
        ExportWriteResult exportWriteResult,
        double frameIntervalSeconds,
        string ocrEngine,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int frameCount,
        int detectionCount,
        int segmentCount,
        int warningCount,
        int errorCount,
        CancellationToken cancellationToken = default)
    {
        var logsDirectory = Path.Combine(frameExtractionResult.RunDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        var status = errorCount > 0 ? "warning" : "success";
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
            exportWriteResult.FramesCsvPath);

        var summaryPath = Path.Combine(logsDirectory, "summary.json");
        await using (var stream = File.Create(summaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, summary, OcrContractJson.RunSummaryRecord, cancellationToken);
        }

        var logPath = Path.Combine(logsDirectory, "run.log");
        await File.WriteAllTextAsync(logPath, BuildRunLog(summary), new UTF8Encoding(false), cancellationToken);

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
        return builder.ToString();
    }
}
