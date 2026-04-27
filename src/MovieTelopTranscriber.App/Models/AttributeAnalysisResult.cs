using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record AttributeAnalysisResult(
    int FrameIndex,
    long TimestampMs,
    string Status,
    IReadOnlyList<TelopAttributeRecord> Detections,
    ProcessingError? Error);
