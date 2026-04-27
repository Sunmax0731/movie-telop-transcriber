namespace MovieTelopTranscriber.App.Models;

public sealed record TelopAttributeRecord(
    string DetectionId,
    string Text,
    double? Confidence,
    string? FontFamily,
    double? FontSize,
    string? FontSizeUnit,
    string? TextColor,
    string? StrokeColor,
    string? BackgroundColor,
    string TextType);
