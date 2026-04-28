using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopSegmentMerger
{
    private const double MergeSimilarityThreshold = 0.88d;

    public IReadOnlyList<SegmentRecord> Merge(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        double frameIntervalSeconds)
    {
        var defaultDurationMs = Math.Max(1L, (long)Math.Round(frameIntervalSeconds * 1000d));
        var maxGapMs = Math.Max(defaultDurationMs, (long)Math.Round(defaultDurationMs * 1.5d));
        var completed = new List<SegmentBuilder>();
        var active = new List<SegmentBuilder>();

        foreach (var analysis in frameAnalyses.OrderBy(frame => frame.Frame.TimestampMs))
        {
            CompleteStaleSegments(active, completed, analysis.Frame.TimestampMs, maxGapMs);

            foreach (var detection in analysis.Attributes.Detections)
            {
                var match = active
                    .Where(segment => segment.CanExtend(analysis.Frame.TimestampMs, maxGapMs, detection))
                    .OrderByDescending(segment => segment.GetTextSimilarity(detection.Text))
                    .ThenBy(segment => analysis.Frame.TimestampMs - segment.LastTimestampMs)
                    .FirstOrDefault();

                if (match is not null)
                {
                    match.Extend(analysis.Frame.TimestampMs, defaultDurationMs, detection);
                    continue;
                }

                active.Add(new SegmentBuilder(
                    analysis.Frame.TimestampMs,
                    defaultDurationMs,
                    detection));
            }
        }

        completed.AddRange(active);

        return completed
            .OrderBy(segment => segment.StartTimestampMs)
            .Select((segment, index) => segment.ToSegmentRecord($"seg-{index + 1:D4}"))
            .ToArray();
    }

    private static void CompleteStaleSegments(
        List<SegmentBuilder> active,
        List<SegmentBuilder> completed,
        long timestampMs,
        long maxGapMs)
    {
        for (var index = active.Count - 1; index >= 0; index--)
        {
            if (timestampMs - active[index].LastTimestampMs > maxGapMs)
            {
                completed.Add(active[index]);
                active.RemoveAt(index);
            }
        }
    }

    private static double CalculateTextSimilarity(string left, string right)
    {
        var normalizedLeft = NormalizeForComparison(left);
        var normalizedRight = NormalizeForComparison(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return 0d;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1d;
        }

        if (Math.Min(normalizedLeft.Length, normalizedRight.Length) <= 3)
        {
            return 0d;
        }

        var distance = CalculateLevenshteinDistance(normalizedLeft, normalizedRight);
        return 1d - ((double)distance / Math.Max(normalizedLeft.Length, normalizedRight.Length));
    }

    private static string NormalizeForComparison(string text)
    {
        return string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var cost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static bool OptionalValueCompatible(string? left, string? right)
    {
        return string.IsNullOrWhiteSpace(left)
            || string.IsNullOrWhiteSpace(right)
            || string.Equals(left, right, StringComparison.Ordinal);
    }

    private sealed class SegmentBuilder
    {
        private readonly List<double> _confidences = new();
        private readonly List<double> _fontSizes = new();
        private readonly Dictionary<string, TextObservation> _textObservations = new(StringComparer.Ordinal);
        private int _observationOrder;

        public SegmentBuilder(long timestampMs, long defaultDurationMs, TelopAttributeRecord detection)
        {
            StartTimestampMs = timestampMs;
            EndTimestampMs = timestampMs + defaultDurationMs;
            LastTimestampMs = timestampMs;
            TextType = detection.TextType;
            FontFamily = detection.FontFamily;
            FontSizeUnit = detection.FontSizeUnit;
            TextColor = detection.TextColor;
            StrokeColor = detection.StrokeColor;
            BackgroundColor = detection.BackgroundColor;
            AddTextObservation(detection.Text, detection.Confidence);
            AddConfidence(detection.Confidence);
            AddFontSize(detection.FontSize);
            SourceFrameCount = 1;
        }

        public long StartTimestampMs { get; }

        public long EndTimestampMs { get; private set; }

        public long LastTimestampMs { get; private set; }

        private string TextType { get; }

        private string? FontFamily { get; }

        private string? FontSizeUnit { get; }

        private string? TextColor { get; }

        private string? StrokeColor { get; }

        private string? BackgroundColor { get; }

        private int SourceFrameCount { get; set; }

        public bool CanExtend(long timestampMs, long maxGapMs, TelopAttributeRecord detection)
        {
            return timestampMs > LastTimestampMs
                && timestampMs - LastTimestampMs <= maxGapMs
                && string.Equals(TextType, detection.TextType, StringComparison.Ordinal)
                && OptionalValueCompatible(FontFamily, detection.FontFamily)
                && OptionalValueCompatible(TextColor, detection.TextColor)
                && OptionalValueCompatible(StrokeColor, detection.StrokeColor)
                && OptionalValueCompatible(BackgroundColor, detection.BackgroundColor)
                && GetTextSimilarity(detection.Text) >= MergeSimilarityThreshold;
        }

        public double GetTextSimilarity(string text)
        {
            return _textObservations.Values
                .Select(observation => CalculateTextSimilarity(observation.Text, text))
                .DefaultIfEmpty(0d)
                .Max();
        }

        public void Extend(long timestampMs, long defaultDurationMs, TelopAttributeRecord detection)
        {
            LastTimestampMs = timestampMs;
            EndTimestampMs = timestampMs + defaultDurationMs;
            SourceFrameCount++;
            AddTextObservation(detection.Text, detection.Confidence);
            AddConfidence(detection.Confidence);
            AddFontSize(detection.FontSize);
        }

        public SegmentRecord ToSegmentRecord(string segmentId)
        {
            var confidence = _confidences.Count == 0 ? (double?)null : Math.Round(_confidences.Average(), 4);
            var fontSize = _fontSizes.Count == 0 ? (double?)null : Math.Round(_fontSizes.Average(), 1);
            return new SegmentRecord(
                segmentId,
                StartTimestampMs,
                EndTimestampMs,
                GetRepresentativeText(),
                TextType,
                FontFamily,
                fontSize,
                FontSizeUnit,
                TextColor,
                StrokeColor,
                BackgroundColor,
                confidence,
                SourceFrameCount);
        }

        private string GetRepresentativeText()
        {
            return _textObservations.Values
                .OrderByDescending(observation => observation.Score)
                .ThenByDescending(observation => observation.BestConfidence)
                .ThenBy(observation => observation.FirstOrder)
                .First()
                .Text;
        }

        private void AddTextObservation(string text, double? confidence)
        {
            if (!_textObservations.TryGetValue(text, out var observation))
            {
                observation = new TextObservation(text, _observationOrder++);
                _textObservations[text] = observation;
            }

            observation.Add(confidence);
        }

        private void AddConfidence(double? confidence)
        {
            if (confidence is not null)
            {
                _confidences.Add(confidence.Value);
            }
        }

        private void AddFontSize(double? fontSize)
        {
            if (fontSize is not null)
            {
                _fontSizes.Add(fontSize.Value);
            }
        }
    }

    private sealed class TextObservation(string text, int firstOrder)
    {
        public string Text { get; } = text;

        public int FirstOrder { get; } = firstOrder;

        public double Score { get; private set; }

        public double BestConfidence { get; private set; }

        public void Add(double? confidence)
        {
            var score = confidence is null ? 0.5d : Math.Clamp(confidence.Value, 0d, 1d);
            Score += score;
            BestConfidence = Math.Max(BestConfidence, score);
        }
    }
}
