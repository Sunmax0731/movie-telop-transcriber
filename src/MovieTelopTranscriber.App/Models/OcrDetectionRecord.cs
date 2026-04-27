using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record OcrDetectionRecord(
    string DetectionId,
    string Text,
    double? Confidence,
    IReadOnlyList<OcrBoundingPoint> BoundingBox);
