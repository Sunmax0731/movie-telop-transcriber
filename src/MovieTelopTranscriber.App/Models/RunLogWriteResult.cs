namespace MovieTelopTranscriber.App.Models;

public sealed record RunLogWriteResult(
    string LogsDirectory,
    string LogPath,
    string SummaryPath);
