namespace MovieTelopTranscriber.App.Models;

public sealed record RunSummaryRecord(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string SourceVideoPath,
    double FrameIntervalSeconds,
    string OcrEngine,
    int FrameCount,
    int DetectionCount,
    int SegmentCount,
    int WarningCount,
    int ErrorCount,
    string WorkDirectory,
    string OutputDirectory,
    string JsonPath,
    string SegmentsCsvPath,
    string FramesCsvPath);
