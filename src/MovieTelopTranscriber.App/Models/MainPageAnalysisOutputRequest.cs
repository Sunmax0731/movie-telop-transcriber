namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageAnalysisOutputRequest(
    VideoMetadata Metadata,
    FrameExtractionResult FrameExtractionResult,
    IReadOnlyList<FrameAnalysisResult> FrameAnalyses,
    IReadOnlyList<SegmentRecord> Segments,
    IReadOnlyList<EditOperationRecord> TimelineEdits,
    double FrameIntervalSeconds,
    string OcrEngine,
    int OcrWorkerCount,
    DateTimeOffset StartedAt,
    double FrameExtractionDurationMs,
    OcrWorkerWarmupResult WarmupResult,
    double OcrDurationMs,
    double SegmentMergeDurationMs);
