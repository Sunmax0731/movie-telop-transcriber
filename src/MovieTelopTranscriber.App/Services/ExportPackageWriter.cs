using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class ExportPackageWriter
{
    public async Task<ExportWriteResult> WriteAsync(
        VideoMetadata sourceVideo,
        FrameExtractionResult frameExtractionResult,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<SegmentRecord> segments,
        double frameIntervalSeconds,
        string ocrEngine,
        long? processingTimeMs,
        int warningCount,
        int errorCount,
        CancellationToken cancellationToken = default)
    {
        var outputDirectory = Path.Combine(frameExtractionResult.RunDirectory, "output");
        Directory.CreateDirectory(outputDirectory);

        var package = new ExportPackage(
            "1.0.0",
            sourceVideo,
            new ProcessingSettingsRecord(frameIntervalSeconds, ocrEngine, true),
            frameAnalyses
                .Select(frame => new FrameExportRecord(
                    frame.Frame.FrameIndex,
                    frame.Frame.TimestampMs,
                    frame.Frame.ImagePath,
                    frame.Attributes.Detections))
                .ToArray(),
            segments,
            new RunMetadataRecord(
                DateTimeOffset.Now,
                GetApplicationVersion(),
                processingTimeMs,
                warningCount,
                errorCount));

        var jsonPath = Path.Combine(outputDirectory, "segments.json");
        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, package, OcrContractJson.Options, cancellationToken);
        }

        var segmentsCsvPath = Path.Combine(outputDirectory, "segments.csv");
        await File.WriteAllTextAsync(segmentsCsvPath, BuildSegmentsCsv(segments), new UTF8Encoding(false), cancellationToken);

        var framesCsvPath = Path.Combine(outputDirectory, "frames.csv");
        await File.WriteAllTextAsync(framesCsvPath, BuildFramesCsv(package.Frames), new UTF8Encoding(false), cancellationToken);

        return new ExportWriteResult(outputDirectory, jsonPath, segmentsCsvPath, framesCsvPath);
    }

    private static string BuildSegmentsCsv(IReadOnlyList<SegmentRecord> segments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("segment_id,start_timestamp_ms,end_timestamp_ms,text,text_type,font_family,font_size,text_color,stroke_color,background_color,confidence");

        foreach (var segment in segments)
        {
            builder.AppendLine(string.Join(
                ',',
                Csv(segment.SegmentId),
                segment.StartTimestampMs.ToString(CultureInfo.InvariantCulture),
                segment.EndTimestampMs.ToString(CultureInfo.InvariantCulture),
                Csv(segment.Text),
                Csv(segment.TextType),
                Csv(segment.FontFamily),
                Csv(FormatNullable(segment.FontSize)),
                Csv(segment.TextColor),
                Csv(segment.StrokeColor),
                Csv(segment.BackgroundColor),
                Csv(FormatNullable(segment.Confidence))));
        }

        return builder.ToString();
    }

    private static string BuildFramesCsv(IReadOnlyList<FrameExportRecord> frames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("frame_index,timestamp_ms,text,font_family,font_size,text_color,stroke_color,background_color,confidence");

        foreach (var frame in frames)
        {
            if (frame.Detections.Count == 0)
            {
                builder.AppendLine(string.Join(
                    ',',
                    frame.FrameIndex.ToString(CultureInfo.InvariantCulture),
                    frame.TimestampMs.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
                continue;
            }

            foreach (var detection in frame.Detections)
            {
                builder.AppendLine(string.Join(
                    ',',
                    frame.FrameIndex.ToString(CultureInfo.InvariantCulture),
                    frame.TimestampMs.ToString(CultureInfo.InvariantCulture),
                    Csv(detection.Text),
                    Csv(detection.FontFamily),
                    Csv(FormatNullable(detection.FontSize)),
                    Csv(detection.TextColor),
                    Csv(detection.StrokeColor),
                    Csv(detection.BackgroundColor),
                    Csv(FormatNullable(detection.Confidence))));
            }
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return normalized.Contains('"', StringComparison.Ordinal)
            || normalized.Contains(',', StringComparison.Ordinal)
            || normalized.Contains(' ', StringComparison.Ordinal)
            ? $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : normalized;
    }

    private static string? FormatNullable(double? value)
    {
        return value?.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }
}
