using System.Globalization;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public static class MainPageUserSettingsCoordinator
{
    public const double DefaultPaddleContrast = 1.1d;
    public const double DefaultPaddleTextDetThresh = 0.3d;
    public const double DefaultPaddleTextDetBoxThresh = 0.6d;
    public const double DefaultPaddleTextDetUnclipRatio = 1.5d;
    public const double DefaultPaddleTextDetLimitSideLen = 960d;
    public const double DefaultPaddleMinTextSize = 0d;
    public const string FixedPaddleUpscale = "1";
    public const string PaddlePreprocessEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_PREPROCESS";
    public const string PaddleUpscaleEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_UPSCALE";
    public const string PaddleContrastEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_CONTRAST";
    public const string PaddleSharpenEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_SHARPEN";
    public const string PaddleTextDetThreshEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH";
    public const string PaddleTextDetBoxThreshEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH";
    public const string PaddleTextDetUnclipRatioEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO";
    public const string PaddleTextDetLimitSideLenEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN";
    public const string PaddleMinTextSizeEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_MIN_TEXT_SIZE";
    public const string PaddleUseTextlineOrientationEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION";
    public const string PaddleUseDocUnwarpingEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING";
    public const string PaddleDeviceEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_DEVICE";
    public const string PaddleWorkerCountEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_WORKER_COUNT";

    public static MainPageStoredUiState ResolveSavedUserInterfaceSettings(
        UserInterfaceSettings? uiSettings,
        IReadOnlyList<LanguageOption> languageOptions,
        string? launchSettingsPath)
    {
        if (uiSettings is null)
        {
            return new MainPageStoredUiState(null, null, null, null);
        }

        var selectedLanguage = ResolveLanguageOption(uiSettings.Language, languageOptions);
        var frameIntervalValue = uiSettings.FrameIntervalSeconds is > 0 ? uiSettings.FrameIntervalSeconds.Value : (double?)null;
        var frameIntervalText = frameIntervalValue.HasValue
            ? FormatSettingNumber(frameIntervalValue.Value, "0.##")
            : null;
        var outputRootDirectoryText = string.IsNullOrWhiteSpace(uiSettings.OutputRootDirectory)
            ? null
            : ResolveSavedPath(uiSettings.OutputRootDirectory, launchSettingsPath);

        return new MainPageStoredUiState(
            selectedLanguage,
            frameIntervalValue,
            frameIntervalText,
            outputRootDirectoryText);
    }

    public static MainPageStoredUiState ResolveProjectUserInterfaceSettings(
        UserInterfaceSettings? uiSettings,
        IReadOnlyList<LanguageOption> languageOptions)
    {
        if (uiSettings is null)
        {
            return new MainPageStoredUiState(null, null, null, null);
        }

        var selectedLanguage = ResolveLanguageOption(uiSettings.Language, languageOptions);
        var frameIntervalValue = uiSettings.FrameIntervalSeconds is > 0 ? uiSettings.FrameIntervalSeconds.Value : (double?)null;
        var frameIntervalText = frameIntervalValue.HasValue
            ? FormatSettingNumber(frameIntervalValue.Value, "0.##")
            : null;
        var outputRootDirectoryText = string.IsNullOrWhiteSpace(uiSettings.OutputRootDirectory)
            ? null
            : uiSettings.OutputRootDirectory;

        return new MainPageStoredUiState(
            selectedLanguage,
            frameIntervalValue,
            frameIntervalText,
            outputRootDirectoryText);
    }

    public static void PersistUserSettings(
        AppLaunchSettings launchSettings,
        string? launchSettingsPath,
        MainPageUserSettingsState state,
        MainWindowLaunchSettings? mainWindowSettings)
    {
        launchSettings.OcrEngine ??= Environment.GetEnvironmentVariable("MOVIE_TELOP_OCR_ENGINE") ?? "paddleocr";
        launchSettings.OcrWorkerPath = Environment.GetEnvironmentVariable("MOVIE_TELOP_OCR_WORKER");
        launchSettings.PaddleOcr ??= new PaddleOcrLaunchSettings();
        launchSettings.PaddleOcr.PythonPath = Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON");
        launchSettings.PaddleOcr.ScriptPath = Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT");
        launchSettings.PaddleOcr.Device = state.SelectedPaddleDeviceKey;
        launchSettings.PaddleOcr.Language = Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_LANG");
        launchSettings.PaddleOcr.MinScore = ReadEnvironmentDouble("MOVIE_TELOP_PADDLEOCR_MIN_SCORE");
        launchSettings.PaddleOcr.NormalizeSmallKana = ReadEnvironmentBool("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA");
        launchSettings.PaddleOcr.Preprocess = state.PaddlePreprocessEnabled;
        launchSettings.PaddleOcr.Contrast = ParseOptionalDouble(state.PaddleContrastText, 0.1d, 4.0d);
        launchSettings.PaddleOcr.Sharpen = state.PaddleSharpenEnabled;
        launchSettings.PaddleOcr.TextDetThresh = ParseOptionalDouble(state.PaddleTextDetThreshText, 0.0d, 1.0d);
        launchSettings.PaddleOcr.TextDetBoxThresh = ParseOptionalDouble(state.PaddleTextDetBoxThreshText, 0.0d, 1.0d);
        launchSettings.PaddleOcr.TextDetUnclipRatio = ParseOptionalDouble(state.PaddleTextDetUnclipRatioText, 0.1d, 10.0d);
        launchSettings.PaddleOcr.TextDetLimitSideLen = ParseOptionalInt(state.PaddleTextDetLimitSideLenText, 16, 4096);
        launchSettings.PaddleOcr.MinTextSize = ParseOptionalDouble(state.PaddleMinTextSizeText, 0.0d, 200.0d);
        launchSettings.PaddleOcr.UseTextlineOrientation = state.PaddleUseTextlineOrientation;
        launchSettings.PaddleOcr.UseDocUnwarping = state.PaddleUseDocUnwarping;
        launchSettings.PaddleOcr.WorkerCount = NormalizePaddleWorkerCount(state.PaddleWorkerCount, state.SelectedPaddleDeviceKey);

        launchSettings.Ui ??= new UserInterfaceSettings();
        launchSettings.Ui.Language = state.SelectedLanguageCode;
        launchSettings.Ui.FrameIntervalSeconds = state.FrameIntervalSeconds;
        launchSettings.Ui.OutputRootDirectory = string.IsNullOrWhiteSpace(state.OutputRootDirectoryText)
            ? null
            : state.OutputRootDirectoryText.Trim();
        launchSettings.Ui.MainWindow = mainWindowSettings is null
            ? null
            : new MainWindowLaunchSettings
            {
                Width = mainWindowSettings.Width,
                Height = mainWindowSettings.Height
            };

        AppLaunchSettingsLoader.Save(launchSettings, launchSettingsPath);
    }

    public static void ApplyPaddleOcrEnvironment(MainPageUserSettingsState state)
    {
        SetEnvironment(PaddleDeviceEnvironmentVariable, state.SelectedPaddleDeviceKey);
        SetEnvironment(PaddlePreprocessEnvironmentVariable, state.PaddlePreprocessEnabled ? "true" : "false");
        SetEnvironment(PaddleUpscaleEnvironmentVariable, FixedPaddleUpscale);
        SetDoubleEnvironment(PaddleContrastEnvironmentVariable, state.PaddleContrastText, 0.1d, 4.0d);
        SetEnvironment(PaddleSharpenEnvironmentVariable, state.PaddleSharpenEnabled ? "true" : "false");
        SetDoubleEnvironment(PaddleTextDetThreshEnvironmentVariable, state.PaddleTextDetThreshText, 0.0d, 1.0d);
        SetDoubleEnvironment(PaddleTextDetBoxThreshEnvironmentVariable, state.PaddleTextDetBoxThreshText, 0.0d, 1.0d);
        SetDoubleEnvironment(PaddleTextDetUnclipRatioEnvironmentVariable, state.PaddleTextDetUnclipRatioText, 0.1d, 10.0d);
        SetIntEnvironment(PaddleTextDetLimitSideLenEnvironmentVariable, state.PaddleTextDetLimitSideLenText, 16, 4096);
        SetDoubleEnvironment(PaddleMinTextSizeEnvironmentVariable, state.PaddleMinTextSizeText, 0.0d, 200.0d);
        SetEnvironment(PaddleUseTextlineOrientationEnvironmentVariable, state.PaddleUseTextlineOrientation ? "true" : "false");
        SetEnvironment(PaddleUseDocUnwarpingEnvironmentVariable, state.PaddleUseDocUnwarping ? "true" : "false");
        SetEnvironment(
            PaddleWorkerCountEnvironmentVariable,
            NormalizePaddleWorkerCount(state.PaddleWorkerCount, state.SelectedPaddleDeviceKey).ToString(CultureInfo.InvariantCulture));
    }

    public static string FormatSettingNumber(double value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string ReadEnvironment(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    public static double? ReadEnvironmentDouble(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static double ReadDoubleSetting(string name, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public static bool? ReadEnvironmentBool(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null
        };
    }

    public static bool ReadBoolEnvironment(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => defaultValue
        };
    }

    public static int ReadPaddleWorkerCountSetting()
    {
        var value = Environment.GetEnvironmentVariable(PaddleWorkerCountEnvironmentVariable);
        var device = Environment.GetEnvironmentVariable(PaddleDeviceEnvironmentVariable);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? NormalizePaddleWorkerCount(parsed, device)
            : 1;
    }

    public static double? ParseOptionalDouble(string text, double minimum, double maximum)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    public static int? ParseOptionalInt(string text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    public static bool IsGpuDevice(string? device)
    {
        return !string.IsNullOrWhiteSpace(device)
            && device.Trim().StartsWith("gpu", StringComparison.OrdinalIgnoreCase);
    }

    public static SelectionOption ResolvePaddleDeviceOption(string? device, IReadOnlyList<SelectionOption> supportedOptions)
    {
        var normalizedKey = IsGpuDevice(device) ? "gpu:0" : "cpu";
        return supportedOptions.First(option => option.Key == normalizedKey);
    }

    public static int NormalizePaddleWorkerCount(int value, string? device)
    {
        var normalized = Math.Clamp(value, 1, 2);
        return IsGpuDevice(device) ? normalized : 1;
    }

    public static string ResolveSavedPath(string path, string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(settingsDirectory, expanded));
    }

    private static LanguageOption? ResolveLanguageOption(string? code, IReadOnlyList<LanguageOption> languageOptions)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return languageOptions.FirstOrDefault(option =>
            string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetDoubleEnvironment(string name, string text, double minimum, double maximum)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            SetEnvironment(name, null);
            return;
        }

        var normalized = Math.Clamp(value, minimum, maximum).ToString(CultureInfo.InvariantCulture);
        SetEnvironment(name, normalized);
    }

    private static void SetIntEnvironment(string name, string text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            SetEnvironment(name, null);
            return;
        }

        SetEnvironment(name, Math.Clamp(value, minimum, maximum).ToString(CultureInfo.InvariantCulture));
    }

    private static void SetEnvironment(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}
