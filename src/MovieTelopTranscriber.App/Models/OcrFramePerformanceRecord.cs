namespace MovieTelopTranscriber.App.Models;

public sealed record OcrFramePerformanceRecord(
    int FrameIndex,
    long TimestampMs,
    bool OcrExecuted,
    string SelectionReason,
    double SelectionMs,
    double RoiDifferenceMean,
    double RequestWriteMs,
    double WorkerInitializationMs,
    double WorkerExecutionMs,
    double ResponseReadMs,
    double AttributeAnalysisMs,
    double AttributeWriteMs,
    double TotalMs);
