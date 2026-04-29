using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record ResultRow(
    string RangeLabel,
    string Category,
    string Text,
    string Detail,
    int? FrameIndex = null,
    long? TimestampMs = null,
    string? SegmentId = null,
    string? DetectionId = null,
    IReadOnlyList<string>? DetectionIds = null);
