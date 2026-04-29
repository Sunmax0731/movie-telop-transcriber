using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record PreviewSelectionRequest(
    int? FrameIndex,
    long? TimestampMs,
    string? SegmentId,
    string? SelectedText,
    string? DetectionId = null,
    IReadOnlyCollection<string>? DetectionIds = null)
{
    public static PreviewSelectionRequest FromTimelineSegment(TimelineSegment selection)
    {
        return new PreviewSelectionRequest(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.Text,
            selection.DetectionId,
            selection.DetectionIds);
    }

    public static PreviewSelectionRequest FromResultRow(ResultRow selection)
    {
        return new PreviewSelectionRequest(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.Text,
            selection.DetectionId,
            selection.DetectionIds);
    }
}

public sealed record MainPagePreviewSelectionState(
    TimelineSegment? TimelineSelection,
    ResultRow? ResultSelection,
    PreviewSelectionRequest? PreviewRequest);
