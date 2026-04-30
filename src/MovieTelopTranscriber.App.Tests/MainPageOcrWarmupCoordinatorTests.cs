using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class MainPageOcrWarmupCoordinatorTests
{
    [Fact]
    public void EnsureStarted_ReusesPendingTask_WhenSignatureMatches()
    {
        var settings = CreateSettings();
        var task = Task.FromResult(OcrWorkerWarmupResult.Skipped);
        var state = new MainPageOcrWarmupState(task, MainPageOcrWarmupCoordinator.CreateSettingsSignature(settings));
        var factoryCalls = 0;

        var updated = MainPageOcrWarmupCoordinator.EnsureStarted(
            state,
            settings,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult(OcrWorkerWarmupResult.Skipped);
            });

        Assert.Same(task, updated.PendingTask);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void EnsureStarted_RestartsPendingTask_WhenSignatureChanges()
    {
        var originalSettings = CreateSettings(device: "cpu");
        var updatedSettings = CreateSettings(device: "gpu:0");
        var state = new MainPageOcrWarmupState(
            Task.FromResult(OcrWorkerWarmupResult.Skipped),
            MainPageOcrWarmupCoordinator.CreateSettingsSignature(originalSettings));
        var factoryCalls = 0;

        var updated = MainPageOcrWarmupCoordinator.EnsureStarted(
            state,
            updatedSettings,
            _ =>
            {
                factoryCalls++;
                return Task.FromResult(OcrWorkerWarmupResult.Skipped);
            });

        Assert.NotNull(updated.PendingTask);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(MainPageOcrWarmupCoordinator.CreateSettingsSignature(updatedSettings), updated.PendingSettingsSignature);
    }

    [Fact]
    public async Task ResolveAsync_ClearsPendingTask_AfterCompletion()
    {
        var settings = CreateSettings();
        var waitingCalled = false;
        var state = MainPageOcrWarmupCoordinator.EnsureStarted(
            MainPageOcrWarmupState.Empty,
            settings,
            async _ =>
            {
                await Task.Delay(10);
                return OcrWorkerWarmupResult.Skipped;
            });

        var resolution = await MainPageOcrWarmupCoordinator.ResolveAsync(
            state,
            settings,
            () => waitingCalled = true,
            _ => Task.FromResult(OcrWorkerWarmupResult.Skipped));

        Assert.True(waitingCalled);
        Assert.Null(resolution.State.PendingTask);
        Assert.Equal("skipped", resolution.Result.Status);
    }

    [Fact]
    public async Task ResolveAsync_SkipsForNonPaddleEngine()
    {
        var settings = CreateSettings(engineName: "json-sidecar");

        var resolution = await MainPageOcrWarmupCoordinator.ResolveAsync(
            MainPageOcrWarmupState.Empty,
            settings,
            null,
            _ => Task.FromResult(OcrWorkerWarmupResult.Skipped));

        Assert.Equal("skipped", resolution.Result.Status);
        Assert.Null(resolution.State.PendingTask);
        Assert.Null(resolution.State.PendingSettingsSignature);
    }

    private static MainPageOcrWarmupSettings CreateSettings(
        string engineName = "paddleocr",
        string device = "cpu")
    {
        return new MainPageOcrWarmupSettings(
            engineName,
            @"D:\python\python.exe",
            @"D:\worker\paddle_ocr_worker.py",
            device,
            "ja",
            "0.5",
            "true",
            "true",
            "1.0",
            "1.1",
            "true",
            "",
            "",
            "",
            "",
            "0",
            "false",
            "false",
            "1");
    }
}
