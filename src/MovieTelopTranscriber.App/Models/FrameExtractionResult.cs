using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record FrameExtractionResult(
    string RunId,
    string RunDirectory,
    string FramesDirectory,
    IReadOnlyList<ExtractedFrameRecord> Frames);
