namespace MovieTelopTranscriber.App.Models;

public sealed record ExtractedFrameRecord(int FrameIndex, long TimestampMs, string ImagePath);
