using MovieTelopTranscriber.App.Models;
using OpenCvSharp;

namespace MovieTelopTranscriber.App.Services;

public sealed class OcrFrameCandidateSelector
{
    private const double SubtitleBandTopRatio = 0.55d;
    private const double DefaultRoiChangeThreshold = 2.0d;
    private const int DefaultMaxSkippedFrames = 4;

    public OcrFrameSelectionDecision Decide(
        ExtractedFrameRecord frame,
        ExtractedFrameRecord? previousFrame,
        FrameAnalysisResult? previousAnalysis,
        int consecutiveSkippedFrames)
    {
        return Decide(
            frame,
            previousFrame,
            previousAnalysis is not null,
            string.Equals(previousAnalysis?.Ocr.Status, "error", StringComparison.OrdinalIgnoreCase),
            consecutiveSkippedFrames);
    }

    public OcrFrameSelectionDecision Decide(
        ExtractedFrameRecord frame,
        ExtractedFrameRecord? previousFrame,
        bool hasPreviousAnalysis,
        bool previousOcrHadError,
        int consecutiveSkippedFrames)
    {
        var startedAt = DateTime.UtcNow;

        if (previousFrame is null || !hasPreviousAnalysis)
        {
            return CreateDecision(true, "first_frame", startedAt, 0d);
        }

        if (previousOcrHadError)
        {
            return CreateDecision(true, "previous_error_retry", startedAt, 0d);
        }

        if (consecutiveSkippedFrames >= DefaultMaxSkippedFrames)
        {
            return CreateDecision(true, "periodic_refresh", startedAt, 0d);
        }

        var roiDifference = CalculateRoiDifference(previousFrame.ImagePath, frame.ImagePath);
        if (roiDifference >= DefaultRoiChangeThreshold)
        {
            return CreateDecision(true, "roi_changed", startedAt, roiDifference);
        }

        return CreateDecision(false, "reuse_previous_result", startedAt, roiDifference);
    }

    private static OcrFrameSelectionDecision CreateDecision(
        bool shouldRunOcr,
        string reason,
        DateTime startedAt,
        double roiDifference)
    {
        return new OcrFrameSelectionDecision(
            shouldRunOcr,
            reason,
            Math.Max(0d, (DateTime.UtcNow - startedAt).TotalMilliseconds),
            roiDifference);
    }

    private static double CalculateRoiDifference(string previousImagePath, string currentImagePath)
    {
        using var previous = Cv2.ImRead(previousImagePath, ImreadModes.Grayscale);
        using var current = Cv2.ImRead(currentImagePath, ImreadModes.Grayscale);
        if (previous.Empty() || current.Empty())
        {
            return double.MaxValue;
        }

        var roiHeight = Math.Min(previous.Rows, current.Rows);
        var roiWidth = Math.Min(previous.Cols, current.Cols);
        if (roiHeight <= 0 || roiWidth <= 0)
        {
            return double.MaxValue;
        }

        var top = Math.Clamp((int)Math.Round(roiHeight * SubtitleBandTopRatio), 0, Math.Max(0, roiHeight - 1));
        var height = roiHeight - top;
        if (height <= 0)
        {
            return double.MaxValue;
        }

        var roiRect = new Rect(0, top, roiWidth, height);
        using var previousRoi = new Mat(previous, roiRect);
        using var currentRoi = new Mat(current, roiRect);
        using var diff = new Mat();
        Cv2.Absdiff(previousRoi, currentRoi, diff);
        return Cv2.Mean(diff).Val0;
    }
}
