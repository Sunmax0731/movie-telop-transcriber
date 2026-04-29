using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Xunit;

namespace MovieTelopTranscriber.App.Tests;

public sealed class ProjectLoadSaveCoordinatorTests
{
    [Fact]
    public void BuildSaveState_UsesFallbackOcrEngine_WhenDisplayIsDash()
    {
        var userSettingsState = new MainPageUserSettingsState(
            SelectedLanguageCode: "ja",
            FrameIntervalSeconds: 1.5d,
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
            PaddleWorkerCount: 1);

        var saveState = ProjectLoadSaveCoordinator.BuildSaveState(
            userSettingsState,
            new MainWindowLaunchSettings { Width = 1600, Height = 900 },
            displayedOcrEngine: "-",
            fallbackOcrEngine: "paddleocr",
            selectedSegmentId: "seg-001",
            selectedDetectionId: "det-001");

        Assert.Equal(1.5d, saveState.FrameIntervalSeconds);
        Assert.Equal("paddleocr", saveState.OcrEngine);
        Assert.Equal(@".\work\runs", saveState.UiSettings.OutputRootDirectory);
        Assert.Equal(1600, saveState.UiSettings.MainWindow?.Width);
        Assert.Equal("seg-001", saveState.SelectedSegmentId);
    }

    [Fact]
    public void BuildLoadState_MapsBundleResultToMainPageState()
    {
        var metadata = new VideoMetadata(
            @"D:\videos\sample.mp4",
            "sample.mp4",
            5000,
            1920,
            1080,
            29.97,
            "h264");
        var frame = new ExtractedFrameRecord(0, 1000, @"D:\cache\frame-0000.png");
        var frameExtractionResult = new FrameExtractionResult(
            "run-001",
            @"D:\cache\run-001",
            @"D:\cache\run-001\frames",
            [frame]);
        var frameAnalyses = new[]
        {
            new FrameAnalysisResult(
                frame,
                new OcrWorkerResponse("ocr-000000-00001000ms", "success", 0, 1000, Array.Empty<OcrDetectionRecord>(), null),
                new AttributeAnalysisResult(0, 1000, "success", Array.Empty<TelopAttributeRecord>(), null),
                new OcrFramePerformanceRecord(0, 1000, false, "project-load", 0, 0, 0, 0, 0, 0, 0, 0, 0))
        };
        var segments = new[]
        {
            new SegmentRecord("seg-001", 1000, 2000, "テスト", "caption_band", null, 32, "px", "#FFFFFF", "#000000", null, 0.9, 1)
        };
        var edits = new[]
        {
            new EditOperationRecord("edit", "seg-001", null, null, "旧", "新", 1000, 2000, DateTimeOffset.Parse("2026-04-30T12:00:00+09:00"), null)
        };
        var exportPackage = new ExportPackage(
            "1.0.0",
            metadata,
            new ProcessingSettingsRecord(1.0d, "paddleocr", false),
            [new FrameExportRecord(0, 1000, "frame-0000.png", Array.Empty<TelopAttributeRecord>())],
            segments,
            edits,
            new RunMetadataRecord(DateTimeOffset.Parse("2026-04-30T12:00:00+09:00"), "0.1.4", null, 0, 0));
        var manifest = new ProjectBundleManifest(
            "1.0.0",
            DateTimeOffset.Parse("2026-04-30T12:00:00+09:00"),
            "0.1.4",
            metadata.FilePath,
            true,
            "run-001",
            @"bundle\run-001\output\segments.json",
            @"bundle\run-001\frames",
            @"bundle\run-001\ocr",
            @"bundle\run-001\attributes",
            "seg-001",
            "det-001",
            new UserInterfaceSettings { Language = "ja", FrameIntervalSeconds = 1.0d, OutputRootDirectory = @".\work\runs" });
        var loadResult = new ProjectBundleLoadResult(
            @"D:\projects\sample.mtproj",
            @"D:\cache\project-load",
            manifest,
            exportPackage,
            frameExtractionResult,
            frameAnalyses);

        var state = ProjectLoadSaveCoordinator.BuildLoadState(
            @"D:\projects\sample.mtproj",
            loadResult,
            [new LanguageOption("ja", "日本語"), new LanguageOption("en", "English")]);

        Assert.Equal(@"D:\projects\sample.mtproj", state.CurrentProjectFilePath);
        Assert.Equal("paddleocr", state.OcrEngineText);
        Assert.Single(state.LatestSegments);
        Assert.Single(state.TimelineEdits);
        Assert.Equal("ja", state.UiState.SelectedLanguageOption?.Code);
        Assert.Equal(@"D:\cache\project-load\bundle\run-001\output", state.ExportDirectoryText);
        Assert.EndsWith(@"segments.json", state.JsonOutputPathText);
    }
}
