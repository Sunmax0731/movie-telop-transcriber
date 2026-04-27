namespace MovieTelopTranscriber.App.Models;

public sealed record ProcessingSettingsRecord(
    double FrameIntervalSeconds,
    string OcrEngine,
    bool OfflineMode);
