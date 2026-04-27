namespace MovieTelopTranscriber.App.Models;

public sealed record SegmentRecord(
    string SegmentId,
    long StartTimestampMs,
    long EndTimestampMs,
    string Text,
    string TextType,
    string? FontFamily,
    double? FontSize,
    string? FontSizeUnit,
    string? TextColor,
    string? StrokeColor,
    string? BackgroundColor,
    double? Confidence,
    int SourceFrameCount);
