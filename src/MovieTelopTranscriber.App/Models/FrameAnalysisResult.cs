namespace MovieTelopTranscriber.App.Models;

public sealed record FrameAnalysisResult(
    ExtractedFrameRecord Frame,
    OcrWorkerResponse Ocr,
    AttributeAnalysisResult Attributes);
