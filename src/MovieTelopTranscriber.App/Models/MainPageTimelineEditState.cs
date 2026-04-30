namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageTimelineEditState(
    IReadOnlyList<FrameAnalysisResult> LatestFrameAnalyses,
    IReadOnlyList<SegmentRecord> LatestSegments,
    IReadOnlyList<EditOperationRecord> TimelineEdits,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SegmentDetectionIds,
    int ManualEditSequence);
