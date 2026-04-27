using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record OcrWorkerResponse(
    string RequestId,
    string Status,
    int FrameIndex,
    long TimestampMs,
    IReadOnlyList<OcrDetectionRecord> Detections,
    ProcessingError? Error);
