using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MovieTelopTranscriber.App.Models;

public sealed class TimelineSegment : ObservableObject
{
    private string _text;
    private bool _isEditing;

    public TimelineSegment(
        string rangeLabel,
        string text,
        string styleSummary,
        string category = "-",
        string detail = "-",
        double? confidence = null,
        int? frameIndex = null,
        long? timestampMs = null,
        string? segmentId = null,
        string? detectionId = null)
    {
        RangeLabel = rangeLabel;
        _text = text;
        StyleSummary = styleSummary;
        Category = category;
        Detail = detail;
        Confidence = confidence;
        FrameIndex = frameIndex;
        TimestampMs = timestampMs;
        SegmentId = segmentId;
        DetectionId = detectionId;
    }

    public string RangeLabel { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string StyleSummary { get; }

    public string Category { get; }

    public string Detail { get; }

    public string DisplayAttributeLabel =>
        string.IsNullOrWhiteSpace(Detail) || Detail == "-"
            ? Category
            : Detail;

    public double? Confidence { get; }

    public int? FrameIndex { get; }

    public long? TimestampMs { get; }

    public string? SegmentId { get; }

    public string? DetectionId { get; }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(TextDisplayVisibility));
                OnPropertyChanged(nameof(TextEditVisibility));
            }
        }
    }

    public bool CanEdit => !string.IsNullOrWhiteSpace(SegmentId) || !string.IsNullOrWhiteSpace(DetectionId);

    public Visibility TextDisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TextEditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    public double ConfidencePercent => Confidence is null ? 0d : Math.Clamp(Confidence.Value * 100d, 0d, 100d);

    public double ConfidenceGaugeWidth
    {
        get
        {
            if (Confidence is null || Confidence.Value < 0.5d)
            {
                return 0d;
            }

            return Math.Clamp((Confidence.Value - 0.5d) / 0.5d * 100d, 0d, 100d);
        }
    }

    public Visibility ConfidenceGaugeVisibility =>
        Confidence is not null && Confidence.Value >= 0.5d ? Visibility.Visible : Visibility.Collapsed;

    public string ConfidenceLabel => Confidence is null ? "-" : $"{Confidence.Value:P0}";

    public string FrameLabel => FrameIndex is null ? "-" : $"{FrameIndex.Value:D6}";

    public SolidColorBrush ConfidenceBrush
    {
        get
        {
            if (Confidence is null || Confidence.Value < 0.5d)
            {
                return new SolidColorBrush(Colors.Transparent);
            }

            var normalized = Math.Clamp((Confidence.Value - 0.5d) / 0.5d, 0d, 1d);
            var red = (byte)Math.Round(220d * (1d - normalized));
            var green = (byte)Math.Round(180d * normalized);
            return new SolidColorBrush(ColorHelper.FromArgb(255, red, green, 40));
        }
    }
}
