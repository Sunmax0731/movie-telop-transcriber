namespace MovieTelopTranscriber.App.Models;

public sealed record OcrFramePerformanceRecord(
    int FrameIndex,
    long TimestampMs,
    double RequestWriteMs,
    double WorkerInitializationMs,
    double WorkerExecutionMs,
    double ResponseReadMs,
    double AttributeAnalysisMs,
    double AttributeWriteMs,
    double TotalMs);
