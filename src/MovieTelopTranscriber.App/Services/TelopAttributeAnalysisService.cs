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

        var baseRect = CreateClampedRect(frameImage, boundingBox, padding: 0);
        var outerRect = CreateClampedRect(frameImage, boundingBox, padding: 4);
        if (baseRect.Width <= 0 || baseRect.Height <= 0 || outerRect.Width <= 0 || outerRect.Height <= 0)
        {
            return UnknownStyle();
        }

        using var baseCrop = new Mat(frameImage, baseRect);
        using var outerCrop = new Mat(frameImage, outerRect);

        var baseStats = AnalyzeColors(baseCrop);
        var cornerStats = AnalyzeCornerColors(outerCrop);
        var foregroundStats = RemoveBackgroundInfluence(baseStats, cornerStats);
        var textColor = SelectTextColor(foregroundStats, baseStats);
        var strokeColor = SelectStrokeColor(baseStats, textColor);
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

    private static ColorStats AnalyzeCornerColors(Mat crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return new ColorStats(1, 0, 0, 0, 0, 0, 0);
        }

        var cornerWidth = Math.Max(1, crop.Width / 5);
        var cornerHeight = Math.Max(1, crop.Height / 5);
        var corners = new[]
        {
            new Rect(0, 0, cornerWidth, cornerHeight),
            new Rect(Math.Max(0, crop.Width - cornerWidth), 0, cornerWidth, cornerHeight),
            new Rect(0, Math.Max(0, crop.Height - cornerHeight), cornerWidth, cornerHeight),
            new Rect(Math.Max(0, crop.Width - cornerWidth), Math.Max(0, crop.Height - cornerHeight), cornerWidth, cornerHeight)
        };

        var total = 0;
        var white = 0;
        var black = 0;
        var green = 0;
        var red = 0;
        var blue = 0;
        var yellow = 0;

        foreach (var corner in corners)
        {
            using var roi = new Mat(crop, corner);
            var stats = AnalyzeColors(roi);
            total += stats.Total;
            white += stats.White;
            black += stats.Black;
            green += stats.Green;
            red += stats.Red;
            blue += stats.Blue;
            yellow += stats.Yellow;
        }

        return new ColorStats(Math.Max(1, total), white, black, green, red, blue, yellow);
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

    private static ColorStats RemoveBackgroundInfluence(ColorStats baseStats, ColorStats cornerStats)
    {
        if (cornerStats.Total <= 0)
        {
            return baseStats;
        }

        var scale = (double)baseStats.Total / cornerStats.Total;
        return new ColorStats(
            baseStats.Total,
            RemoveScaledCount(baseStats.White, cornerStats.White, scale),
            RemoveScaledCount(baseStats.Black, cornerStats.Black, scale),
            RemoveScaledCount(baseStats.Green, cornerStats.Green, scale),
            RemoveScaledCount(baseStats.Red, cornerStats.Red, scale),
            RemoveScaledCount(baseStats.Blue, cornerStats.Blue, scale),
            RemoveScaledCount(baseStats.Yellow, cornerStats.Yellow, scale));
    }

    private static int RemoveScaledCount(int totalCount, int sampledCount, double scale)
    {
        var estimatedBackground = (int)Math.Round(sampledCount * scale);
        return Math.Max(0, totalCount - estimatedBackground);
    }

    private static string? SelectTextColor(ColorStats foregroundStats, ColorStats baseStats)
    {
        var minimum = Math.Max(8, (int)Math.Round(baseStats.Total * 0.015d));
        var colored = new[]
        {
            ("緑文字", foregroundStats.Green),
            ("黄文字", foregroundStats.Yellow),
            ("赤文字", foregroundStats.Red),
            ("青文字", foregroundStats.Blue)
        }
            .OrderByDescending(item => item.Item2)
            .FirstOrDefault();

        if (foregroundStats.White >= minimum && foregroundStats.White >= colored.Item2 * 0.5d)
        {
            return "白文字";
        }

        if (colored.Item2 >= minimum)
        {
            return colored.Item1;
        }

        if (baseStats.White >= minimum)
        {
            return "白文字";
        }

        if (foregroundStats.Black >= minimum)
        {
            return "黒文字";
        }

        return null;
    }

    private static string? SelectStrokeColor(ColorStats stats, string? textColor)
    {
        var minimum = Math.Max(10, (int)Math.Round(stats.Total * 0.05d));
        if (textColor != "黒文字" && stats.Black >= minimum)
        {
            return "黒枠";
        }

        if (textColor != "白文字" && stats.White >= minimum)
        {
            return "白縁";
        }

        return null;
    }

    private static string CreateTextTypeLabel(string? textColor, string? strokeColor)
    {
        var parts = new[] { textColor, strokeColor }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return parts.Length == 0 ? "未推定" : string.Join(" / ", parts);
    }

    private static TelopStyleEstimate UnknownStyle()
    {
        return new TelopStyleEstimate(null, null, null, "未推定");
    }
}
