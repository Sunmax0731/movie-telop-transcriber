namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageUserSettingsState(
    string SelectedLanguageCode,
    double? FrameIntervalSeconds,
    string OutputRootDirectoryText,
    bool PaddlePreprocessEnabled,
    string PaddleContrastText,
    bool PaddleSharpenEnabled,
    string PaddleTextDetThreshText,
    string PaddleTextDetBoxThreshText,
    string PaddleTextDetUnclipRatioText,
    string PaddleTextDetLimitSideLenText,
    string PaddleMinTextSizeText,
    bool PaddleUseTextlineOrientation,
    bool PaddleUseDocUnwarping,
    string SelectedPaddleDeviceKey,
    int PaddleWorkerCount);

public sealed record MainPageStoredUiState(
    LanguageOption? SelectedLanguageOption,
    double? FrameIntervalValue,
    string? FrameIntervalText,
    string? OutputRootDirectoryText);
