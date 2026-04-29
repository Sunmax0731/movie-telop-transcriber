using System;
using System.Collections.Generic;
using System.Linq;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public static class PreviewSelectionCoordinator
{
    public static MainPagePreviewSelectionState SelectFirst(
        IReadOnlyList<TimelineSegment> timelineSegments,
        IReadOnlyList<ResultRow> resultRows)
    {
        var firstTimeline = timelineSegments.FirstOrDefault();
        var firstResult = resultRows.FirstOrDefault();
        return BuildTimelineSelectionState(firstTimeline, resultRows, firstResult);
    }

    public static MainPagePreviewSelectionState BuildTimelineSelectionState(
        TimelineSegment? timelineSelection,
        IReadOnlyList<ResultRow> resultRows,
        ResultRow? currentResultSelection = null)
    {
        if (timelineSelection is null)
        {
            return new MainPagePreviewSelectionState(null, currentResultSelection, null);
        }

        var matchedResult = FindMatchingResultRow(timelineSelection, resultRows) ?? currentResultSelection;
        return new MainPagePreviewSelectionState(
            timelineSelection,
            matchedResult,
            PreviewSelectionRequest.FromTimelineSegment(timelineSelection));
    }

    public static MainPagePreviewSelectionState BuildResultSelectionState(
        ResultRow? resultSelection,
        IReadOnlyList<TimelineSegment> timelineSegments,
        TimelineSegment? currentTimelineSelection = null)
    {
        if (resultSelection is null)
        {
            return new MainPagePreviewSelectionState(currentTimelineSelection, null, null);
        }

        var matchedTimeline = FindMatchingTimelineSegment(resultSelection, timelineSegments) ?? currentTimelineSelection;
        return new MainPagePreviewSelectionState(
            matchedTimeline,
            resultSelection,
            PreviewSelectionRequest.FromResultRow(resultSelection));
    }

    public static MainPagePreviewSelectionState BuildFrameSelectionState(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<TimelineSegment> timelineSegments,
        IReadOnlyList<ResultRow> resultRows,
        int index,
        TimelineSegment? currentTimelineSelection = null,
        ResultRow? currentResultSelection = null)
    {
        if (frameAnalyses.Count == 0)
        {
            return new MainPagePreviewSelectionState(currentTimelineSelection, currentResultSelection, null);
        }

        var normalizedIndex = ((index % frameAnalyses.Count) + frameAnalyses.Count) % frameAnalyses.Count;
        var nextAnalysis = frameAnalyses[normalizedIndex];
        var matchingTimelineRows = timelineSegments
            .Where(row => row.FrameIndex == nextAnalysis.Frame.FrameIndex && row.TimestampMs == nextAnalysis.Frame.TimestampMs)
            .ToArray();

        if (matchingTimelineRows.Length == 0)
        {
            return new MainPagePreviewSelectionState(
                currentTimelineSelection,
                currentResultSelection,
                new PreviewSelectionRequest(nextAnalysis.Frame.FrameIndex, nextAnalysis.Frame.TimestampMs, null, null));
        }

        var timelineSelection = matchingTimelineRows[0];
        var resultSelection = FindMatchingResultRow(timelineSelection, resultRows) ?? currentResultSelection;
        var previewRequest = matchingTimelineRows.Length > 1
            ? new PreviewSelectionRequest(
                timelineSelection.FrameIndex,
                timelineSelection.TimestampMs,
                timelineSelection.SegmentId,
                timelineSelection.Text,
                DetectionIds: NormalizeDetectionIds(matchingTimelineRows.SelectMany(row => row.DetectionIds)))
            : PreviewSelectionRequest.FromTimelineSegment(timelineSelection);

        return new MainPagePreviewSelectionState(timelineSelection, resultSelection, previewRequest);
    }

    public static TimelineSegment? FindTimelineSelection(
        IReadOnlyList<TimelineSegment> timelineSegments,
        string? preferredSegmentId,
        string? preferredDetectionId)
    {
        return timelineSegments.FirstOrDefault(row => SelectionKeysMatch(
                row.FrameIndex,
                row.TimestampMs,
                row.SegmentId,
                row.DetectionId,
                null,
                null,
                preferredSegmentId,
                preferredDetectionId))
            ?? timelineSegments.FirstOrDefault();
    }

    public static FrameAnalysisResult? ResolvePreviewAnalysis(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        PreviewSelectionRequest? request)
    {
        if (frameAnalyses.Count == 0 || request is null)
        {
            return null;
        }

        if (request.DetectionIds?.Count > 0)
        {
            var detectionFrame = frameAnalyses.FirstOrDefault(analysis =>
                analysis.Ocr.Detections.Any(detection => request.DetectionIds.Contains(detection.DetectionId)));
            if (detectionFrame is not null)
            {
                return detectionFrame;
            }
        }

        if (request.FrameIndex is not null)
        {
            var exactFrame = frameAnalyses.FirstOrDefault(analysis => analysis.Frame.FrameIndex == request.FrameIndex);
            if (exactFrame is not null)
            {
                return exactFrame;
            }
        }

        if (request.TimestampMs is not null)
        {
            return frameAnalyses
                .OrderBy(analysis => Math.Abs(analysis.Frame.TimestampMs - request.TimestampMs.Value))
                .FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedText))
        {
            var matchingText = frameAnalyses.FirstOrDefault(analysis =>
                analysis.Ocr.Detections.Any(detection => TextsMatch(detection.Text, request.SelectedText)));
            if (matchingText is not null)
            {
                return matchingText;
            }
        }

        return frameAnalyses.FirstOrDefault();
    }

    public static int ResolvePreviewSequenceIndex(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        FrameAnalysisResult analysis)
    {
        return frameAnalyses
            .Select((item, itemIndex) => new { item, itemIndex })
            .FirstOrDefault(item => string.Equals(item.item.Frame.ImagePath, analysis.Frame.ImagePath, StringComparison.OrdinalIgnoreCase))
            ?.itemIndex ?? 0;
    }

    public static bool SelectionKeysMatch(
        int? leftFrameIndex,
        long? leftTimestampMs,
        string? leftSegmentId,
        string? leftDetectionId,
        int? rightFrameIndex,
        long? rightTimestampMs,
        string? rightSegmentId,
        string? rightDetectionId)
    {
        if (!string.IsNullOrWhiteSpace(leftSegmentId) || !string.IsNullOrWhiteSpace(rightSegmentId))
        {
            return string.Equals(leftSegmentId, rightSegmentId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(leftDetectionId) || !string.IsNullOrWhiteSpace(rightDetectionId))
        {
            return string.Equals(leftDetectionId, rightDetectionId, StringComparison.Ordinal);
        }

        return leftFrameIndex == rightFrameIndex && leftTimestampMs == rightTimestampMs;
    }

    private static ResultRow? FindMatchingResultRow(TimelineSegment selection, IReadOnlyList<ResultRow> resultRows)
    {
        return resultRows.FirstOrDefault(row => SelectionKeysMatch(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.DetectionId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId,
            row.DetectionId));
    }

    private static TimelineSegment? FindMatchingTimelineSegment(ResultRow selection, IReadOnlyList<TimelineSegment> timelineSegments)
    {
        return timelineSegments.FirstOrDefault(row => SelectionKeysMatch(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.DetectionId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId,
            row.DetectionId));
    }

    private static IReadOnlyList<string> NormalizeDetectionIds(IEnumerable<string> detectionIds)
    {
        return detectionIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TextsMatch(string? left, string? right)
    {
        return string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.Ordinal);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
    }
}
