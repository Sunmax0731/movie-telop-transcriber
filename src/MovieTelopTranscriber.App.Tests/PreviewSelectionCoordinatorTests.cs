using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class PreviewSelectionCoordinatorTests
{
    [Fact]
    public void BuildTimelineSelectionState_MatchesResultRowBySegmentId()
    {
        var timelineSelection = new TimelineSegment(
            "00:00-00:01",
            "字幕",
            "caption",
            frameIndex: 12,
            timestampMs: 1000,
            segmentId: "seg-001",
            detectionId: "det-001");
        var resultRows = new[]
        {
            new ResultRow("00:00-00:01", "OCR", "字幕", "detail", 12, 1000, "seg-001", "det-001")
        };

        var state = PreviewSelectionCoordinator.BuildTimelineSelectionState(timelineSelection, resultRows);

        Assert.Same(timelineSelection, state.TimelineSelection);
        Assert.Same(resultRows[0], state.ResultSelection);
        Assert.Equal("seg-001", state.PreviewRequest?.SegmentId);
    }

    [Fact]
    public void BuildFrameSelectionState_MultipleRowsAggregatesDetectionIds()
    {
        var frame = new ExtractedFrameRecord(3, 3000, @"D:\cache\frame-000003.png");
        var analyses = new[]
        {
            new FrameAnalysisResult(
                frame,
                new OcrWorkerResponse(
                    "ocr-000003-00003000ms",
                    "success",
                    3,
                    3000,
                    Array.Empty<OcrDetectionRecord>(),
                    null),
                new AttributeAnalysisResult(3, 3000, "success", Array.Empty<TelopAttributeRecord>(), null),
                new OcrFramePerformanceRecord(3, 3000, false, "frame-selection", 0, 0, 0, 0, 0, 0, 0, 0, 0))
        };
        var timelineSegments = new[]
        {
            new TimelineSegment("00:03-00:04", "一行目", "caption", frameIndex: 3, timestampMs: 3000, segmentId: "seg-001", detectionId: "det-001", detectionIds: ["det-001"]),
            new TimelineSegment("00:03-00:04", "二行目", "caption", frameIndex: 3, timestampMs: 3000, segmentId: "seg-002", detectionId: "det-002", detectionIds: ["det-002"])
        };
        var resultRows = new[]
        {
            new ResultRow("00:03-00:04", "OCR", "一行目", "detail", 3, 3000, "seg-001", "det-001")
        };

        var state = PreviewSelectionCoordinator.BuildFrameSelectionState(analyses, timelineSegments, resultRows, 0);

        Assert.Same(timelineSegments[0], state.TimelineSelection);
        Assert.Same(resultRows[0], state.ResultSelection);
        Assert.Equal(["det-001", "det-002"], state.PreviewRequest?.DetectionIds);
        Assert.Null(state.PreviewRequest?.DetectionId);
    }

    [Fact]
    public void ResolvePreviewAnalysis_PrefersDetectionIdsOverFrameIndex()
    {
        var firstFrame = new ExtractedFrameRecord(0, 1000, @"D:\cache\frame-000000.png");
        var secondFrame = new ExtractedFrameRecord(1, 2000, @"D:\cache\frame-000001.png");
        var analyses = new[]
        {
            new FrameAnalysisResult(
                firstFrame,
                new OcrWorkerResponse(
                    "ocr-000000-00001000ms",
                    "success",
                    0,
                    1000,
                    [new OcrDetectionRecord("det-001", "A", 0.9, Array.Empty<OcrBoundingPoint>())],
                    null),
                new AttributeAnalysisResult(0, 1000, "success", Array.Empty<TelopAttributeRecord>(), null),
                new OcrFramePerformanceRecord(0, 1000, false, "preview", 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            new FrameAnalysisResult(
                secondFrame,
                new OcrWorkerResponse(
                    "ocr-000001-00002000ms",
                    "success",
                    1,
                    2000,
                    [new OcrDetectionRecord("det-999", "B", 0.9, Array.Empty<OcrBoundingPoint>())],
                    null),
                new AttributeAnalysisResult(1, 2000, "success", Array.Empty<TelopAttributeRecord>(), null),
                new OcrFramePerformanceRecord(1, 2000, false, "preview", 0, 0, 0, 0, 0, 0, 0, 0, 0))
        };
        var request = new PreviewSelectionRequest(
            FrameIndex: 0,
            TimestampMs: 1000,
            SegmentId: "seg-001",
            SelectedText: "A",
            DetectionIds: ["det-999"]);

        var analysis = PreviewSelectionCoordinator.ResolvePreviewAnalysis(analyses, request);

        Assert.Same(analyses[1], analysis);
    }
}
