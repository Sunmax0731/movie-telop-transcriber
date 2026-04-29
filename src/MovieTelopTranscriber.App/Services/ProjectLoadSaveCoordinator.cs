using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public static class ProjectLoadSaveCoordinator
{
    public static MainPageProjectLoadState BuildLoadState(
        string projectFilePath,
        ProjectBundleLoadResult loadResult,
        IReadOnlyList<LanguageOption> languageOptions)
    {
        var latestMetadata = loadResult.ExportPackage.SourceVideo;
        var latestFrameExtractionResult = loadResult.FrameExtractionResult;
        var latestFrameAnalyses = loadResult.FrameAnalyses;
        var latestSegments = loadResult.ExportPackage.Segments.ToArray();
        var latestFrameIntervalSeconds = loadResult.ExportPackage.ProcessingSettings.FrameIntervalSeconds;
        var timelineEdits = loadResult.ExportPackage.Edits.ToArray();
        var uiState = MainPageUserSettingsCoordinator.ResolveProjectUserInterfaceSettings(
            loadResult.Manifest.Ui,
            languageOptions);

        var outputDirectory = Path.GetDirectoryName(Path.Combine(loadResult.ExtractionDirectory, loadResult.Manifest.ExportPackagePath)) ?? "-";
        var jsonOutputPath = Path.Combine(loadResult.ExtractionDirectory, loadResult.Manifest.ExportPackagePath);
        var latestExport = new ExportWriteResult(
            outputDirectory,
            jsonOutputPath,
            Path.Combine(outputDirectory, "segments.csv"),
            Path.Combine(outputDirectory, "frames.csv"),
            Path.Combine(outputDirectory, "segments.srt"),
            Path.Combine(outputDirectory, "segments.vtt"),
            Path.Combine(outputDirectory, "segments.ass"));

        return new MainPageProjectLoadState(
            projectFilePath,
            loadResult.ExtractionDirectory,
            latestMetadata,
            latestFrameExtractionResult,
            latestFrameAnalyses,
            latestSegments,
            latestFrameIntervalSeconds,
            timelineEdits,
            uiState,
            loadResult.Manifest.SourceVideoPath,
            loadResult.FrameExtractionResult.RunDirectory,
            loadResult.ExportPackage.ProcessingSettings.OcrEngine,
            outputDirectory,
            jsonOutputPath,
            latestExport.SegmentsCsvPath,
            latestExport.FramesCsvPath,
            latestExport,
            File.Exists(loadResult.Manifest.SourceVideoPath));
    }

    public static MainPageProjectSaveState BuildSaveState(
        MainPageUserSettingsState userSettingsState,
        MainWindowLaunchSettings? mainWindowSettings,
        string displayedOcrEngine,
        string fallbackOcrEngine,
        string? selectedSegmentId,
        string? selectedDetectionId)
    {
        var uiSettings = new UserInterfaceSettings
        {
            Language = userSettingsState.SelectedLanguageCode,
            FrameIntervalSeconds = userSettingsState.FrameIntervalSeconds,
            OutputRootDirectory = string.IsNullOrWhiteSpace(userSettingsState.OutputRootDirectoryText)
                ? null
                : userSettingsState.OutputRootDirectoryText.Trim(),
            MainWindow = mainWindowSettings is null
                ? null
                : new MainWindowLaunchSettings
                {
                    Width = mainWindowSettings.Width,
                    Height = mainWindowSettings.Height
                }
        };

        return new MainPageProjectSaveState(
            userSettingsState.FrameIntervalSeconds ?? 1.0d,
            displayedOcrEngine == "-" ? fallbackOcrEngine : displayedOcrEngine,
            uiSettings,
            selectedSegmentId,
            selectedDetectionId);
    }
}
