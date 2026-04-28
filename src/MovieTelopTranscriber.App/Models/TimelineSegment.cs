using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace MovieTelopTranscriber.App.Models;

public sealed record TimelineSegment(
    string RangeLabel,
    string Text,
    string StyleSummary,
    string Category = "-",
    string Detail = "-",
    double? Confidence = null,
    int? FrameIndex = null,
    long? TimestampMs = null,
    string? SegmentId = null,
    string? DetectionId = null)
{
    public double ConfidencePercent => Confidence is null ? 0d : Math.Clamp(Confidence.Value * 100d, 0d, 100d);

    public string ConfidenceLabel => Confidence is null ? "-" : $"{Confidence.Value:P0}";

    public string FrameLabel => FrameIndex is null ? "-" : $"Frame {FrameIndex.Value:D6}";

    public SolidColorBrush ConfidenceBrush
    {
        get
        {
            if (Confidence is null)
            {
                return new SolidColorBrush(Colors.Gray);
            }

            var normalized = Math.Clamp(Confidence.Value, 0d, 1d);
            var red = (byte)Math.Round(220d * (1d - normalized));
            var green = (byte)Math.Round(170d * normalized);
            return new SolidColorBrush(ColorHelper.FromArgb(255, red, green, 40));
        }
    }
}
