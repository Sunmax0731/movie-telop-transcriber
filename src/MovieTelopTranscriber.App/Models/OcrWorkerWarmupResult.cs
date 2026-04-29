namespace MovieTelopTranscriber.App.Models;

public sealed record OcrWorkerWarmupResult(
    string Status,
    double RequestWriteMs,
    double WorkerInitializationMs,
    double WorkerExecutionMs,
    double ResponseReadMs,
    double TotalMs,
    ProcessingError? Error)
{
    public static OcrWorkerWarmupResult Skipped { get; } = new("skipped", 0d, 0d, 0d, 0d, 0d, null);

    public bool Attempted => !string.Equals(Status, "skipped", StringComparison.OrdinalIgnoreCase);

    public bool Succeeded => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}
