namespace MovieTelopTranscriber.App.Models;

public sealed record OcrFrameSelectionDecision(
    bool ShouldRunOcr,
    string Reason,
    double SelectionMs,
    double RoiDifferenceMean);
