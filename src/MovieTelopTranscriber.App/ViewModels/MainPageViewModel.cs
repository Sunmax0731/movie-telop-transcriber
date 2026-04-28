using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Windows.Storage.Pickers;

namespace MovieTelopTranscriber.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private enum AnalysisStartStage
    {
        FrameExtraction,
        Ocr,
        Export
    }

    private const string PaddlePreprocessEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_PREPROCESS";
    private const string PaddleUpscaleEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_UPSCALE";
    private const string PaddleContrastEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_CONTRAST";
    private const string PaddleSharpenEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_SHARPEN";
    private const string PaddleTextDetThreshEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH";
    private const string PaddleTextDetBoxThreshEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH";
    private const string PaddleTextDetUnclipRatioEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO";
    private const string PaddleTextDetLimitSideLenEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN";
    private const string PaddleUseTextlineOrientationEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION";
    private const string PaddleUseDocUnwarpingEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING";

    private readonly OpenCvVideoProcessingService _videoProcessingService = new();
    private readonly TelopFrameAnalysisService _frameAnalysisService = new();
    private readonly TelopSegmentMerger _segmentMerger = new();
    private readonly ExportPackageWriter _exportPackageWriter = new();
    private readonly RunLogWriter _runLogWriter = new();
    private static readonly LanguageOption[] SupportedLanguageOptions =
    [
        new("ja", "日本語"),
        new("en", "English"),
        new("zh", "中文"),
        new("ko", "한국어")
    ];

    private IReadOnlyList<FrameAnalysisResult> _latestFrameAnalyses = Array.Empty<FrameAnalysisResult>();
    private IReadOnlyList<SegmentRecord> _latestSegments = Array.Empty<SegmentRecord>();
    private VideoMetadata? _latestMetadata;
    private FrameExtractionResult? _latestFrameExtractionResult;
    private ExportWriteResult? _latestExport;
    private double _latestFrameIntervalSeconds = 1.0d;
    private bool _isSynchronizingSelection;

    public MainPageViewModel()
    {
        SettingItems = new ObservableCollection<SettingItem>();
        LanguageOptions = new ObservableCollection<LanguageOption>(SupportedLanguageOptions);
        InfoCards = new ObservableCollection<InfoCardItem>();
        TimelineSegments = new ObservableCollection<TimelineSegment>();
        ResultRows = new ObservableCollection<ResultRow>();
        PreviewDetections = new ObservableCollection<PreviewDetectionOverlay>();

        SelectedLanguageOption = ResolveDefaultLanguageOption();
        UiText = LocalizedUiText.ForLanguage(SelectedLanguageOption.Code);
        ApplyPaddleOcrEnvironment();
        RefreshStaticCollections();
        ResetDynamicCollections();
    }

    public ObservableCollection<SettingItem> SettingItems { get; }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    public ObservableCollection<InfoCardItem> InfoCards { get; }

    public ObservableCollection<TimelineSegment> TimelineSegments { get; }

    public ObservableCollection<ResultRow> ResultRows { get; }

    public ObservableCollection<PreviewDetectionOverlay> PreviewDetections { get; }

    public event EventHandler? SettingsWindowRequested;

    [ObservableProperty]
    public partial LocalizedUiText UiText { get; set; } = LocalizedUiText.English;

    [ObservableProperty]
    public partial string VideoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupportedFormats { get; set; } = ".mp4, .mov, .avi, .mkv, .wmv";

    [ObservableProperty]
    public partial string PreviewState { get; set; } = "Video not loaded";

    [ObservableProperty]
    public partial string? PreviewImagePath { get; set; }

    [ObservableProperty]
    public partial int PreviewImageWidth { get; set; }

    [ObservableProperty]
    public partial int PreviewImageHeight { get; set; }

    [ObservableProperty]
    public partial string PreviewDetailText { get; set; } = "No frame selected.";

    [ObservableProperty]
    public partial string ActivityMessage { get; set; } = "Select a video file to load metadata and extract frames.";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "App foundation is ready. Video input and frame extraction are available on this screen.";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool ShowTimelineSelection { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool CanInteract => !IsBusy;

    [ObservableProperty]
    public partial string FrameIntervalText { get; set; } = "1.0";

    [ObservableProperty]
    public partial bool PaddlePreprocessEnabled { get; set; } = ReadBoolEnvironment(PaddlePreprocessEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleUpscaleText { get; set; } = ReadEnvironment(PaddleUpscaleEnvironmentVariable, "1.5");

    [ObservableProperty]
    public partial string PaddleContrastText { get; set; } = ReadEnvironment(PaddleContrastEnvironmentVariable, "1.1");

    [ObservableProperty]
    public partial bool PaddleSharpenEnabled { get; set; } = ReadBoolEnvironment(PaddleSharpenEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleTextDetThreshText { get; set; } = ReadEnvironment(PaddleTextDetThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial string PaddleTextDetBoxThreshText { get; set; } = ReadEnvironment(PaddleTextDetBoxThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial string PaddleTextDetUnclipRatioText { get; set; } = ReadEnvironment(PaddleTextDetUnclipRatioEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial string PaddleTextDetLimitSideLenText { get; set; } = ReadEnvironment(PaddleTextDetLimitSideLenEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial bool PaddleUseTextlineOrientation { get; set; } = ReadBoolEnvironment(PaddleUseTextlineOrientationEnvironmentVariable, false);

    [ObservableProperty]
    public partial bool PaddleUseDocUnwarping { get; set; } = ReadBoolEnvironment(PaddleUseDocUnwarpingEnvironmentVariable, false);

    [ObservableProperty]
    public partial LanguageOption SelectedLanguageOption { get; set; } = SupportedLanguageOptions[1];

    [ObservableProperty]
    public partial string DurationText { get; set; } = "-";

    [ObservableProperty]
    public partial string ResolutionText { get; set; } = "-";

    [ObservableProperty]
    public partial string FpsText { get; set; } = "-";

    [ObservableProperty]
    public partial string CodecText { get; set; } = "-";

    [ObservableProperty]
    public partial string WorkDirectoryText { get; set; } = "-";

    [ObservableProperty]
    public partial string OcrEngineText { get; set; } = "-";

    [ObservableProperty]
    public partial string ExportDirectoryText { get; set; } = "-";

    [ObservableProperty]
    public partial string JsonOutputPathText { get; set; } = "-";

    [ObservableProperty]
    public partial string SegmentsCsvOutputPathText { get; set; } = "-";

    [ObservableProperty]
    public partial string FramesCsvOutputPathText { get; set; } = "-";

    [ObservableProperty]
    public partial string LogDirectoryText { get; set; } = "-";

    [ObservableProperty]
    public partial string RunLogPathText { get; set; } = "-";

    [ObservableProperty]
    public partial string RunSummaryPathText { get; set; } = "-";

    [ObservableProperty]
    public partial string LastFailedStageText { get; set; } = "-";

    [ObservableProperty]
    public partial string LastErrorCodeText { get; set; } = "-";

    [ObservableProperty]
    public partial string LastErrorMessageText { get; set; } = "-";

    [ObservableProperty]
    public partial TimelineSegment? SelectedTimelineSegment { get; set; }

    [ObservableProperty]
    public partial ResultRow? SelectedResultRow { get; set; }

    public string SelectedSegmentSummary =>
        SelectedTimelineSegment is null
            ? "Nothing selected"
            : $"{SelectedTimelineSegment.RangeLabel} / {SelectedTimelineSegment.Text} / {SelectedTimelineSegment.StyleSummary}";

    partial void OnSelectedTimelineSegmentChanged(TimelineSegment? value)
    {
        OnPropertyChanged(nameof(SelectedSegmentSummary));
        if (_isSynchronizingSelection)
        {
            return;
        }

        UpdatePreviewFromTimelineSelection(value);
    }

    partial void OnSelectedResultRowChanged(ResultRow? value)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        UpdatePreviewFromResultSelection(value);
    }

    partial void OnShowTimelineSelectionChanged(bool value)
    {
        if (value)
        {
            UpdatePreviewFromTimelineSelection(SelectedTimelineSegment);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnFrameIntervalTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddlePreprocessEnabledChanged(bool value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleUpscaleTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleContrastTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleSharpenEnabledChanged(bool value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetThreshTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetBoxThreshTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetUnclipRatioTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetLimitSideLenTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleUseTextlineOrientationChanged(bool value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleUseDocUnwarpingChanged(bool value)
    {
        RefreshStaticCollections();
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption value)
    {
        ApplyLanguageCulture(value);
        UiText = LocalizedUiText.ForLanguage(value.Code);
        RefreshStaticCollections();
        RefreshInfoCards(
            _latestMetadata,
            _latestFrameExtractionResult?.Frames.Count ?? 0,
            _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count),
            _latestSegments.Count);
    }

    [RelayCommand]
    private async Task SelectVideoAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
        foreach (var extension in SupportedFormats.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadVideoAsync(file.Path);
    }

    [RelayCommand]
    private async Task StartAnalysisAsync()
    {
        await RunAnalysisAsync(AnalysisStartStage.FrameExtraction);
    }

    [RelayCommand]
    private async Task RerunFrameExtractionAsync()
    {
        await RunAnalysisAsync(AnalysisStartStage.FrameExtraction);
    }

    [RelayCommand]
    private async Task RerunOcrAsync()
    {
        await RunAnalysisAsync(AnalysisStartStage.Ocr);
    }

    [RelayCommand]
    private async Task RerunExportAsync()
    {
        await RunAnalysisAsync(AnalysisStartStage.Export);
    }

    private async Task RunAnalysisAsync(AnalysisStartStage startStage)
    {
        if (IsBusy)
        {
            return;
        }

        if (startStage == AnalysisStartStage.FrameExtraction
            && (string.IsNullOrWhiteSpace(VideoPath) || !File.Exists(VideoPath)))
        {
            SetPipelineFailure("Input validation", "VIDEO_NOT_FOUND", "Select a valid video file before running analysis.");
            return;
        }

        if (startStage != AnalysisStartStage.FrameExtraction
            && (_latestMetadata is null || _latestFrameExtractionResult is null))
        {
            SetPipelineFailure("Re-run preparation", "PREVIOUS_RUN_NOT_FOUND", "Run frame extraction before re-running a later stage.");
            return;
        }

        if (startStage == AnalysisStartStage.Export && _latestFrameAnalyses.Count == 0)
        {
            SetPipelineFailure("Re-run preparation", "ANALYSIS_RESULT_NOT_FOUND", "Run OCR before exporting results again.");
            return;
        }

        ApplyPaddleOcrEnvironment();
        ClearPipelineFailure();
        IsBusy = true;
        ProgressValue = 0;
        PreviewState = startStage == AnalysisStartStage.Export ? "Exporting results" : "Running analysis";
        StatusMessage = CreateStartStatus(startStage);
        ActivityMessage = "Pipeline stage is running.";
        var currentStage = "Input validation";

        try
        {
            var startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            var intervalSeconds = startStage == AnalysisStartStage.FrameExtraction
                ? ParseFrameIntervalSeconds()
                : _latestFrameIntervalSeconds;
            VideoMetadata metadata;
            FrameExtractionResult result;

            if (startStage == AnalysisStartStage.FrameExtraction)
            {
                currentStage = "Video metadata";
                metadata = await _videoProcessingService.ReadMetadataAsync(VideoPath);
                _latestMetadata = metadata;
                _latestFrameIntervalSeconds = intervalSeconds;

                currentStage = "Frame extraction";
                var progress = new Progress<double>(value => ProgressValue = value * 0.6d);
                result = await _videoProcessingService.ExtractFramesAsync(metadata, intervalSeconds, progress);
                _latestFrameExtractionResult = result;
            }
            else
            {
                metadata = _latestMetadata!;
                result = _latestFrameExtractionResult!;
                ProgressValue = startStage == AnalysisStartStage.Ocr ? 10d : 70d;
            }

            WorkDirectoryText = result.RunDirectory;
            OcrEngineText = _frameAnalysisService.EngineName;

            if (startStage != AnalysisStartStage.Export)
            {
                currentStage = "OCR";
                PreviewState = "Running OCR";
                ActivityMessage = "OCR worker is processing extracted frames.";
                var ocrProgress = new Progress<double>(value => ProgressValue = 60d + (value * 0.3d));
                _latestFrameAnalyses = await _frameAnalysisService.AnalyzeFramesAsync(result, ocrProgress);

                currentStage = "Attribute analysis";
                _latestSegments = _segmentMerger.Merge(_latestFrameAnalyses, intervalSeconds);
            }

            TimelineSegments.Clear();
            ResultRows.Clear();

            var detectionCount = _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count);
            var errorCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "error");
            var warningCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "warning");
            PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);

            currentStage = "Output";
            stopwatch.Stop();
            _latestExport = await _exportPackageWriter.WriteAsync(
                metadata,
                result,
                _latestFrameAnalyses,
                _latestSegments,
                intervalSeconds,
                OcrEngineText,
                stopwatch.ElapsedMilliseconds,
                warningCount,
                errorCount);
            ExportDirectoryText = _latestExport.OutputDirectory;
            JsonOutputPathText = _latestExport.JsonPath;
            SegmentsCsvOutputPathText = _latestExport.SegmentsCsvPath;
            FramesCsvOutputPathText = _latestExport.FramesCsvPath;

            currentStage = "Logging";
            var logWriteResult = await _runLogWriter.WriteSuccessAsync(
                result,
                metadata,
                _latestExport,
                intervalSeconds,
                OcrEngineText,
                startedAt,
                DateTimeOffset.Now,
                result.Frames.Count,
                detectionCount,
                _latestSegments.Count,
                warningCount,
                errorCount);
            LogDirectoryText = logWriteResult.LogsDirectory;
            RunLogPathText = logWriteResult.LogPath;
            RunSummaryPathText = logWriteResult.SummaryPath;
            ProgressValue = 100d;

            SelectFirstPreviewSelection();
            if (PreviewImagePath is null)
            {
                PreviewState = _latestSegments.Count > 0 ? $"Created {_latestSegments.Count} segments" : "No telop segments";
            }

            ActivityMessage = $"Saved {result.Frames.Count} frames, {detectionCount} detections, {_latestSegments.Count} segments, and run logs under {result.RunDirectory}.";
            StatusMessage = errorCount == 0
                ? $"Analysis and export completed. Run ID: {result.RunId}"
                : $"Analysis exported with {errorCount} OCR error(s). Run ID: {result.RunId}";

            RefreshInfoCards(metadata, result.Frames.Count, detectionCount, _latestSegments.Count);
        }
        catch (Exception ex)
        {
            SetPipelineFailure(currentStage, "PIPELINE_STAGE_FAILED", ex.Message);
            PreviewState = "Analysis failed";
            ActivityMessage = ex.Message;
            StatusMessage = $"Pipeline failed at {currentStage}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSettingsOutput()
    {
        SettingsWindowRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = _latestExport is null
            ? "Settings and output window is open. Run analysis to populate output paths."
            : $"Settings and output window is open. Latest export: {_latestExport.JsonPath}";
    }

    private async Task LoadVideoAsync(string path)
    {
        IsBusy = true;
        ProgressValue = 0;
        PreviewState = "Loading metadata";
        ActivityMessage = "Reading video metadata.";

        try
        {
            VideoPath = path;
            var metadata = await _videoProcessingService.ReadMetadataAsync(path);
            DurationText = FormatTimestamp(metadata.DurationMs);
            ResolutionText = $"{metadata.Width} x {metadata.Height}";
            FpsText = $"{metadata.Fps:F3}";
            CodecText = metadata.Codec;
            WorkDirectoryText = "-";
            OcrEngineText = _frameAnalysisService.EngineName;
            ExportDirectoryText = "-";
            JsonOutputPathText = "-";
            SegmentsCsvOutputPathText = "-";
            FramesCsvOutputPathText = "-";
            LogDirectoryText = "-";
            RunLogPathText = "-";
            RunSummaryPathText = "-";
            _latestMetadata = metadata;
            _latestFrameExtractionResult = null;
            _latestFrameAnalyses = Array.Empty<FrameAnalysisResult>();
            _latestSegments = Array.Empty<SegmentRecord>();
            _latestExport = null;
            _latestFrameIntervalSeconds = ParseFrameIntervalSeconds();
            ClearPipelineFailure();
            PreviewState = "Ready";
            StatusMessage = $"Loaded {metadata.FileName}";
            ActivityMessage = "Metadata loaded. You can now run frame extraction.";

            RefreshInfoCards(metadata, 0, 0, 0);
            TimelineSegments.Clear();
            ResultRows.Clear();
            TimelineSegments.Add(new TimelineSegment("preview", metadata.FileName, "metadata loaded"));
            ResultRows.Add(new ResultRow("metadata", "Video", metadata.FileName, $"{ResolutionText} / {FpsText} fps / {CodecText}"));
            SelectFirstPreviewSelection();
            ClearPreview("Ready", "Run frame extraction to display frame preview.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load video metadata.";
            ActivityMessage = ex.Message;
            PreviewState = "Load failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshStaticCollections()
    {
        SettingItems.Clear();
        SettingItems.Add(new SettingItem(UiText.FrameIntervalSettingLabel, $"{ParseFrameIntervalSeconds():F1} sec", UiText.FrameIntervalSettingDescription));
        SettingItems.Add(new SettingItem(UiText.Language, SelectedLanguageOption.DisplayName, UiText.LanguageSettingDescription));
        SettingItems.Add(new SettingItem(UiText.OcrEngineSettingLabel, _frameAnalysisService.EngineName, UiText.OcrEngineSettingDescription));
        SettingItems.Add(new SettingItem("PaddleOCR 前処理", FormatPaddlePreprocessSummary(), "フルフレームを自動で拡大、コントラスト補正、シャープ化してから OCR に渡します。"));
        SettingItems.Add(new SettingItem("PaddleOCR 検出", FormatPaddleDetectionSummary(), "検出閾値や向き補正を設定できます。空欄の値は PaddleOCR の既定値を使います。"));
        SettingItems.Add(new SettingItem(UiText.OutputSettingLabel, "work/runs/<run_id>", UiText.OutputSettingDescription));
    }

    private void ResetDynamicCollections()
    {
        OcrEngineText = _frameAnalysisService.EngineName;
        ExportDirectoryText = "-";
        JsonOutputPathText = "-";
        SegmentsCsvOutputPathText = "-";
        FramesCsvOutputPathText = "-";
        LogDirectoryText = "-";
        RunLogPathText = "-";
        RunSummaryPathText = "-";
        ClearPipelineFailure();
        RefreshInfoCards(null, 0, 0, 0);
        TimelineSegments.Clear();
        ResultRows.Clear();
        TimelineSegments.Add(new TimelineSegment("timeline", "No frames yet", "load a video to begin"));
        ResultRows.Add(new ResultRow("result", "Status", "No extracted frames", "Run frame extraction to populate this list."));
        SelectFirstPreviewSelection();
        ClearPreview("Video not loaded", "Select a video file to display frame preview.");
    }

    private void RefreshInfoCards(VideoMetadata? metadata, int frameCount, int detectionCount, int segmentCount)
    {
        InfoCards.Clear();
        InfoCards.Add(new InfoCardItem(UiText.VideoInfoTitle, metadata?.FileName ?? "Not selected", UiText.VideoInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.FramesInfoTitle, frameCount.ToString(), UiText.FramesInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.OcrInfoTitle, $"{detectionCount} detections", OcrEngineText));
        InfoCards.Add(new InfoCardItem(UiText.SegmentsInfoTitle, segmentCount.ToString(), UiText.SegmentsInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.ExportInfoTitle, ExportDirectoryText, UiText.ExportInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.LogInfoTitle, LogDirectoryText, UiText.LogInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.WorkInfoTitle, WorkDirectoryText, UiText.WorkInfoDescription));
    }

    private static string CreateStartStatus(AnalysisStartStage startStage)
    {
        return startStage switch
        {
            AnalysisStartStage.FrameExtraction => "Frame extraction started.",
            AnalysisStartStage.Ocr => "OCR re-run started.",
            AnalysisStartStage.Export => "Export re-run started.",
            _ => "Analysis started."
        };
    }

    private void ClearPipelineFailure()
    {
        LastFailedStageText = "-";
        LastErrorCodeText = "-";
        LastErrorMessageText = "-";
    }

    private void SetPipelineFailure(string stage, string code, string message)
    {
        LastFailedStageText = stage;
        LastErrorCodeText = code;
        LastErrorMessageText = message;
        ActivityMessage = message;
        StatusMessage = $"{stage} failed: {message}";
    }

    private void PopulateTimelineAndResults(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<SegmentRecord> segments)
    {
        if (segments.Count > 0)
        {
            foreach (var segment in segments)
            {
                var rangeLabel = $"{FormatTimestamp(segment.StartTimestampMs)} - {FormatTimestamp(segment.EndTimestampMs)}";
                var previewAnalysis = FindPreviewAnalysisForSegment(segment, frameAnalyses);
                var frameIndex = previewAnalysis?.Frame.FrameIndex;
                var timestampMs = previewAnalysis?.Frame.TimestampMs;
                TimelineSegments.Add(new TimelineSegment(
                    rangeLabel,
                    segment.Text,
                    FormatSegmentStyleSummary(segment),
                    frameIndex,
                    timestampMs,
                    segment.SegmentId));
                ResultRows.Add(new ResultRow(
                    rangeLabel,
                    segment.TextType,
                    segment.Text,
                    FormatSegmentDetail(segment),
                    frameIndex,
                    timestampMs,
                    segment.SegmentId));
            }

            return;
        }

        foreach (var analysis in frameAnalyses)
        {
            var timeLabel = FormatTimestamp(analysis.Frame.TimestampMs);
            if (analysis.Ocr.Status == "error")
            {
                TimelineSegments.Add(new TimelineSegment(
                    timeLabel,
                    $"Frame {analysis.Frame.FrameIndex:D6}",
                    "OCR error",
                    analysis.Frame.FrameIndex,
                    analysis.Frame.TimestampMs));
                ResultRows.Add(new ResultRow(
                    timeLabel,
                    "Error",
                    analysis.Ocr.Error?.Code ?? "OCR_PROCESS_FAILED",
                    analysis.Ocr.Error?.Message ?? "OCR worker failed.",
                    analysis.Frame.FrameIndex,
                    analysis.Frame.TimestampMs));
                continue;
            }

            TimelineSegments.Add(new TimelineSegment(
                timeLabel,
                $"Frame {analysis.Frame.FrameIndex:D6}",
                "No telop detected",
                analysis.Frame.FrameIndex,
                analysis.Frame.TimestampMs));
            ResultRows.Add(new ResultRow(
                timeLabel,
                "OCR",
                "No text detected",
                Path.GetFileName(analysis.Frame.ImagePath),
                analysis.Frame.FrameIndex,
                analysis.Frame.TimestampMs));
        }
    }

    private void SelectFirstPreviewSelection()
    {
        _isSynchronizingSelection = true;
        try
        {
            SelectedTimelineSegment = TimelineSegments.FirstOrDefault();
            SelectedResultRow = ResultRows.FirstOrDefault();
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        UpdatePreviewFromTimelineSelection(SelectedTimelineSegment);
    }

    private void UpdatePreviewFromTimelineSelection(TimelineSegment? selection)
    {
        if (selection is null)
        {
            ClearPreview("No frame selected", "Select a timeline row or result row to display a frame.");
            return;
        }

        SyncSelectedResultRow(selection);
        UpdatePreview(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.Text);
    }

    private void UpdatePreviewFromResultSelection(ResultRow? selection)
    {
        if (selection is null)
        {
            ClearPreview("No frame selected", "Select a timeline row or result row to display a frame.");
            return;
        }

        SyncSelectedTimelineSegment(selection);
        UpdatePreview(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            selection.Text,
            selection.DetectionId);
    }

    private void SyncSelectedResultRow(TimelineSegment selection)
    {
        var match = ResultRows.FirstOrDefault(row => SelectionKeysMatch(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId));
        if (match is null || ReferenceEquals(match, SelectedResultRow))
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedResultRow = match;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void SyncSelectedTimelineSegment(ResultRow selection)
    {
        var match = TimelineSegments.FirstOrDefault(row => SelectionKeysMatch(
            selection.FrameIndex,
            selection.TimestampMs,
            selection.SegmentId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId));
        if (match is null || ReferenceEquals(match, SelectedTimelineSegment))
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedTimelineSegment = match;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private static bool SelectionKeysMatch(
        int? leftFrameIndex,
        long? leftTimestampMs,
        string? leftSegmentId,
        int? rightFrameIndex,
        long? rightTimestampMs,
        string? rightSegmentId)
    {
        if (!string.IsNullOrWhiteSpace(leftSegmentId) || !string.IsNullOrWhiteSpace(rightSegmentId))
        {
            return string.Equals(leftSegmentId, rightSegmentId, StringComparison.Ordinal);
        }

        return leftFrameIndex == rightFrameIndex && leftTimestampMs == rightTimestampMs;
    }

    private void UpdatePreview(
        int? frameIndex,
        long? timestampMs,
        string? segmentId,
        string? selectedText,
        string? detectionId = null)
    {
        var analysis = ResolvePreviewAnalysis(frameIndex, timestampMs, selectedText);
        if (analysis is null)
        {
            ClearPreview("Frame image is not available", "Run frame extraction and OCR to display the selected frame.");
            return;
        }

        PreviewImagePath = analysis.Frame.ImagePath;
        PreviewImageWidth = _latestMetadata?.Width ?? 0;
        PreviewImageHeight = _latestMetadata?.Height ?? 0;

        PreviewDetections.Clear();
        foreach (var detection in analysis.Ocr.Detections)
        {
            var highlighted = IsHighlightedDetection(detection, detectionId, segmentId, selectedText);
            PreviewDetections.Add(new PreviewDetectionOverlay(
                detection.DetectionId,
                detection.Text,
                detection.Confidence,
                detection.BoundingBox,
                highlighted));
        }

        var detectionCount = analysis.Ocr.Detections.Count;
        var timeLabel = FormatTimestamp(analysis.Frame.TimestampMs);
        PreviewState = detectionCount == 0
            ? $"Frame {analysis.Frame.FrameIndex:D6} / No text detected"
            : $"Frame {analysis.Frame.FrameIndex:D6} / {detectionCount} detection(s)";
        PreviewDetailText = $"{timeLabel} / Frame {analysis.Frame.FrameIndex:D6} / {Path.GetFileName(analysis.Frame.ImagePath)}";
    }

    private void ClearPreview(string state, string detail)
    {
        PreviewImagePath = null;
        PreviewImageWidth = 0;
        PreviewImageHeight = 0;
        PreviewDetections.Clear();
        PreviewState = state;
        PreviewDetailText = detail;
    }

    private FrameAnalysisResult? ResolvePreviewAnalysis(int? frameIndex, long? timestampMs, string? selectedText)
    {
        if (_latestFrameAnalyses.Count == 0)
        {
            return null;
        }

        if (frameIndex is not null)
        {
            var exactFrame = _latestFrameAnalyses.FirstOrDefault(analysis => analysis.Frame.FrameIndex == frameIndex);
            if (exactFrame is not null)
            {
                return exactFrame;
            }
        }

        if (timestampMs is not null)
        {
            return _latestFrameAnalyses
                .OrderBy(analysis => Math.Abs(analysis.Frame.TimestampMs - timestampMs.Value))
                .FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(selectedText))
        {
            var matchingText = _latestFrameAnalyses.FirstOrDefault(analysis =>
                analysis.Ocr.Detections.Any(detection => TextsMatch(detection.Text, selectedText)));
            if (matchingText is not null)
            {
                return matchingText;
            }
        }

        return _latestFrameAnalyses.FirstOrDefault();
    }

    private static bool IsHighlightedDetection(
        OcrDetectionRecord detection,
        string? detectionId,
        string? segmentId,
        string? selectedText)
    {
        if (!string.IsNullOrWhiteSpace(detectionId))
        {
            return string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(segmentId) && !string.IsNullOrWhiteSpace(selectedText))
        {
            return TextsMatch(detection.Text, selectedText);
        }

        return false;
    }

    private static FrameAnalysisResult? FindPreviewAnalysisForSegment(
        SegmentRecord segment,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses)
    {
        var candidates = frameAnalyses
            .Where(analysis =>
                analysis.Frame.TimestampMs >= segment.StartTimestampMs
                && analysis.Frame.TimestampMs <= segment.EndTimestampMs)
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = frameAnalyses
                .OrderBy(analysis => Math.Abs(analysis.Frame.TimestampMs - segment.StartTimestampMs))
                .Take(1)
                .ToArray();
        }

        return candidates.FirstOrDefault(analysis =>
                analysis.Ocr.Detections.Any(detection => TextsMatch(detection.Text, segment.Text)))
            ?? candidates.FirstOrDefault(analysis => analysis.Ocr.Detections.Count > 0)
            ?? candidates.FirstOrDefault();
    }

    private static bool TextsMatch(string left, string right)
    {
        return string.Equals(NormalizeTextForSelection(left), NormalizeTextForSelection(right), StringComparison.Ordinal);
    }

    private static string NormalizeTextForSelection(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatSegmentStyleSummary(SegmentRecord segment)
    {
        var fontSize = segment.FontSize is null ? "size unknown" : $"{segment.FontSize:F1}{segment.FontSizeUnit}";
        return $"{segment.TextType} / {fontSize} / {segment.SourceFrameCount} frame(s)";
    }

    private static string FormatSegmentDetail(SegmentRecord segment)
    {
        var confidence = segment.Confidence is null ? "confidence unknown" : $"confidence {segment.Confidence:P1}";
        var colors = string.Join(
            " / ",
            new[] { segment.TextColor, segment.StrokeColor, segment.BackgroundColor }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(colors)
            ? confidence
            : $"{confidence} / {colors}";
    }

    private static string FormatTimestamp(long timestampMs)
    {
        var ts = TimeSpan.FromMilliseconds(timestampMs);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }

    private double ParseFrameIntervalSeconds()
    {
        return double.TryParse(FrameIntervalText, out var seconds) && seconds > 0
            ? seconds
            : 1.0d;
    }

    private void ApplyPaddleOcrEnvironment()
    {
        SetEnvironment(PaddlePreprocessEnvironmentVariable, PaddlePreprocessEnabled ? "true" : "false");
        SetDoubleEnvironment(PaddleUpscaleEnvironmentVariable, PaddleUpscaleText, 1.0d, 4.0d);
        SetDoubleEnvironment(PaddleContrastEnvironmentVariable, PaddleContrastText, 0.1d, 4.0d);
        SetEnvironment(PaddleSharpenEnvironmentVariable, PaddleSharpenEnabled ? "true" : "false");
        SetDoubleEnvironment(PaddleTextDetThreshEnvironmentVariable, PaddleTextDetThreshText, 0.0d, 1.0d);
        SetDoubleEnvironment(PaddleTextDetBoxThreshEnvironmentVariable, PaddleTextDetBoxThreshText, 0.0d, 1.0d);
        SetDoubleEnvironment(PaddleTextDetUnclipRatioEnvironmentVariable, PaddleTextDetUnclipRatioText, 0.1d, 10.0d);
        SetIntEnvironment(PaddleTextDetLimitSideLenEnvironmentVariable, PaddleTextDetLimitSideLenText, 16, 4096);
        SetEnvironment(PaddleUseTextlineOrientationEnvironmentVariable, PaddleUseTextlineOrientation ? "true" : "false");
        SetEnvironment(PaddleUseDocUnwarpingEnvironmentVariable, PaddleUseDocUnwarping ? "true" : "false");
    }

    private string FormatPaddlePreprocessSummary()
    {
        var enabled = PaddlePreprocessEnabled ? "ON" : "OFF";
        var sharpen = PaddleSharpenEnabled ? "sharp ON" : "sharp OFF";
        return $"{enabled} / scale {FormatSettingValue(PaddleUpscaleText)} / contrast {FormatSettingValue(PaddleContrastText)} / {sharpen}";
    }

    private string FormatPaddleDetectionSummary()
    {
        var orientation = PaddleUseTextlineOrientation ? "orientation ON" : "orientation OFF";
        var unwarping = PaddleUseDocUnwarping ? "unwarp ON" : "unwarp OFF";
        return $"det {FormatSettingValue(PaddleTextDetThreshText)} / box {FormatSettingValue(PaddleTextDetBoxThreshText)} / unclip {FormatSettingValue(PaddleTextDetUnclipRatioText)} / limit {FormatSettingValue(PaddleTextDetLimitSideLenText)} / {orientation} / {unwarping}";
    }

    private static string FormatSettingValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "既定" : value.Trim();
    }

    private static string ReadEnvironment(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static bool ReadBoolEnvironment(string name, bool defaultValue)
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

    private static LanguageOption ResolveDefaultLanguageOption()
    {
        var cultureName = CultureInfo.CurrentUICulture.Name;
        var languageCode = cultureName.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "en";
        return SupportedLanguageOptions.FirstOrDefault(option => option.Code == languageCode)
            ?? SupportedLanguageOptions[1];
    }

    private static void ApplyLanguageCulture(LanguageOption option)
    {
        var culture = CultureInfo.GetCultureInfo(option.Code);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
