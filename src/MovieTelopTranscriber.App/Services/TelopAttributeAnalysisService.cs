using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopAttributeAnalysisService
{
    public AttributeAnalysisResult Analyze(OcrWorkerResponse ocrResponse)
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

        var detections = ocrResponse.Detections
            .Where(detection => !string.IsNullOrWhiteSpace(detection.Text))
            .Select(detection => new TelopAttributeRecord(
                detection.DetectionId,
                detection.Text,
                detection.Confidence,
                null,
                EstimateFontSize(detection.BoundingBox),
                "px",
                null,
                null,
                null,
                "unknown"))
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
}
