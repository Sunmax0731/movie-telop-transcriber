using System.Text.Json.Serialization;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppLaunchSettings))]
[JsonSerializable(typeof(MainWindowLaunchSettings))]
[JsonSerializable(typeof(PaddleOcrLaunchSettings))]
[JsonSerializable(typeof(UserInterfaceSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
