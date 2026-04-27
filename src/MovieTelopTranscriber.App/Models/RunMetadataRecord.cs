namespace MovieTelopTranscriber.App.Models;

public sealed record RunMetadataRecord(
    DateTimeOffset GeneratedAt,
    string ApplicationVersion,
    long? ProcessingTimeMs,
    int WarningCount,
    int ErrorCount);
