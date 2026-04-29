namespace MovieTelopTranscriber.App.Models;

public sealed record RunPerformanceSummaryRecord(
    string OcrWarmupStatus,
    double FrameExtractionMs,
    double OcrTotalMs,
    double SegmentMergeMs,
    double ExportWriteMs,
    double LogWriteMs,
    double OcrWarmupMs,
    double OcrRequestWriteMs,
    double OcrWorkerInitializationMs,
    double OcrWorkerExecutionMs,
    double OcrResponseReadMs,
    double AttributeAnalysisMs,
    double AttributeWriteMs,
    double OcrFirstFrameMs,
    double OcrAverageFrameMs,
    double OcrMaxFrameMs);
