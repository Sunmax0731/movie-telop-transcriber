namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageOcrWarmupSettings(
    string EngineName,
    string PythonPath,
    string ScriptPath,
    string Device,
    string Language,
    string MinScore,
    string NormalizeSmallKana,
    string Preprocess,
    string Upscale,
    string Contrast,
    string Sharpen,
    string TextDetThresh,
    string TextDetBoxThresh,
    string TextDetUnclipRatio,
    string TextDetLimitSideLen,
    string MinTextSize,
    string UseTextlineOrientation,
    string UseDocUnwarping,
    string WorkerCount);
