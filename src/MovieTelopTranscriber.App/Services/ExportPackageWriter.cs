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
        IReadOnlyList<EditOperationRecord> edits,
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
            edits,
            new RunMetadataRecord(
                DateTimeOffset.Now,
                GetApplicationVersion(),
                processingTimeMs,
                warningCount,
                errorCount));

        var jsonPath = Path.Combine(outputDirectory, "segments.json");
        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, package, OcrContractJson.ExportPackage, cancellationToken);
        }

        var segmentsCsvPath = Path.Combine(outputDirectory, "segments.csv");
        await File.WriteAllTextAsync(segmentsCsvPath, BuildSegmentsCsv(segments), new UTF8Encoding(false), cancellationToken);

        var framesCsvPath = Path.Combine(outputDirectory, "frames.csv");
        await File.WriteAllTextAsync(framesCsvPath, BuildFramesCsv(package.Frames), new UTF8Encoding(false), cancellationToken);

        var srtPath = Path.Combine(outputDirectory, "segments.srt");
        await File.WriteAllTextAsync(srtPath, BuildSrt(segments), new UTF8Encoding(false), cancellationToken);

        var vttPath = Path.Combine(outputDirectory, "segments.vtt");
        await File.WriteAllTextAsync(vttPath, BuildVtt(segments), new UTF8Encoding(false), cancellationToken);

        var assPath = Path.Combine(outputDirectory, "segments.ass");
        await File.WriteAllTextAsync(assPath, BuildAss(segments), new UTF8Encoding(false), cancellationToken);

        return new ExportWriteResult(outputDirectory, jsonPath, segmentsCsvPath, framesCsvPath, srtPath, vttPath, assPath);
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

    private static string BuildSrt(IReadOnlyList<SegmentRecord> segments)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in segments.Where(HasSubtitleText))
        {
            var endTimestampMs = NormalizeEndTimestampMs(segment.StartTimestampMs, segment.EndTimestampMs);
            builder.AppendLine(index.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine($"{FormatSrtTimestamp(segment.StartTimestampMs)} --> {FormatSrtTimestamp(endTimestampMs)}");
            builder.AppendLine(NormalizeSubtitleText(segment.Text));
            builder.AppendLine();
            index++;
        }

        return builder.ToString();
    }

    private static string BuildVtt(IReadOnlyList<SegmentRecord> segments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WEBVTT");
        builder.AppendLine();

        foreach (var segment in segments.Where(HasSubtitleText))
        {
            var endTimestampMs = NormalizeEndTimestampMs(segment.StartTimestampMs, segment.EndTimestampMs);
            builder.AppendLine($"{FormatVttTimestamp(segment.StartTimestampMs)} --> {FormatVttTimestamp(endTimestampMs)}");
            builder.AppendLine(NormalizeSubtitleText(segment.Text));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildAss(IReadOnlyList<SegmentRecord> segments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("Collisions: Normal");
        builder.AppendLine("PlayResX: 1920");
        builder.AppendLine("PlayResY: 1080");
        builder.AppendLine("Timer: 100.0000");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine("Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,0,2,40,40,40,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var segment in segments.Where(HasSubtitleText))
        {
            var endTimestampMs = NormalizeEndTimestampMs(segment.StartTimestampMs, segment.EndTimestampMs);
            builder.AppendLine(string.Join(
                ',',
                "Dialogue: 0",
                FormatAssTimestamp(segment.StartTimestampMs),
                FormatAssTimestamp(endTimestampMs),
                "Default",
                string.Empty,
                "0",
                "0",
                "0",
                string.Empty,
                EscapeAssText(segment.Text)));
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

    private static bool HasSubtitleText(SegmentRecord segment)
    {
        return !string.IsNullOrWhiteSpace(segment.Text);
    }

    private static long NormalizeEndTimestampMs(long startTimestampMs, long endTimestampMs)
    {
        return endTimestampMs > startTimestampMs ? endTimestampMs : startTimestampMs + 1000;
    }

    private static string NormalizeSubtitleText(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeAssText(string text)
    {
        return NormalizeSubtitleText(text)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace(Environment.NewLine, "\\N", StringComparison.Ordinal);
    }

    private static string FormatSrtTimestamp(long timestampMs)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, timestampMs));
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }

    private static string FormatVttTimestamp(long timestampMs)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, timestampMs));
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private static string FormatAssTimestamp(long timestampMs)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, timestampMs));
        var centiseconds = time.Milliseconds / 10;
        return $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{centiseconds:00}";
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
