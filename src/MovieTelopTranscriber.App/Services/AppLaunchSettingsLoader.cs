using System.Globalization;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

internal static class AppLaunchSettingsLoader
{
    private const string SettingsFileName = "movie-telop-transcriber.settings.json";

    public static void Apply()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return;
        }

        AppLaunchSettings? settings;
        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            settings = ParseSettings(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Environment.SetEnvironmentVariable(
                "MOVIE_TELOP_LAUNCH_SETTINGS_LOAD_ERROR",
                $"{settingsPath}: {ex.Message}");
            return;
        }

        if (settings is null)
        {
            return;
        }

        SetString("MOVIE_TELOP_OCR_ENGINE", settings.OcrEngine);
        SetPath("MOVIE_TELOP_OCR_WORKER", settings.OcrWorkerPath, settingsPath);

        if (settings.PaddleOcr is null)
        {
            return;
        }

        SetPath("MOVIE_TELOP_PADDLEOCR_PYTHON", settings.PaddleOcr.PythonPath, settingsPath);
        SetPath("MOVIE_TELOP_PADDLEOCR_SCRIPT", settings.PaddleOcr.ScriptPath, settingsPath);
        SetString("MOVIE_TELOP_PADDLEOCR_DEVICE", settings.PaddleOcr.Device);
        SetString("MOVIE_TELOP_PADDLEOCR_LANG", settings.PaddleOcr.Language);
        SetDouble("MOVIE_TELOP_PADDLEOCR_MIN_SCORE", settings.PaddleOcr.MinScore);
        SetBool("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA", settings.PaddleOcr.NormalizeSmallKana);
        SetBool("MOVIE_TELOP_PADDLEOCR_PREPROCESS", settings.PaddleOcr.Preprocess);
        SetDouble("MOVIE_TELOP_PADDLEOCR_CONTRAST", settings.PaddleOcr.Contrast);
        SetBool("MOVIE_TELOP_PADDLEOCR_SHARPEN", settings.PaddleOcr.Sharpen);
        SetDouble("MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH", settings.PaddleOcr.TextDetThresh);
        SetDouble("MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH", settings.PaddleOcr.TextDetBoxThresh);
        SetDouble("MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO", settings.PaddleOcr.TextDetUnclipRatio);
        SetInt("MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN", settings.PaddleOcr.TextDetLimitSideLen);
        SetBool("MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION", settings.PaddleOcr.UseTextlineOrientation);
        SetBool("MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING", settings.PaddleOcr.UseDocUnwarping);
    }

    private static void SetPath(string environmentVariable, string? value, string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var resolved = ResolvePath(value, settingsPath);
        Environment.SetEnvironmentVariable(environmentVariable, resolved);
    }

    private static void SetString(string environmentVariable, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Environment.SetEnvironmentVariable(environmentVariable, value);
    }

    private static void SetBool(string environmentVariable, bool? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        Environment.SetEnvironmentVariable(environmentVariable, value.Value ? "true" : "false");
    }

    private static void SetDouble(string environmentVariable, double? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            environmentVariable,
            value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void SetInt(string environmentVariable, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            environmentVariable,
            value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string ResolvePath(string value, string settingsPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(settingsDirectory, expanded));
    }

    private static AppLaunchSettings ParseSettings(JsonElement root)
    {
        return new AppLaunchSettings
        {
            OcrEngine = ReadString(root, "ocrEngine"),
            OcrWorkerPath = ReadString(root, "ocrWorkerPath"),
            PaddleOcr = ReadObject(root, "paddleOcr") is { } paddle
                ? new PaddleOcrLaunchSettings
                {
                    PythonPath = ReadString(paddle, "pythonPath"),
                    ScriptPath = ReadString(paddle, "scriptPath"),
                    Device = ReadString(paddle, "device"),
                    Language = ReadString(paddle, "language"),
                    MinScore = ReadDouble(paddle, "minScore"),
                    NormalizeSmallKana = ReadBool(paddle, "normalizeSmallKana"),
                    Preprocess = ReadBool(paddle, "preprocess"),
                    Contrast = ReadDouble(paddle, "contrast"),
                    Sharpen = ReadBool(paddle, "sharpen"),
                    TextDetThresh = ReadDouble(paddle, "textDetThresh"),
                    TextDetBoxThresh = ReadDouble(paddle, "textDetBoxThresh"),
                    TextDetUnclipRatio = ReadDouble(paddle, "textDetUnclipRatio"),
                    TextDetLimitSideLen = ReadInt(paddle, "textDetLimitSideLen"),
                    UseTextlineOrientation = ReadBool(paddle, "useTextlineOrientation"),
                    UseDocUnwarping = ReadBool(paddle, "useDocUnwarping")
                }
                : null
        };
    }

    private static JsonElement? ReadObject(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Object ? property : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
