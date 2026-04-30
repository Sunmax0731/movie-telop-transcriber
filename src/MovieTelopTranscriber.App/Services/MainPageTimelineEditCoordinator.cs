using System.Globalization;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public static class MainPageTimelineEditCoordinator
{
    public static MainPageTimelineEditOutcome UpdateText(
        MainPageTimelineEditState state,
        TimelineSegment segment,
        string newText)
    {
        var trimmedText = newText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            return new MainPageTimelineEditOutcome(state, false, "Telop text was not changed because it was empty.", null, null);
        }

        var originalText = ResolveCurrentText(state, segment) ?? segment.Text;
        if (string.Equals(originalText, trimmedText, StringComparison.Ordinal))
        {
            return new MainPageTimelineEditOutcome(state, false, "Telop text was not changed.", null, null);
        }

        var updatedState = state with
        {
            LatestSegments = string.IsNullOrWhiteSpace(segment.SegmentId)
                ? state.LatestSegments
                : state.LatestSegments
                    .Select(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal)
                        ? item with { Text = trimmedText }
                        : item)
                    .ToArray(),
            LatestFrameAnalyses = string.IsNullOrWhiteSpace(segment.DetectionId)
                ? state.LatestFrameAnalyses
                : state.LatestFrameAnalyses
                    .Select(analysis => ReplaceDetectionText(analysis, segment.DetectionId, trimmedText))
                    .ToArray(),
            SegmentDetectionIds = PreserveSegmentDetectionIds(state, segment),
            TimelineEdits = AddEditRecord(state, "edit", segment, null, originalText, trimmedText, "timeline text edit")
        };

        return new MainPageTimelineEditOutcome(
            updatedState,
            true,
            "Edited telop text. Use Export only to write the updated output files.",
            segment.SegmentId,
            segment.DetectionId);
    }

    public static MainPageTimelineEditOutcome Delete(
        MainPageTimelineEditState state,
        TimelineSegment segment)
    {
        var updatedSegments = string.IsNullOrWhiteSpace(segment.SegmentId)
            ? state.LatestSegments
            : state.LatestSegments
                .Where(item => !string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal))
                .ToArray();
        var updatedAnalyses = string.IsNullOrWhiteSpace(segment.DetectionId)
            ? state.LatestFrameAnalyses
            : state.LatestFrameAnalyses
                .Select(analysis => RemoveDetection(analysis, segment.DetectionId))
                .ToArray();
        var updatedDetectionIds = CloneDetectionIds(state.SegmentDetectionIds);
        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            updatedDetectionIds.Remove(segment.SegmentId);
        }

        return new MainPageTimelineEditOutcome(
            state with
            {
                LatestSegments = updatedSegments,
                LatestFrameAnalyses = updatedAnalyses,
                SegmentDetectionIds = updatedDetectionIds,
                TimelineEdits = AddEditRecord(state, "delete", segment, null, segment.Text, null, "timeline row delete")
            },
            true,
            "Deleted selected telop. Use Export only to write the updated output files.",
            null,
            null);
    }

    public static bool CanMergeTimelineSegments(
        TimelineSegment first,
        TimelineSegment second,
        out string statusMessage)
    {
        if (IsLikelyTimecodeText(first.Text) || IsLikelyTimecodeText(second.Text))
        {
            statusMessage = "Timecode-like rows cannot be merged with telop text rows.";
            return false;
        }

        if (!string.Equals(first.Category, second.Category, StringComparison.Ordinal)
            || !string.Equals(first.DisplayAttributeLabel, second.DisplayAttributeLabel, StringComparison.Ordinal))
        {
            statusMessage = "Merge requires the selected row and next row to have the same display attributes.";
            return false;
        }

        statusMessage = string.Empty;
        return true;
    }

    public static MainPageTimelineEditOutcome Merge(
        MainPageTimelineEditState state,
        TimelineSegment first,
        TimelineSegment second)
    {
        var mergedText = NormalizeMergedText(first.Text, second.Text);
        var updatedState = state;
        var updatedDetectionIds = CloneDetectionIds(state.SegmentDetectionIds);

        if (!string.IsNullOrWhiteSpace(first.SegmentId) && !string.IsNullOrWhiteSpace(second.SegmentId))
        {
            var firstSegment = state.LatestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, first.SegmentId, StringComparison.Ordinal));
            var secondSegment = state.LatestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, second.SegmentId, StringComparison.Ordinal));
            if (firstSegment is null || secondSegment is null)
            {
                return new MainPageTimelineEditOutcome(state, false, "Could not find selected segments to merge.", null, null);
            }

            var mergedSegment = firstSegment with
            {
                EndTimestampMs = Math.Max(firstSegment.EndTimestampMs, secondSegment.EndTimestampMs),
                Text = mergedText,
                TextType = string.Equals(firstSegment.TextType, secondSegment.TextType, StringComparison.Ordinal)
                    ? firstSegment.TextType
                    : "edited",
                Confidence = AverageConfidence(firstSegment.Confidence, secondSegment.Confidence),
                SourceFrameCount = firstSegment.SourceFrameCount + secondSegment.SourceFrameCount
            };
            updatedState = updatedState with
            {
                LatestSegments = state.LatestSegments
                    .Select(item => string.Equals(item.SegmentId, firstSegment.SegmentId, StringComparison.Ordinal) ? mergedSegment : item)
                    .Where(item => !string.Equals(item.SegmentId, secondSegment.SegmentId, StringComparison.Ordinal))
                    .ToArray()
            };
            updatedDetectionIds[firstSegment.SegmentId] = MergeDetectionIds(state, first, second);
            updatedDetectionIds.Remove(secondSegment.SegmentId);
        }

        if (string.IsNullOrWhiteSpace(first.SegmentId) && !string.IsNullOrWhiteSpace(first.DetectionId))
        {
            updatedState = updatedState with
            {
                LatestFrameAnalyses = updatedState.LatestFrameAnalyses
                    .Select(analysis => ReplaceDetectionText(analysis, first.DetectionId, mergedText))
                    .ToArray()
            };
        }

        if (string.IsNullOrWhiteSpace(second.SegmentId) && !string.IsNullOrWhiteSpace(second.DetectionId))
        {
            updatedState = updatedState with
            {
                LatestFrameAnalyses = updatedState.LatestFrameAnalyses
                    .Select(analysis => RemoveDetection(analysis, second.DetectionId))
                    .ToArray()
            };
        }

        updatedState = updatedState with
        {
            SegmentDetectionIds = updatedDetectionIds,
            TimelineEdits = AddEditRecord(state, "merge", first, second, $"{first.Text.Trim()} | {second.Text.Trim()}", mergedText, "merged selected row with next row")
        };

        return new MainPageTimelineEditOutcome(
            updatedState,
            true,
            "Merged selected telop with the next row. Use Export only to write the updated output files.",
            first.SegmentId,
            first.DetectionId);
    }

    public static MainPageTimelineEditOutcome Split(
        MainPageTimelineEditState state,
        TimelineSegment segment,
        string firstText,
        string secondText)
    {
        var updatedState = state;
        var updatedDetectionIds = CloneDetectionIds(state.SegmentDetectionIds);
        var manualEditSequence = state.ManualEditSequence;

        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            var sourceSegment = state.LatestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal));
            if (sourceSegment is null)
            {
                return new MainPageTimelineEditOutcome(state, false, "Could not find selected segment to split.", null, null);
            }

            var midpointMs = sourceSegment.StartTimestampMs + ((sourceSegment.EndTimestampMs - sourceSegment.StartTimestampMs) / 2);
            var secondSegmentId = CreateManualId(sourceSegment.SegmentId, "split", ref manualEditSequence);
            var firstSegment = sourceSegment with
            {
                EndTimestampMs = midpointMs,
                Text = firstText,
                SourceFrameCount = Math.Max(1, sourceSegment.SourceFrameCount / 2)
            };
            var secondSegment = sourceSegment with
            {
                SegmentId = secondSegmentId,
                StartTimestampMs = midpointMs,
                Text = secondText,
                SourceFrameCount = Math.Max(1, sourceSegment.SourceFrameCount - firstSegment.SourceFrameCount)
            };

            var updatedSegments = new List<SegmentRecord>();
            foreach (var item in state.LatestSegments)
            {
                if (string.Equals(item.SegmentId, sourceSegment.SegmentId, StringComparison.Ordinal))
                {
                    updatedSegments.Add(firstSegment);
                    updatedSegments.Add(secondSegment);
                }
                else
                {
                    updatedSegments.Add(item);
                }
            }

            updatedState = updatedState with { LatestSegments = updatedSegments };
            var splitDetectionIds = SplitSegmentDetectionIds(state, segment, firstText, secondText);
            updatedDetectionIds[sourceSegment.SegmentId] = splitDetectionIds.FirstIds;
            updatedDetectionIds[secondSegmentId] = splitDetectionIds.SecondIds;
        }

        if (string.IsNullOrWhiteSpace(segment.SegmentId) && !string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            var secondDetectionId = CreateManualId(segment.DetectionId, "split", ref manualEditSequence);
            updatedState = updatedState with
            {
                LatestFrameAnalyses = updatedState.LatestFrameAnalyses
                    .Select(analysis => SplitDetection(analysis, segment.DetectionId, secondDetectionId, firstText, secondText))
                    .ToArray()
            };
        }

        updatedState = updatedState with
        {
            SegmentDetectionIds = updatedDetectionIds,
            ManualEditSequence = manualEditSequence,
            TimelineEdits = AddEditRecord(state, "split", segment, null, segment.Text, $"{firstText} | {secondText}", "split selected row into two rows")
        };

        return new MainPageTimelineEditOutcome(
            updatedState,
            true,
            "Split selected telop. Use Export only to write the updated output files.",
            segment.SegmentId,
            segment.DetectionId);
    }

    public static string? ResolveCurrentText(MainPageTimelineEditState state, TimelineSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            var sourceSegment = state.LatestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal));
            if (sourceSegment is not null)
            {
                return sourceSegment.Text;
            }
        }

        if (!string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            return state.LatestFrameAnalyses
                .SelectMany(analysis => analysis.Ocr.Detections)
                .FirstOrDefault(detection => string.Equals(detection.DetectionId, segment.DetectionId, StringComparison.Ordinal))
                ?.Text;
        }

        return null;
    }

    public static bool TrySplitText(string text, out string firstText, out string secondText)
    {
        var trimmed = text.Trim();
        var lines = trimmed
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (lines.Length >= 2)
        {
            firstText = lines[0];
            secondText = string.Join(" ", lines.Skip(1));
            return true;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length >= 2)
        {
            var splitIndex = Math.Max(1, words.Length / 2);
            firstText = string.Join(' ', words.Take(splitIndex));
            secondText = string.Join(' ', words.Skip(splitIndex));
            return !string.IsNullOrWhiteSpace(firstText) && !string.IsNullOrWhiteSpace(secondText);
        }

        firstText = string.Empty;
        secondText = string.Empty;
        return false;
    }

    public static bool IsLikelyTimecodeText(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length < 2 || !normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return double.TryParse(
            normalized[..^1],
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out _);
    }

    private static IReadOnlyList<EditOperationRecord> AddEditRecord(
        MainPageTimelineEditState state,
        string operation,
        TimelineSegment target,
        TimelineSegment? related,
        string? originalText,
        string? updatedText,
        string notes)
    {
        return state.TimelineEdits
            .Append(new EditOperationRecord(
                operation,
                target.SegmentId ?? target.DetectionId ?? target.RangeLabel,
                related?.SegmentId ?? related?.DetectionId,
                target.DetectionId,
                originalText,
                updatedText,
                target.TimestampMs,
                null,
                DateTimeOffset.Now,
                notes))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> PreserveSegmentDetectionIds(
        MainPageTimelineEditState state,
        TimelineSegment segment)
    {
        var updated = CloneDetectionIds(state.SegmentDetectionIds);
        if (string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            return updated;
        }

        var detectionIds = ResolveTimelineSegmentDetectionIds(state, segment);
        if (detectionIds.Count > 0)
        {
            updated[segment.SegmentId] = detectionIds;
        }

        return updated;
    }

    private static IReadOnlyList<string> MergeDetectionIds(
        MainPageTimelineEditState state,
        TimelineSegment first,
        TimelineSegment second)
    {
        return NormalizeDetectionIds(ResolveTimelineSegmentDetectionIds(state, first).Concat(ResolveTimelineSegmentDetectionIds(state, second)));
    }

    private static (IReadOnlyList<string> FirstIds, IReadOnlyList<string> SecondIds) SplitSegmentDetectionIds(
        MainPageTimelineEditState state,
        TimelineSegment segment,
        string firstText,
        string secondText)
    {
        var sourceIds = ResolveTimelineSegmentDetectionIds(state, segment);
        if (sourceIds.Count == 0)
        {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var firstIds = new List<string>();
        var secondIds = new List<string>();
        foreach (var detectionId in sourceIds)
        {
            var detectionText = ResolveDetectionText(state, detectionId);
            if (detectionText is null)
            {
                firstIds.Add(detectionId);
                secondIds.Add(detectionId);
                continue;
            }

            var belongsToFirst = DetectionTextIsRelatedToSelectedSegment(detectionText, firstText);
            var belongsToSecond = DetectionTextIsRelatedToSelectedSegment(detectionText, secondText);
            if (belongsToFirst)
            {
                firstIds.Add(detectionId);
            }

            if (belongsToSecond)
            {
                secondIds.Add(detectionId);
            }
        }

        return (
            firstIds.Count == 0 ? sourceIds : NormalizeDetectionIds(firstIds),
            secondIds.Count == 0 ? sourceIds : NormalizeDetectionIds(secondIds));
    }

    private static IReadOnlyList<string> ResolveTimelineSegmentDetectionIds(
        MainPageTimelineEditState state,
        TimelineSegment segment)
    {
        var detectionIds = NormalizeDetectionIds(segment.DetectionIds);
        if (detectionIds.Count > 0)
        {
            return detectionIds;
        }

        if (!string.IsNullOrWhiteSpace(segment.SegmentId)
            && state.SegmentDetectionIds.TryGetValue(segment.SegmentId, out var mappedIds)
            && mappedIds.Count > 0)
        {
            return mappedIds;
        }

        return string.IsNullOrWhiteSpace(segment.DetectionId)
            ? Array.Empty<string>()
            : [segment.DetectionId];
    }

    private static string? ResolveDetectionText(MainPageTimelineEditState state, string detectionId)
    {
        return state.LatestFrameAnalyses
            .SelectMany(analysis => analysis.Ocr.Detections)
            .FirstOrDefault(detection => string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal))
            ?.Text;
    }

    private static IReadOnlyList<string> NormalizeDetectionIds(IEnumerable<string> detectionIds)
    {
        return detectionIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeMergedText(string first, string second)
    {
        return string.Join(
            " ",
            new[] { first.Trim(), second.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static double? AverageConfidence(double? first, double? second)
    {
        return (first, second) switch
        {
            ({ } left, { } right) => (left + right) / 2d,
            ({ } left, null) => left,
            (null, { } right) => right,
            _ => null
        };
    }

    private static string CreateManualId(string baseId, string operation, ref int manualEditSequence)
    {
        manualEditSequence++;
        return $"{baseId}-{operation}-{manualEditSequence:D3}";
    }

    private static FrameAnalysisResult ReplaceDetectionText(
        FrameAnalysisResult analysis,
        string detectionId,
        string newText)
    {
        var ocrDetections = analysis.Ocr.Detections
            .Select(detection => string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal)
                ? detection with { Text = newText }
                : detection)
            .ToArray();
        var attributeDetections = analysis.Attributes.Detections
            .Select(detection => string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal)
                ? detection with { Text = newText }
                : detection)
            .ToArray();

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
    }

    private static FrameAnalysisResult SplitDetection(
        FrameAnalysisResult analysis,
        string detectionId,
        string secondDetectionId,
        string firstText,
        string secondText)
    {
        var ocrDetections = SplitDetectionRecords(
            analysis.Ocr.Detections,
            detectionId,
            secondDetectionId,
            detection => detection with { Text = firstText },
            detection => detection with { DetectionId = secondDetectionId, Text = secondText });
        var attributeDetections = SplitDetectionRecords(
            analysis.Attributes.Detections,
            detectionId,
            secondDetectionId,
            detection => detection with { Text = firstText },
            detection => detection with { DetectionId = secondDetectionId, Text = secondText });

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
    }

    private static IReadOnlyList<TDetection> SplitDetectionRecords<TDetection>(
        IReadOnlyList<TDetection> detections,
        string detectionId,
        string secondDetectionId,
        Func<TDetection, TDetection> createFirst,
        Func<TDetection, TDetection> createSecond)
        where TDetection : notnull
    {
        var updated = new List<TDetection>();
        foreach (var detection in detections)
        {
            var id = detection switch
            {
                OcrDetectionRecord ocr => ocr.DetectionId,
                TelopAttributeRecord attribute => attribute.DetectionId,
                _ => string.Empty
            };

            if (string.Equals(id, detectionId, StringComparison.Ordinal))
            {
                updated.Add(createFirst(detection));
                updated.Add(createSecond(detection));
            }
            else
            {
                updated.Add(detection);
            }
        }

        return updated;
    }

    private static FrameAnalysisResult RemoveDetection(FrameAnalysisResult analysis, string detectionId)
    {
        var ocrDetections = analysis.Ocr.Detections
            .Where(detection => !string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal))
            .ToArray();
        var attributeDetections = analysis.Attributes.Detections
            .Where(detection => !string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal))
            .ToArray();

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
    }

    private static bool DetectionTextIsRelatedToSelectedSegment(string detectionText, string selectedText)
    {
        return string.Equals(NormalizeText(detectionText), NormalizeText(selectedText), StringComparison.Ordinal)
            || NormalizeText(selectedText).Contains(NormalizeText(detectionText), StringComparison.Ordinal)
            || NormalizeText(detectionText).Contains(NormalizeText(selectedText), StringComparison.Ordinal);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
    }

    private static Dictionary<string, IReadOnlyList<string>> CloneDetectionIds(
        IReadOnlyDictionary<string, IReadOnlyList<string>> source)
    {
        var clone = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            clone[pair.Key] = pair.Value.ToArray();
        }

        return clone;
    }
}
