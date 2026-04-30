namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageOcrWarmupResolution(
    MainPageOcrWarmupState State,
    OcrWorkerWarmupResult Result);
