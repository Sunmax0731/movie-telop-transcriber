namespace MovieTelopTranscriber.App.Models;

public sealed record ProcessingError(
    string Code,
    string Message,
    string? Details,
    bool Recoverable);
