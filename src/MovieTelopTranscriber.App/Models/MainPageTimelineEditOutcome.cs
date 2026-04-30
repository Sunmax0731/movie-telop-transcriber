namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageTimelineEditOutcome(
    MainPageTimelineEditState State,
    bool Changed,
    string StatusMessage,
    string? PreferredSegmentId,
    string? PreferredDetectionId);
