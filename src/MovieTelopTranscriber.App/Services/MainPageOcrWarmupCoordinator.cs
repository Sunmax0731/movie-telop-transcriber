using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public static class MainPageOcrWarmupCoordinator
{
    public static MainPageOcrWarmupState EnsureStarted(
        MainPageOcrWarmupState state,
        MainPageOcrWarmupSettings settings,
        Func<CancellationToken, Task<OcrWorkerWarmupResult>> warmupFactory)
    {
        if (!string.Equals(settings.EngineName, "paddleocr", StringComparison.OrdinalIgnoreCase))
        {
            return state with { PendingTask = null, PendingSettingsSignature = null };
        }

        var signature = CreateSettingsSignature(settings);
        if (state.PendingTask is not null
            && string.Equals(state.PendingSettingsSignature, signature, StringComparison.Ordinal)
            && !state.PendingTask.IsCanceled
            && !state.PendingTask.IsFaulted)
        {
            return state;
        }

        return new MainPageOcrWarmupState(
            warmupFactory(CancellationToken.None),
            signature);
    }

    public static async Task<MainPageOcrWarmupResolution> ResolveAsync(
        MainPageOcrWarmupState state,
        MainPageOcrWarmupSettings settings,
        Action? onWaiting,
        Func<CancellationToken, Task<OcrWorkerWarmupResult>> warmupFactory,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(settings.EngineName, "paddleocr", StringComparison.OrdinalIgnoreCase))
        {
            return new MainPageOcrWarmupResolution(
                state with { PendingTask = null, PendingSettingsSignature = null },
                OcrWorkerWarmupResult.Skipped);
        }

        var ensuredState = EnsureStarted(state, settings, warmupFactory);
        if (ensuredState.PendingTask is null)
        {
            return new MainPageOcrWarmupResolution(ensuredState, OcrWorkerWarmupResult.Skipped);
        }

        if (!ensuredState.PendingTask.IsCompleted)
        {
            onWaiting?.Invoke();
        }

        var result = await ensuredState.PendingTask.WaitAsync(cancellationToken);
        return new MainPageOcrWarmupResolution(
            ensuredState with { PendingTask = null },
            result);
    }

    public static string CreateSettingsSignature(MainPageOcrWarmupSettings settings)
    {
        return string.Join(
            "\n",
            new[]
            {
                settings.EngineName,
                settings.PythonPath,
                settings.ScriptPath,
                settings.Device,
                settings.Language,
                settings.MinScore,
                settings.NormalizeSmallKana,
                settings.Preprocess,
                settings.Upscale,
                settings.Contrast,
                settings.Sharpen,
                settings.TextDetThresh,
                settings.TextDetBoxThresh,
                settings.TextDetUnclipRatio,
                settings.TextDetLimitSideLen,
                settings.MinTextSize,
                settings.UseTextlineOrientation,
                settings.UseDocUnwarping,
                settings.WorkerCount
            });
    }
}
