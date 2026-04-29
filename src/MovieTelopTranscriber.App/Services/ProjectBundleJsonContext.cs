using System.Text.Json.Serialization;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectBundleManifest))]
internal sealed partial class ProjectBundleJsonContext : JsonSerializerContext
{
}
