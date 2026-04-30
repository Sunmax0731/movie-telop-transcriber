namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageOcrWarmupState(
    Task<OcrWorkerWarmupResult>? PendingTask,
    string? PendingSettingsSignature)
{
    public static MainPageOcrWarmupState Empty { get; } = new(null, null);
}
