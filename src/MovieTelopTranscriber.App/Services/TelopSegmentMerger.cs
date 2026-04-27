using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopSegmentMerger
{
    public IReadOnlyList<SegmentRecord> Merge(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        double frameIntervalSeconds)
    {
        var defaultDurationMs = Math.Max(1L, (long)Math.Round(frameIntervalSeconds * 1000d));
        var maxGapMs = Math.Max(defaultDurationMs, (long)Math.Round(defaultDurationMs * 1.5d));
        var completed = new List<SegmentBuilder>();
        var activeByKey = new Dictionary<string, SegmentBuilder>();

        foreach (var analysis in frameAnalyses.OrderBy(frame => frame.Frame.TimestampMs))
        {
            foreach (var detection in analysis.Attributes.Detections)
            {
                var key = CreateMergeKey(detection);
                if (activeByKey.TryGetValue(key, out var active)
                    && analysis.Frame.TimestampMs - active.LastTimestampMs <= maxGapMs)
                {
                    active.Extend(analysis.Frame.TimestampMs, defaultDurationMs, detection);
                    continue;
                }

                if (activeByKey.Remove(key, out var stale))
                {
                    completed.Add(stale);
                }

                activeByKey[key] = new SegmentBuilder(
                    analysis.Frame.TimestampMs,
                    defaultDurationMs,
                    detection);
            }
        }

        completed.AddRange(activeByKey.Values);

        return completed
            .OrderBy(segment => segment.StartTimestampMs)
            .Select((segment, index) => segment.ToSegmentRecord($"seg-{index + 1:D4}"))
            .ToArray();
    }

    private static string CreateMergeKey(TelopAttributeRecord detection)
    {
        return string.Join(
            "|",
            detection.Text,
            detection.TextType,
            detection.FontFamily ?? string.Empty,
            detection.TextColor ?? string.Empty,
            detection.StrokeColor ?? string.Empty,
            detection.BackgroundColor ?? string.Empty);
    }

    private sealed class SegmentBuilder
    {
        private readonly List<double> _confidences = new();

        public SegmentBuilder(long timestampMs, long defaultDurationMs, TelopAttributeRecord detection)
        {
            StartTimestampMs = timestampMs;
            EndTimestampMs = timestampMs + defaultDurationMs;
            LastTimestampMs = timestampMs;
            Text = detection.Text;
            TextType = detection.TextType;
            FontFamily = detection.FontFamily;
            FontSize = detection.FontSize;
            FontSizeUnit = detection.FontSizeUnit;
            TextColor = detection.TextColor;
            StrokeColor = detection.StrokeColor;
            BackgroundColor = detection.BackgroundColor;
            AddConfidence(detection.Confidence);
            SourceFrameCount = 1;
        }

        public long StartTimestampMs { get; }

        public long EndTimestampMs { get; private set; }

        public long LastTimestampMs { get; private set; }

        private string Text { get; }

        private string TextType { get; }

        private string? FontFamily { get; }

        private double? FontSize { get; }

        private string? FontSizeUnit { get; }

        private string? TextColor { get; }

        private string? StrokeColor { get; }

        private string? BackgroundColor { get; }

        private int SourceFrameCount { get; set; }

        public void Extend(long timestampMs, long defaultDurationMs, TelopAttributeRecord detection)
        {
            LastTimestampMs = timestampMs;
            EndTimestampMs = timestampMs + defaultDurationMs;
            SourceFrameCount++;
            AddConfidence(detection.Confidence);
        }

        public SegmentRecord ToSegmentRecord(string segmentId)
        {
            var confidence = _confidences.Count == 0 ? (double?)null : Math.Round(_confidences.Average(), 4);
            return new SegmentRecord(
                segmentId,
                StartTimestampMs,
                EndTimestampMs,
                Text,
                TextType,
                FontFamily,
                FontSize,
                FontSizeUnit,
                TextColor,
                StrokeColor,
                BackgroundColor,
                confidence,
                SourceFrameCount);
        }

        private void AddConfidence(double? confidence)
        {
            if (confidence is not null)
            {
                _confidences.Add(confidence.Value);
            }
        }
    }
}
