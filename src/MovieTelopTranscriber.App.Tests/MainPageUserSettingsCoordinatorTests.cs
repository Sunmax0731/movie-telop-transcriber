using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class MainPageUserSettingsCoordinatorTests
{
    [Fact]
    public void NormalizePaddleWorkerCount_CpuDevice_ForcesSingleWorker()
    {
        var normalized = MainPageUserSettingsCoordinator.NormalizePaddleWorkerCount(2, "cpu");

        Assert.Equal(1, normalized);
    }

    [Fact]
    public void ResolveSavedUserInterfaceSettings_RelativeOutputPath_UsesSettingsDirectory()
    {
        var uiSettings = new UserInterfaceSettings
        {
            Language = "ja",
            FrameIntervalSeconds = 0.5d,
            OutputRootDirectory = @".\work\runs"
        };

        var state = MainPageUserSettingsCoordinator.ResolveSavedUserInterfaceSettings(
            uiSettings,
            [new LanguageOption("ja", "日本語"), new LanguageOption("en", "English")],
            @"D:\Apps\MovieTelopTranscriber\app\movie-telop-transcriber.settings.json");

        Assert.Equal("ja", state.SelectedLanguageOption?.Code);
        Assert.Equal(0.5d, state.FrameIntervalValue);
        Assert.Equal("0.5", state.FrameIntervalText);
        Assert.Equal(@"D:\Apps\MovieTelopTranscriber\app\work\runs", state.OutputRootDirectoryText);
    }

    [Fact]
    public void PersistUserSettings_NormalizesWorkerCountAndOutputRoot()
    {
        var launchSettings = new AppLaunchSettings();
        var state = new MainPageUserSettingsState(
            SelectedLanguageCode: "ja",
            FrameIntervalSeconds: 1.25d,
            OutputRootDirectoryText: @" .\work\runs ",
            PaddlePreprocessEnabled: true,
            PaddleContrastText: "1.1",
            PaddleSharpenEnabled: true,
            PaddleTextDetThreshText: string.Empty,
            PaddleTextDetBoxThreshText: string.Empty,
            PaddleTextDetUnclipRatioText: string.Empty,
            PaddleTextDetLimitSideLenText: string.Empty,
            PaddleMinTextSizeText: "0",
            PaddleUseTextlineOrientation: false,
            PaddleUseDocUnwarping: false,
            SelectedPaddleDeviceKey: "cpu",
            PaddleWorkerCount: 2);

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"movie-telop-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var settingsPath = Path.Combine(tempDirectory, "movie-telop-transcriber.settings.json");

        try
        {
            MainPageUserSettingsCoordinator.PersistUserSettings(
                launchSettings,
                settingsPath,
                state,
                new MainWindowLaunchSettings { Width = 1600, Height = 900 });

            Assert.Equal("ja", launchSettings.Ui?.Language);
            Assert.Equal(1.25d, launchSettings.Ui?.FrameIntervalSeconds);
            Assert.Equal(@".\work\runs", launchSettings.Ui?.OutputRootDirectory);
            Assert.Equal(1, launchSettings.PaddleOcr?.WorkerCount);
            Assert.Equal("cpu", launchSettings.PaddleOcr?.Device);
            Assert.True(File.Exists(settingsPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
