using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record FrameExportRecord(
    int FrameIndex,
    long TimestampMs,
    string ImagePath,
    IReadOnlyList<TelopAttributeRecord> Detections);
