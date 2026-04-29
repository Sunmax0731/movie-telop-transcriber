using System.Globalization;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

internal static class AppLaunchSettingsLoader
{
    private const string SettingsFileName = "movie-telop-transcriber.settings.json";
    private const string InstallRootName = "MovieTelopTranscriber";
    private const string InstallManifestFileName = "movie-telop-transcriber.installation.json";
    private const string InstallerCommandFileName = "Install-MovieTelopTranscriber.cmd";

    public static AppLaunchSettingsLoadResult Load()
    {
        var settingsPath = FindSettingsPath();
        if (settingsPath is null)
        {
            return new AppLaunchSettingsLoadResult(null, new AppLaunchSettings(), null);
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            return new AppLaunchSettingsLoadResult(settingsPath, ParseSettings(document.RootElement), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppLaunchSettingsLoadResult(settingsPath, new AppLaunchSettings(), ex.Message);
        }
    }

    public static void Apply(AppLaunchSettings settings, string? settingsPath, string? loadError)
    {
        if (!string.IsNullOrWhiteSpace(loadError) && !string.IsNullOrWhiteSpace(settingsPath))
        {
            Environment.SetEnvironmentVariable(
                "MOVIE_TELOP_LAUNCH_SETTINGS_LOAD_ERROR",
                $"{settingsPath}: {loadError}");
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
        SetDouble("MOVIE_TELOP_PADDLEOCR_MIN_TEXT_SIZE", settings.PaddleOcr.MinTextSize);
    }

    public static void Save(AppLaunchSettings settings, string? preferredSettingsPath = null)
    {
        var settingsPath = ResolveWritableSettingsPath(preferredSettingsPath);
        var settingsDirectory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(settingsDirectory))
        {
            Directory.CreateDirectory(settingsDirectory);
        }

        var normalized = NormalizeForSave(settings, settingsPath);
        var json = JsonSerializer.Serialize(normalized, AppSettingsJsonContext.Default.AppLaunchSettings);

        File.WriteAllText(settingsPath, json);
    }

    public static string? FindSettingsPath()
    {
        var baseSettingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (File.Exists(baseSettingsPath))
        {
            return baseSettingsPath;
        }

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        if (!string.Equals(baseDirectory.Name, "app", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var packageRoot = baseDirectory.Parent;
        if (packageRoot is null)
        {
            return null;
        }

        var installerCommandPath = Path.Combine(packageRoot.FullName, InstallerCommandFileName);
        if (!File.Exists(installerCommandPath))
        {
            return null;
        }

        var siblingInstallRoot = Path.Combine(packageRoot.FullName, InstallRootName);
        var siblingManifestPath = Path.Combine(siblingInstallRoot, InstallManifestFileName);
        var siblingSettingsPath = Path.Combine(siblingInstallRoot, "app", SettingsFileName);
        if (File.Exists(siblingManifestPath) && File.Exists(siblingSettingsPath))
        {
            return siblingSettingsPath;
        }

        return null;
    }

    private static string ResolveWritableSettingsPath(string? preferredSettingsPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredSettingsPath))
        {
            return Path.GetFullPath(preferredSettingsPath);
        }

        var discovered = FindSettingsPath();
        if (!string.IsNullOrWhiteSpace(discovered))
        {
            return discovered;
        }

        return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
    }

    private static AppLaunchSettings NormalizeForSave(AppLaunchSettings settings, string settingsPath)
    {
        return new AppLaunchSettings
        {
            OcrEngine = NormalizeString(settings.OcrEngine),
            OcrWorkerPath = NormalizeRelativePath(settings.OcrWorkerPath, settingsPath),
            PaddleOcr = settings.PaddleOcr is null
                ? null
                : new PaddleOcrLaunchSettings
                {
                    PythonPath = NormalizeRelativePath(settings.PaddleOcr.PythonPath, settingsPath),
                    ScriptPath = NormalizeRelativePath(settings.PaddleOcr.ScriptPath, settingsPath),
                    Device = NormalizeString(settings.PaddleOcr.Device),
                    Language = NormalizeString(settings.PaddleOcr.Language),
                    MinScore = settings.PaddleOcr.MinScore,
                    NormalizeSmallKana = settings.PaddleOcr.NormalizeSmallKana,
                    Preprocess = settings.PaddleOcr.Preprocess,
                    Contrast = settings.PaddleOcr.Contrast,
                    Sharpen = settings.PaddleOcr.Sharpen,
                    TextDetThresh = settings.PaddleOcr.TextDetThresh,
                    TextDetBoxThresh = settings.PaddleOcr.TextDetBoxThresh,
                    TextDetUnclipRatio = settings.PaddleOcr.TextDetUnclipRatio,
                    TextDetLimitSideLen = settings.PaddleOcr.TextDetLimitSideLen,
                    UseTextlineOrientation = settings.PaddleOcr.UseTextlineOrientation,
                    UseDocUnwarping = settings.PaddleOcr.UseDocUnwarping,
                    MinTextSize = settings.PaddleOcr.MinTextSize
                },
            Ui = settings.Ui is null
                ? null
                : new UserInterfaceSettings
                {
                    Language = NormalizeString(settings.Ui.Language),
                    FrameIntervalSeconds = settings.Ui.FrameIntervalSeconds,
                    OutputRootDirectory = NormalizeRelativePath(settings.Ui.OutputRootDirectory, settingsPath),
                    MainWindow = settings.Ui.MainWindow is null
                        ? null
                        : new MainWindowLaunchSettings
                        {
                            Width = settings.Ui.MainWindow.Width,
                            Height = settings.Ui.MainWindow.Height
                        }
                }
        };
    }

    private static string? NormalizeRelativePath(string? value, string settingsPath)
    {
        var normalized = NormalizeString(value);
        if (normalized is null)
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(normalized);
        if (!Path.IsPathRooted(expanded))
        {
            return normalized;
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return Path.GetFullPath(expanded);
        }

        var fullPath = Path.GetFullPath(expanded);
        var relativePath = Path.GetRelativePath(settingsDirectory, fullPath);
        return relativePath.StartsWith("..", StringComparison.Ordinal)
            ? fullPath
            : relativePath;
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void SetPath(string environmentVariable, string? value, string? settingsPath)
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

    private static string ResolvePath(string value, string? settingsPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
        }

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
                    UseDocUnwarping = ReadBool(paddle, "useDocUnwarping"),
                    MinTextSize = ReadDouble(paddle, "minTextSize")
                }
                : null,
            Ui = ReadObject(root, "ui") is { } ui
                ? new UserInterfaceSettings
                {
                    Language = ReadString(ui, "language"),
                    FrameIntervalSeconds = ReadDouble(ui, "frameIntervalSeconds"),
                    OutputRootDirectory = ReadString(ui, "outputRootDirectory"),
                    MainWindow = ReadObject(ui, "mainWindow") is { } mainWindow
                        ? new MainWindowLaunchSettings
                        {
                            Width = ReadInt(mainWindow, "width"),
                            Height = ReadInt(mainWindow, "height")
                        }
                        : null
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

internal sealed record AppLaunchSettingsLoadResult(
    string? SettingsPath,
    AppLaunchSettings Settings,
    string? LoadError);
