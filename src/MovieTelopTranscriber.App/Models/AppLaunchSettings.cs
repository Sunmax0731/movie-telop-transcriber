namespace MovieTelopTranscriber.App.Models;

public sealed class AppLaunchSettings
{
    public string? OcrEngine { get; set; }

    public string? OcrWorkerPath { get; set; }

    public PaddleOcrLaunchSettings? PaddleOcr { get; set; }
}

public sealed class PaddleOcrLaunchSettings
{
    public string? PythonPath { get; set; }

    public string? ScriptPath { get; set; }

    public string? Device { get; set; }

    public string? Language { get; set; }

    public double? MinScore { get; set; }

    public bool? NormalizeSmallKana { get; set; }

    public bool? Preprocess { get; set; }

    public double? Contrast { get; set; }

    public bool? Sharpen { get; set; }

    public double? TextDetThresh { get; set; }

    public double? TextDetBoxThresh { get; set; }

    public double? TextDetUnclipRatio { get; set; }

    public int? TextDetLimitSideLen { get; set; }

    public bool? UseTextlineOrientation { get; set; }

    public bool? UseDocUnwarping { get; set; }
}
