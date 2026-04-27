using System.Text.Json;

namespace MovieTelopTranscriber.App.Services;

internal static class OcrContractJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}
