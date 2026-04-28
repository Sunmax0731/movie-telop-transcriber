namespace MovieTelopTranscriber.App.Models;

public sealed record EditOperationRecord(
    string Operation,
    string TargetId,
    string? RelatedId,
    string? DetectionId,
    string? OriginalText,
    string? UpdatedText,
    long? StartTimestampMs,
    long? EndTimestampMs,
    DateTimeOffset EditedAt,
    string? Notes);
