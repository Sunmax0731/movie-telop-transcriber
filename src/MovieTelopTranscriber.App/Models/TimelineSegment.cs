namespace MovieTelopTranscriber.App.Models;

public sealed record TimelineSegment(
    string RangeLabel,
    string Text,
    string StyleSummary,
    int? FrameIndex = null,
    long? TimestampMs = null,
    string? SegmentId = null);
