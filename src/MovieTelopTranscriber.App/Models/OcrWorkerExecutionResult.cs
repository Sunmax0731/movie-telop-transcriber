namespace MovieTelopTranscriber.App.Models;

public sealed record OcrWorkerExecutionResult(
    OcrWorkerResponse Response,
    double RequestWriteMs,
    double WorkerInitializationMs,
    double WorkerExecutionMs,
    double ResponseReadMs);
