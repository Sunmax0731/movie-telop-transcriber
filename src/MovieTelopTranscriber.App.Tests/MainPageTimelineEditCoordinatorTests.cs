using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class MainPageTimelineEditCoordinatorTests
{
    [Fact]
    public void UpdateText_UpdatesSegmentDetectionAndEditLog()
    {
        var segment = new TimelineSegment("00:01-00:02", "旧字幕", "caption", frameIndex: 1, timestampMs: 1000, segmentId: "seg-001", detectionId: "det-001", detectionIds: ["det-001"]);
        var state = CreateState(
            frameAnalyses:
            [
                CreateAnalysis(1, 1000, "det-001", "旧字幕")
            ],
            segments:
            [
                new SegmentRecord("seg-001", 1000, 2000, "旧字幕", "caption", null, 32, "px", null, null, null, 0.9, 1)
            ],
            detectionIds: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["seg-001"] = ["det-001"]
            });

        var outcome = MainPageTimelineEditCoordinator.UpdateText(state, segment, "新字幕");

        Assert.True(outcome.Changed);
        Assert.Equal("新字幕", outcome.State.LatestSegments[0].Text);
        Assert.Equal("新字幕", outcome.State.LatestFrameAnalyses[0].Ocr.Detections[0].Text);
        Assert.Equal(["det-001"], outcome.State.SegmentDetectionIds["seg-001"]);
        Assert.Single(outcome.State.TimelineEdits);
        Assert.Equal("edit", outcome.State.TimelineEdits[0].Operation);
    }

    [Fact]
    public void Merge_CombinesSegmentsAndDetectionIds()
    {
        var first = new TimelineSegment("00:01-00:02", "前半", "caption", category: "caption_band", detail: "white / black", frameIndex: 1, timestampMs: 1000, segmentId: "seg-001", detectionId: "det-001", detectionIds: ["det-001"]);
        var second = new TimelineSegment("00:02-00:03", "後半", "caption", category: "caption_band", detail: "white / black", frameIndex: 2, timestampMs: 2000, segmentId: "seg-002", detectionId: "det-002", detectionIds: ["det-002"]);
        var state = CreateState(
            frameAnalyses:
            [
                CreateAnalysis(1, 1000, "det-001", "前半"),
                CreateAnalysis(2, 2000, "det-002", "後半")
            ],
            segments:
            [
                new SegmentRecord("seg-001", 1000, 2000, "前半", "caption_band", null, 32, "px", null, null, null, 0.8, 1),
                new SegmentRecord("seg-002", 2000, 3000, "後半", "caption_band", null, 32, "px", null, null, null, 0.6, 1)
            ],
            detectionIds: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["seg-001"] = ["det-001"],
                ["seg-002"] = ["det-002"]
            });

        var outcome = MainPageTimelineEditCoordinator.Merge(state, first, second);

        Assert.True(outcome.Changed);
        Assert.Single(outcome.State.LatestSegments);
        Assert.Equal("前半 後半", outcome.State.LatestSegments[0].Text);
        Assert.Equal(["det-001", "det-002"], outcome.State.SegmentDetectionIds["seg-001"]);
        Assert.False(outcome.State.SegmentDetectionIds.ContainsKey("seg-002"));
        Assert.Single(outcome.State.TimelineEdits);
        Assert.Equal("merge", outcome.State.TimelineEdits[0].Operation);
    }

    [Fact]
    public void Split_DetectionOnlyRow_CreatesSecondDetectionAndIncrementsSequence()
    {
        var segment = new TimelineSegment("00:01-00:02", "一行 二行", "caption", frameIndex: 1, timestampMs: 1000, detectionId: "det-001", detectionIds: ["det-001"]);
        var state = CreateState(
            frameAnalyses:
            [
                CreateAnalysis(1, 1000, "det-001", "一行 二行")
            ],
            segments: Array.Empty<SegmentRecord>(),
            manualEditSequence: 0);

        var outcome = MainPageTimelineEditCoordinator.Split(state, segment, "一行", "二行");

        Assert.True(outcome.Changed);
        Assert.Equal(1, outcome.State.ManualEditSequence);
        Assert.Equal(2, outcome.State.LatestFrameAnalyses[0].Ocr.Detections.Count);
        Assert.Equal(["一行", "二行"], outcome.State.LatestFrameAnalyses[0].Ocr.Detections.Select(item => item.Text).ToArray());
        Assert.Equal(["det-001", "det-001-split-001"], outcome.State.LatestFrameAnalyses[0].Ocr.Detections.Select(item => item.DetectionId).ToArray());
        Assert.Single(outcome.State.TimelineEdits);
        Assert.Equal("split", outcome.State.TimelineEdits[0].Operation);
    }

    private static MainPageTimelineEditState CreateState(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<SegmentRecord> segments,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? detectionIds = null,
        int manualEditSequence = 0)
    {
        return new MainPageTimelineEditState(
            frameAnalyses,
            segments,
            Array.Empty<EditOperationRecord>(),
            detectionIds ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            manualEditSequence);
    }

    private static FrameAnalysisResult CreateAnalysis(int frameIndex, long timestampMs, string detectionId, string text)
    {
        return new FrameAnalysisResult(
            new ExtractedFrameRecord(frameIndex, timestampMs, $@"D:\cache\frame-{frameIndex:D6}.png"),
            new OcrWorkerResponse(
                $"ocr-{frameIndex:D6}-{timestampMs:D8}ms",
                "success",
                frameIndex,
                timestampMs,
                [new OcrDetectionRecord(detectionId, text, 0.9, Array.Empty<OcrBoundingPoint>())],
                null),
            new AttributeAnalysisResult(
                frameIndex,
                timestampMs,
                "success",
                [new TelopAttributeRecord(detectionId, text, 0.9, null, 32, "px", null, null, null, "caption")],
                null),
            new OcrFramePerformanceRecord(frameIndex, timestampMs, false, "test", 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }
}
