using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record PreviewDetectionOverlay(
    string DetectionId,
    string Text,
    double? Confidence,
    IReadOnlyList<OcrBoundingPoint> BoundingBox,
    bool IsHighlighted);
