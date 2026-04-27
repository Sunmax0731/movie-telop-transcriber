namespace MovieTelopTranscriber.App.Models;

public sealed record VideoMetadata(
    string FilePath,
    string FileName,
    long DurationMs,
    int Width,
    int Height,
    double Fps,
    string Codec);
