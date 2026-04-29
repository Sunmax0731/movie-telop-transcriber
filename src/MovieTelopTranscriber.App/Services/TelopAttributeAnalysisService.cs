using MovieTelopTranscriber.App.Models;
using OpenCvSharp;
using System.Globalization;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopAttributeAnalysisService
{
    private const string MinTextSizeEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_MIN_TEXT_SIZE";

    private sealed record TelopStyleEstimate(
        string? TextColor,
        string? StrokeColor,
        string? BackgroundColor,
        string TextType);

    private sealed record ColorStats(
        int Total,
        int White,
        int Black,
        int Green,
        int Red,
        int Blue,
        int Yellow);

    public AttributeAnalysisResult Analyze(OcrWorkerResponse ocrResponse, string frameImagePath)
    {
        if (ocrResponse.Status == "error")
        {
            return new AttributeAnalysisResult(
                ocrResponse.FrameIndex,
                ocrResponse.TimestampMs,
                "error",
                Array.Empty<TelopAttributeRecord>(),
                ocrResponse.Error);
        }

        using var frameImage = File.Exists(frameImagePath) ? Cv2.ImRead(frameImagePath, ImreadModes.Color) : new Mat();
        var minimumTextSize = ResolveMinimumTextSize();
        var detections = ocrResponse.Detections
            .Where(detection => !string.IsNullOrWhiteSpace(detection.Text))
            .Select(detection =>
            {
                var fontSize = EstimateFontSize(detection.BoundingBox);
                if (minimumTextSize > 0d && (!fontSize.HasValue || fontSize.Value < minimumTextSize))
                {
                    return null;
                }

                var style = EstimateStyle(frameImage, detection.BoundingBox);
                return new TelopAttributeRecord(
                    detection.DetectionId,
                    detection.Text,
                    detection.Confidence,
                    null,
                    fontSize,
                    "px",
                    style.TextColor,
                    style.StrokeColor,
                    style.BackgroundColor,
                    style.TextType);
            })
            .Where(detection => detection is not null)
            .Select(detection => detection!)
            .ToArray();

        return new AttributeAnalysisResult(
            ocrResponse.FrameIndex,
            ocrResponse.TimestampMs,
            "success",
            detections,
            null);
    }

    private static double? EstimateFontSize(IReadOnlyList<OcrBoundingPoint> boundingBox)
    {
        if (boundingBox.Count == 0)
        {
            return null;
        }

        var minY = boundingBox.Min(point => point.Y);
        var maxY = boundingBox.Max(point => point.Y);
        var height = Math.Abs(maxY - minY);
        return height > 0 ? Math.Round(height, 1) : null;
    }

    private static double ResolveMinimumTextSize()
    {
        var value = Environment.GetEnvironmentVariable(MinTextSizeEnvironmentVariable);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0d
            ? parsed
            : 0d;
    }

    private static TelopStyleEstimate EstimateStyle(Mat frameImage, IReadOnlyList<OcrBoundingPoint> boundingBox)
    {
        if (frameImage.Empty() || boundingBox.Count == 0)
        {
            return UnknownStyle();
        }

        var rect = CreateClampedRect(frameImage, boundingBox, padding: 4);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return UnknownStyle();
        }

        using var crop = new Mat(frameImage, rect);
        var stats = AnalyzeColors(crop);
        var textColor = SelectTextColor(stats);
        var strokeColor = SelectStrokeColor(stats, textColor);
        var textType = CreateTextTypeLabel(textColor, strokeColor);
        return new TelopStyleEstimate(textColor, strokeColor, null, textType);
    }

    private static Rect CreateClampedRect(Mat image, IReadOnlyList<OcrBoundingPoint> boundingBox, int padding)
    {
        var minX = (int)Math.Floor(boundingBox.Min(point => point.X)) - padding;
        var minY = (int)Math.Floor(boundingBox.Min(point => point.Y)) - padding;
        var maxX = (int)Math.Ceiling(boundingBox.Max(point => point.X)) + padding;
        var maxY = (int)Math.Ceiling(boundingBox.Max(point => point.Y)) + padding;

        minX = Math.Clamp(minX, 0, Math.Max(0, image.Width - 1));
        minY = Math.Clamp(minY, 0, Math.Max(0, image.Height - 1));
        maxX = Math.Clamp(maxX, minX + 1, image.Width);
        maxY = Math.Clamp(maxY, minY + 1, image.Height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static ColorStats AnalyzeColors(Mat crop)
    {
        var total = Math.Max(1, crop.Width * crop.Height);
        var white = 0;
        var black = 0;
        var green = 0;
        var red = 0;
        var blue = 0;
        var yellow = 0;

        for (var y = 0; y < crop.Height; y++)
        {
            for (var x = 0; x < crop.Width; x++)
            {
                var pixel = crop.At<Vec3b>(y, x);
                var b = pixel.Item0;
                var g = pixel.Item1;
                var r = pixel.Item2;
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var delta = max - min;

                if (max <= 80)
                {
                    black++;
                }
                else if (min >= 180 && delta <= 70)
                {
                    white++;
                }
                else if (max >= 110 && delta >= 45)
                {
                    if (g >= r + 20 && g >= b + 20)
                    {
                        green++;
                    }
                    else if (r >= 170 && g >= 130 && b <= 130)
                    {
                        yellow++;
                    }
                    else if (r >= g + 30 && r >= b + 30)
                    {
                        red++;
                    }
                    else if (b >= r + 30 && b >= g + 20)
                    {
                        blue++;
                    }
                }
            }
        }

        return new ColorStats(total, white, black, green, red, blue, yellow);
    }

    private static string? SelectTextColor(ColorStats stats)
    {
        var minimum = Math.Max(8, (int)Math.Round(stats.Total * 0.03d));
        var colored = new[]
        {
            ("緑文字", stats.Green),
            ("黄文字", stats.Yellow),
            ("赤文字", stats.Red),
            ("青文字", stats.Blue)
        }
            .OrderByDescending(item => item.Item2)
            .First();

        if (colored.Item2 >= minimum && colored.Item2 >= stats.White * 0.75d)
        {
            return colored.Item1;
        }

        if (stats.White >= minimum && stats.White >= stats.Black * 0.35d)
        {
            return "白文字";
        }

        if (colored.Item2 >= minimum)
        {
            return colored.Item1;
        }

        return stats.Black >= minimum ? "黒文字" : null;
    }

    private static string? SelectStrokeColor(ColorStats stats, string? textColor)
    {
        var minimum = Math.Max(10, (int)Math.Round(stats.Total * 0.08d));
        if (textColor != "黒文字" && stats.Black >= minimum)
        {
            return "黒枠";
        }

        if (textColor != "白文字" && stats.White >= minimum)
        {
            return "白枠";
        }

        return null;
    }

    private static string CreateTextTypeLabel(string? textColor, string? strokeColor)
    {
        var parts = new[] { textColor, strokeColor }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return parts.Length == 0 ? "未分類" : string.Join(" / ", parts);
    }

    private static TelopStyleEstimate UnknownStyle()
    {
        return new TelopStyleEstimate(null, null, null, "未分類");
    }
}
