namespace MovieTelopTranscriber.App.Models;

public sealed record OcrWorkerRequest(
    string RequestId,
    int FrameIndex,
    long TimestampMs,
    string ImagePath,
    string LanguageHint,
    string Engine);
