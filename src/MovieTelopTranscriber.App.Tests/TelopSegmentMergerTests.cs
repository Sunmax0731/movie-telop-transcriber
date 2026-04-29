using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class TelopSegmentMergerTests
{
    [Fact]
    public void Merge_AdjacentFramesWithSameText_MergesIntoSingleSegment()
    {
        var merger = new TelopSegmentMerger();
        var frames = new[]
        {
            CreateFrameAnalysis(
                frameIndex: 0,
                timestampMs: 1000,
                detection: CreateDetection("det-01", "サンプル テロップ", 0.90, fontSize: 30)),
            CreateFrameAnalysis(
                frameIndex: 1,
                timestampMs: 2000,
                detection: CreateDetection("det-02", "サンプルテロップ", 0.96, fontSize: 36))
        };

        var segments = merger.Merge(frames, frameIntervalSeconds: 1.0d);

        var segment = Assert.Single(segments);
        Assert.Equal(1000, segment.StartTimestampMs);
        Assert.Equal(3000, segment.EndTimestampMs);
        Assert.Equal("サンプルテロップ", segment.Text);
        Assert.Equal("caption_band", segment.TextType);
        Assert.Equal(2, segment.SourceFrameCount);
        Assert.Equal(0.93, segment.Confidence);
        Assert.Equal(33.0, segment.FontSize);
    }

    [Fact]
    public void Merge_DifferentTextTypes_DoesNotMergeSegments()
    {
        var merger = new TelopSegmentMerger();
        var frames = new[]
        {
            CreateFrameAnalysis(
                frameIndex: 0,
                timestampMs: 1000,
                detection: CreateDetection("det-01", "重要なお知らせ", 0.99, textType: "title")),
            CreateFrameAnalysis(
                frameIndex: 1,
                timestampMs: 2000,
                detection: CreateDetection("det-02", "重要なお知らせ", 0.99, textType: "caption_band"))
        };

        var segments = merger.Merge(frames, frameIntervalSeconds: 1.0d);

        Assert.Equal(2, segments.Count);
        Assert.Collection(
            segments,
            first =>
            {
                Assert.Equal(1000, first.StartTimestampMs);
                Assert.Equal(2000, first.EndTimestampMs);
                Assert.Equal("title", first.TextType);
            },
            second =>
            {
                Assert.Equal(2000, second.StartTimestampMs);
                Assert.Equal(3000, second.EndTimestampMs);
                Assert.Equal("caption_band", second.TextType);
            });
    }

    private static FrameAnalysisResult CreateFrameAnalysis(
        int frameIndex,
        long timestampMs,
        TelopAttributeRecord detection)
    {
        return new FrameAnalysisResult(
            new ExtractedFrameRecord(frameIndex, timestampMs, $@"frames\frame-{frameIndex:D4}.png"),
            new OcrWorkerResponse(
                $"req-{frameIndex:D4}",
                "success",
                frameIndex,
                timestampMs,
                Array.Empty<OcrDetectionRecord>(),
                null),
            new AttributeAnalysisResult(
                frameIndex,
                timestampMs,
                "success",
                new[] { detection },
                null),
            new OcrFramePerformanceRecord(
                frameIndex,
                timestampMs,
                true,
                "ocr_executed",
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0));
    }

    private static TelopAttributeRecord CreateDetection(
        string detectionId,
        string text,
        double confidence,
        double fontSize = 32.0,
        string textType = "caption_band")
    {
        return new TelopAttributeRecord(
            detectionId,
            text,
            confidence,
            "Meiryo",
            fontSize,
            "px",
            "#FFFFFF",
            "#000000",
            "#144A8B",
            textType);
    }
}
