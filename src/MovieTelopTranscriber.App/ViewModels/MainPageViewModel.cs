using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Windows.ApplicationModel.DataTransfer;
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

    private sealed record ProgressFrameState(
        string StageLabel,
        double Percent,
        int TotalFrames,
        Stopwatch Stopwatch);

    private const double DefaultFrameIntervalSeconds = 1.0d;
    private const double DefaultPaddleContrast = 1.1d;
    private const double DefaultPaddleTextDetThresh = 0.3d;
    private const double DefaultPaddleTextDetBoxThresh = 0.6d;
    private const double DefaultPaddleTextDetUnclipRatio = 1.5d;
    private const double DefaultPaddleTextDetLimitSideLen = 960d;
    private const string FixedPaddleUpscale = "1";
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
    private readonly List<EditOperationRecord> _timelineEdits = new();
    private VideoMetadata? _latestMetadata;
    private FrameExtractionResult? _latestFrameExtractionResult;
    private ExportWriteResult? _latestExport;
    private double _latestFrameIntervalSeconds = 1.0d;
    private bool _isSynchronizingSelection;
    private bool _isUpdatingPreviewSequence;
    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private ProgressFrameState? _activeProgressFrameState;
    private int _manualEditSequence;

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
    public partial double PreviewSequenceValue { get; set; }

    [ObservableProperty]
    public partial double PreviewSequenceMaximum { get; set; }

    [ObservableProperty]
    public partial bool HasPreviewSequence { get; set; }

    [ObservableProperty]
    public partial string PreviewSequenceLabel { get; set; } = "-";

    [ObservableProperty]
    public partial string ActivityMessage { get; set; } = "Select a video file to load metadata and extract frames.";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "App foundation is ready. Video input and frame extraction are available on this screen.";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressDetailText { get; set; } = "-";

    [ObservableProperty]
    public partial bool ShowTimelineSelection { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool CanInteract => !IsBusy;

    [ObservableProperty]
    public partial string FrameIntervalText { get; set; } = "1.0";

    [ObservableProperty]
    public partial double FrameIntervalValue { get; set; } = DefaultFrameIntervalSeconds;

    [ObservableProperty]
    public partial string OutputRootDirectoryText { get; set; } = OpenCvVideoProcessingService.ResolveDefaultRunsRootDirectory();

    [ObservableProperty]
    public partial bool PaddlePreprocessEnabled { get; set; } = ReadBoolEnvironment(PaddlePreprocessEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleContrastText { get; set; } = ReadEnvironment(PaddleContrastEnvironmentVariable, "1.1");

    [ObservableProperty]
    public partial double PaddleContrastValue { get; set; } = ReadDoubleSetting(PaddleContrastEnvironmentVariable, DefaultPaddleContrast);

    [ObservableProperty]
    public partial bool PaddleSharpenEnabled { get; set; } = ReadBoolEnvironment(PaddleSharpenEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleTextDetThreshText { get; set; } = ReadEnvironment(PaddleTextDetThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetThreshValue { get; set; } = ReadDoubleSetting(PaddleTextDetThreshEnvironmentVariable, DefaultPaddleTextDetThresh);

    [ObservableProperty]
    public partial string PaddleTextDetBoxThreshText { get; set; } = ReadEnvironment(PaddleTextDetBoxThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetBoxThreshValue { get; set; } = ReadDoubleSetting(PaddleTextDetBoxThreshEnvironmentVariable, DefaultPaddleTextDetBoxThresh);

    [ObservableProperty]
    public partial string PaddleTextDetUnclipRatioText { get; set; } = ReadEnvironment(PaddleTextDetUnclipRatioEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetUnclipRatioValue { get; set; } = ReadDoubleSetting(PaddleTextDetUnclipRatioEnvironmentVariable, DefaultPaddleTextDetUnclipRatio);

    [ObservableProperty]
    public partial string PaddleTextDetLimitSideLenText { get; set; } = ReadEnvironment(PaddleTextDetLimitSideLenEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetLimitSideLenValue { get; set; } = ReadDoubleSetting(PaddleTextDetLimitSideLenEnvironmentVariable, DefaultPaddleTextDetLimitSideLen);

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

    public Visibility TimelineSelectionActionsVisibility =>
        SelectedTimelineSegment?.CanEdit == true ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSelectedTimelineSegmentChanged(TimelineSegment? value)
    {
        OnPropertyChanged(nameof(SelectedSegmentSummary));
        OnPropertyChanged(nameof(TimelineSelectionActionsVisibility));
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

    partial void OnPreviewSequenceValueChanged(double value)
    {
        if (_isUpdatingPreviewSequence || _latestFrameAnalyses.Count == 0)
        {
            return;
        }

        SelectPreviewFrameByIndex((int)Math.Round(value));
    }

    partial void OnFrameIntervalValueChanged(double value)
    {
        FrameIntervalText = FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
    }

    partial void OnFrameIntervalTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleContrastValueChanged(double value)
    {
        PaddleContrastText = FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
    }

    partial void OnOutputRootDirectoryTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddlePreprocessEnabledChanged(bool value)
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

    partial void OnPaddleTextDetThreshValueChanged(double value)
    {
        PaddleTextDetThreshText = FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetBoxThreshTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetBoxThreshValueChanged(double value)
    {
        PaddleTextDetBoxThreshText = FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetUnclipRatioTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetUnclipRatioValueChanged(double value)
    {
        PaddleTextDetUnclipRatioText = FormatSettingNumber(value, "0.#");
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetLimitSideLenTextChanged(string value)
    {
        RefreshStaticCollections();
    }

    partial void OnPaddleTextDetLimitSideLenValueChanged(double value)
    {
        PaddleTextDetLimitSideLenText = Math.Round(value).ToString(CultureInfo.InvariantCulture);
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
    private async Task SelectOutputRootDirectoryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        OutputRootDirectoryText = folder.Path;
        StatusMessage = $"Output folder set: {folder.Path}";
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

    [RelayCommand]
    private void CopyPath(string? path)
    {
        var normalizedPath = NormalizeActionPath(path);
        if (normalizedPath is null)
        {
            StatusMessage = "No path is available to copy.";
            return;
        }

        var package = new DataPackage();
        package.SetText(normalizedPath);
        Clipboard.SetContent(package);
        StatusMessage = $"Copied path: {normalizedPath}";
    }

    [RelayCommand]
    private void CopySelectedTimelineText()
    {
        var text = SelectedTimelineSegment?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "No selected telop text is available to copy.";
            return;
        }

        var package = new DataPackage();
        package.SetText(text.Trim());
        Clipboard.SetContent(package);
        StatusMessage = "Copied selected telop text.";
    }

    [RelayCommand]
    private void OpenPathLocation(string? path)
    {
        var normalizedPath = NormalizeActionPath(path);
        if (normalizedPath is null)
        {
            StatusMessage = "No path is available to open.";
            return;
        }

        var targetPath = ResolveExistingPath(normalizedPath);
        if (targetPath is null)
        {
            StatusMessage = $"Path does not exist: {normalizedPath}";
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = File.Exists(targetPath)
                ? $"/select,\"{targetPath}\""
                : $"\"{targetPath}\""
        };
        Process.Start(startInfo);
        StatusMessage = $"Opened path: {targetPath}";
    }

    [RelayCommand]
    private void ResetSettingsDefaults()
    {
        SelectedLanguageOption = ResolveDefaultLanguageOption();
        FrameIntervalValue = DefaultFrameIntervalSeconds;
        PaddlePreprocessEnabled = true;
        PaddleContrastValue = DefaultPaddleContrast;
        PaddleSharpenEnabled = true;
        PaddleTextDetThreshText = string.Empty;
        PaddleTextDetBoxThreshText = string.Empty;
        PaddleTextDetUnclipRatioText = string.Empty;
        PaddleTextDetLimitSideLenText = string.Empty;
        PaddleTextDetThreshValue = DefaultPaddleTextDetThresh;
        PaddleTextDetBoxThreshValue = DefaultPaddleTextDetBoxThresh;
        PaddleTextDetUnclipRatioValue = DefaultPaddleTextDetUnclipRatio;
        PaddleTextDetLimitSideLenValue = DefaultPaddleTextDetLimitSideLen;
        PaddleUseTextlineOrientation = false;
        PaddleUseDocUnwarping = false;
        ApplyPaddleOcrEnvironment();
        RefreshStaticCollections();
        StatusMessage = "Settings were reset to defaults.";
    }

    [RelayCommand]
    private void EditSelectedTimelineSegment()
    {
        if (SelectedTimelineSegment?.CanEdit != true)
        {
            StatusMessage = "Select an editable telop row before editing.";
            return;
        }

        foreach (var row in TimelineSegments)
        {
            row.IsEditing = ReferenceEquals(row, SelectedTimelineSegment);
        }

        StatusMessage = "Edit the selected telop text in the timeline.";
    }

    [RelayCommand]
    private void DeleteSelectedTimelineSegment()
    {
        if (SelectedTimelineSegment?.CanEdit != true)
        {
            StatusMessage = "Select an editable telop row before deleting.";
            return;
        }

        DeleteTimelineSegment(SelectedTimelineSegment);
    }

    [RelayCommand]
    private void MergeSelectedTimelineSegment()
    {
        if (SelectedTimelineSegment?.CanEdit != true)
        {
            StatusMessage = "Select an editable telop row before merging.";
            return;
        }

        var next = FindNextEditableTimelineSegment(SelectedTimelineSegment);
        if (next is null)
        {
            StatusMessage = "No next editable telop row is available to merge.";
            return;
        }

        MergeTimelineSegments(SelectedTimelineSegment, next);
    }

    [RelayCommand]
    private void SplitSelectedTimelineSegment()
    {
        if (SelectedTimelineSegment?.CanEdit != true)
        {
            StatusMessage = "Select an editable telop row before splitting.";
            return;
        }

        if (!TrySplitText(SelectedTimelineSegment.Text, out var firstText, out var secondText))
        {
            StatusMessage = "Selected telop text is too short to split.";
            return;
        }

        SplitTimelineSegment(SelectedTimelineSegment, firstText, secondText);
    }

    public void CommitTimelineTextEdit(TimelineSegment? segment)
    {
        if (segment is null)
        {
            return;
        }

        var newText = segment.Text.Trim();
        if (string.IsNullOrWhiteSpace(newText))
        {
            segment.IsEditing = false;
            StatusMessage = "Telop text was not changed because it was empty.";
            return;
        }

        var originalText = ResolveCurrentText(segment) ?? segment.Text;
        if (string.Equals(originalText, newText, StringComparison.Ordinal))
        {
            segment.IsEditing = false;
            StatusMessage = "Telop text was not changed.";
            return;
        }

        ApplyTimelineTextChange(segment, newText);
        AddEditRecord("edit", segment, null, originalText, newText, "timeline text edit");
        segment.Text = newText;
        segment.IsEditing = false;
        OnPropertyChanged(nameof(SelectedSegmentSummary));
        UpdatePreviewFromTimelineSelection(segment);
        StatusMessage = "Edited telop text. Use Export only to write the updated output files.";
    }

    public bool AdvancePreviewFrame()
    {
        if (_latestFrameAnalyses.Count == 0)
        {
            StatusMessage = "No extracted frames are available for preview playback.";
            return false;
        }

        var currentPath = PreviewImagePath;
        var currentIndex = string.IsNullOrWhiteSpace(currentPath)
            ? -1
            : _latestFrameAnalyses
                .Select((analysis, index) => new { analysis, index })
                .FirstOrDefault(item => string.Equals(item.analysis.Frame.ImagePath, currentPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
        SelectPreviewFrameByIndex(currentIndex + 1);
        return true;
    }

    private void SelectPreviewFrameByIndex(int index)
    {
        if (_latestFrameAnalyses.Count == 0)
        {
            return;
        }

        var normalizedIndex = ((index % _latestFrameAnalyses.Count) + _latestFrameAnalyses.Count) % _latestFrameAnalyses.Count;
        var next = _latestFrameAnalyses[normalizedIndex];
        var matchingTimelineRow = TimelineSegments.FirstOrDefault(row => row.FrameIndex == next.Frame.FrameIndex);

        if (matchingTimelineRow is not null)
        {
            SelectedTimelineSegment = matchingTimelineRow;
        }
        else
        {
            UpdatePreview(next.Frame.FrameIndex, next.Frame.TimestampMs, null, null);
        }
    }

    private void ApplyTimelineTextChange(TimelineSegment segment, string newText)
    {
        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            _latestSegments = _latestSegments
                .Select(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal)
                    ? item with { Text = newText }
                    : item)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            _latestFrameAnalyses = _latestFrameAnalyses
                .Select(analysis => ReplaceDetectionText(analysis, segment.DetectionId, newText))
                .ToArray();
        }

        ReplaceMatchingResultRow(segment, row => row with { Text = newText });
    }

    private void DeleteTimelineSegment(TimelineSegment segment)
    {
        var currentIndex = TimelineSegments.IndexOf(segment);

        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            _latestSegments = _latestSegments
                .Where(item => !string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            _latestFrameAnalyses = _latestFrameAnalyses
                .Select(analysis => RemoveDetection(analysis, segment.DetectionId))
                .ToArray();
        }

        RemoveMatchingResultRow(segment);
        TimelineSegments.Remove(segment);

        var nextIndex = Math.Clamp(currentIndex, 0, Math.Max(0, TimelineSegments.Count - 1));
        SelectedTimelineSegment = TimelineSegments.Count == 0 ? null : TimelineSegments[nextIndex];
        RefreshInfoCards(
            _latestMetadata,
            _latestFrameExtractionResult?.Frames.Count ?? 0,
            _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count),
            _latestSegments.Count);
        AddEditRecord("delete", segment, null, segment.Text, null, "timeline row delete");
        StatusMessage = "Deleted selected telop. Use Export only to write the updated output files.";
    }

    private TimelineSegment? FindNextEditableTimelineSegment(TimelineSegment segment)
    {
        var currentIndex = TimelineSegments.IndexOf(segment);
        if (currentIndex < 0)
        {
            return null;
        }

        return TimelineSegments
            .Skip(currentIndex + 1)
            .FirstOrDefault(row => row.CanEdit);
    }

    private void MergeTimelineSegments(TimelineSegment first, TimelineSegment second)
    {
        var mergedText = NormalizeMergedText(first.Text, second.Text);
        if (!string.IsNullOrWhiteSpace(first.SegmentId) && !string.IsNullOrWhiteSpace(second.SegmentId))
        {
            var firstSegment = _latestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, first.SegmentId, StringComparison.Ordinal));
            var secondSegment = _latestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, second.SegmentId, StringComparison.Ordinal));
            if (firstSegment is null || secondSegment is null)
            {
                StatusMessage = "Could not find selected segments to merge.";
                return;
            }

            var mergedSegment = firstSegment with
            {
                EndTimestampMs = Math.Max(firstSegment.EndTimestampMs, secondSegment.EndTimestampMs),
                Text = mergedText,
                TextType = string.Equals(firstSegment.TextType, secondSegment.TextType, StringComparison.Ordinal)
                    ? firstSegment.TextType
                    : "edited",
                Confidence = AverageConfidence(firstSegment.Confidence, secondSegment.Confidence),
                SourceFrameCount = firstSegment.SourceFrameCount + secondSegment.SourceFrameCount
            };
            _latestSegments = _latestSegments
                .Select(item => string.Equals(item.SegmentId, firstSegment.SegmentId, StringComparison.Ordinal) ? mergedSegment : item)
                .Where(item => !string.Equals(item.SegmentId, secondSegment.SegmentId, StringComparison.Ordinal))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(first.SegmentId) && !string.IsNullOrWhiteSpace(first.DetectionId))
        {
            _latestFrameAnalyses = _latestFrameAnalyses
                .Select(analysis => ReplaceDetectionText(analysis, first.DetectionId, mergedText))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(second.SegmentId) && !string.IsNullOrWhiteSpace(second.DetectionId))
        {
            _latestFrameAnalyses = _latestFrameAnalyses
                .Select(analysis => RemoveDetection(analysis, second.DetectionId))
                .ToArray();
        }

        AddEditRecord("merge", first, second, $"{first.Text.Trim()} | {second.Text.Trim()}", mergedText, "merged selected row with next row");
        RebuildTimelineAndResults(first.SegmentId, first.DetectionId);
        StatusMessage = "Merged selected telop with the next row. Use Export only to write the updated output files.";
    }

    private void SplitTimelineSegment(TimelineSegment segment, string firstText, string secondText)
    {
        string? secondSegmentId = null;
        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            var sourceSegment = _latestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal));
            if (sourceSegment is null)
            {
                StatusMessage = "Could not find selected segment to split.";
                return;
            }

            var midpointMs = sourceSegment.StartTimestampMs + ((sourceSegment.EndTimestampMs - sourceSegment.StartTimestampMs) / 2);
            secondSegmentId = CreateManualId(sourceSegment.SegmentId, "split");
            var firstSegment = sourceSegment with
            {
                EndTimestampMs = midpointMs,
                Text = firstText,
                SourceFrameCount = Math.Max(1, sourceSegment.SourceFrameCount / 2)
            };
            var secondSegment = sourceSegment with
            {
                SegmentId = secondSegmentId,
                StartTimestampMs = midpointMs,
                Text = secondText,
                SourceFrameCount = Math.Max(1, sourceSegment.SourceFrameCount - firstSegment.SourceFrameCount)
            };

            var updatedSegments = new List<SegmentRecord>();
            foreach (var item in _latestSegments)
            {
                if (string.Equals(item.SegmentId, sourceSegment.SegmentId, StringComparison.Ordinal))
                {
                    updatedSegments.Add(firstSegment);
                    updatedSegments.Add(secondSegment);
                }
                else
                {
                    updatedSegments.Add(item);
                }
            }

            _latestSegments = updatedSegments;
        }

        if (string.IsNullOrWhiteSpace(segment.SegmentId) && !string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            var secondDetectionId = CreateManualId(segment.DetectionId, "split");
            _latestFrameAnalyses = _latestFrameAnalyses
                .Select(analysis => SplitDetection(analysis, segment.DetectionId, secondDetectionId, firstText, secondText))
                .ToArray();
        }

        AddEditRecord("split", segment, null, segment.Text, $"{firstText} | {secondText}", "split selected row into two rows");
        RebuildTimelineAndResults(segment.SegmentId, segment.DetectionId);
        StatusMessage = "Split selected telop. Use Export only to write the updated output files.";
    }

    private void RebuildTimelineAndResults(string? preferredSegmentId, string? preferredDetectionId)
    {
        TimelineSegments.Clear();
        ResultRows.Clear();
        PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);

        var nextSelection = TimelineSegments.FirstOrDefault(row => SelectionKeysMatch(
                row.FrameIndex,
                row.TimestampMs,
                row.SegmentId,
                row.DetectionId,
                null,
                null,
                preferredSegmentId,
                preferredDetectionId))
            ?? TimelineSegments.FirstOrDefault();
        SelectedTimelineSegment = nextSelection;
        RefreshInfoCards(
            _latestMetadata,
            _latestFrameExtractionResult?.Frames.Count ?? 0,
            _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count),
            _latestSegments.Count);
    }

    private string? ResolveCurrentText(TimelineSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            var sourceSegment = _latestSegments.FirstOrDefault(item => string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal));
            if (sourceSegment is not null)
            {
                return sourceSegment.Text;
            }
        }

        if (!string.IsNullOrWhiteSpace(segment.DetectionId))
        {
            return _latestFrameAnalyses
                .SelectMany(analysis => analysis.Ocr.Detections)
                .FirstOrDefault(detection => string.Equals(detection.DetectionId, segment.DetectionId, StringComparison.Ordinal))
                ?.Text;
        }

        return null;
    }

    private void AddEditRecord(
        string operation,
        TimelineSegment target,
        TimelineSegment? related,
        string? originalText,
        string? updatedText,
        string notes)
    {
        _timelineEdits.Add(new EditOperationRecord(
            operation,
            target.SegmentId ?? target.DetectionId ?? target.RangeLabel,
            related?.SegmentId ?? related?.DetectionId,
            target.DetectionId,
            originalText,
            updatedText,
            target.TimestampMs,
            null,
            DateTimeOffset.Now,
            notes));
    }

    private void ReplaceMatchingResultRow(TimelineSegment segment, Func<ResultRow, ResultRow> replace)
    {
        var index = FindMatchingResultRowIndex(segment);
        if (index >= 0)
        {
            ResultRows[index] = replace(ResultRows[index]);
        }
    }

    private void RemoveMatchingResultRow(TimelineSegment segment)
    {
        var index = FindMatchingResultRowIndex(segment);
        if (index >= 0)
        {
            ResultRows.RemoveAt(index);
        }
    }

    private int FindMatchingResultRowIndex(TimelineSegment segment)
    {
        for (var i = 0; i < ResultRows.Count; i++)
        {
            var row = ResultRows[i];
            if (SelectionKeysMatch(
                segment.FrameIndex,
                segment.TimestampMs,
                segment.SegmentId,
                segment.DetectionId,
                row.FrameIndex,
                row.TimestampMs,
                row.SegmentId,
                row.DetectionId))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeMergedText(string first, string second)
    {
        return string.Join(
            " ",
            new[] { first.Trim(), second.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static double? AverageConfidence(double? first, double? second)
    {
        return (first, second) switch
        {
            ({ } left, { } right) => (left + right) / 2d,
            ({ } left, null) => left,
            (null, { } right) => right,
            _ => null
        };
    }

    private string CreateManualId(string baseId, string operation)
    {
        _manualEditSequence++;
        return $"{baseId}-{operation}-{_manualEditSequence:D3}";
    }

    private static bool TrySplitText(string text, out string firstText, out string secondText)
    {
        var trimmed = text.Trim();
        var lines = trimmed
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (lines.Length >= 2)
        {
            firstText = lines[0];
            secondText = string.Join(" ", lines.Skip(1));
            return true;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length >= 2)
        {
            var splitIndex = Math.Max(1, words.Length / 2);
            firstText = string.Join(' ', words.Take(splitIndex));
            secondText = string.Join(' ', words.Skip(splitIndex));
            return !string.IsNullOrWhiteSpace(firstText) && !string.IsNullOrWhiteSpace(secondText);
        }

        var textElementIndexes = StringInfo.ParseCombiningCharacters(trimmed);
        if (textElementIndexes.Length < 2)
        {
            firstText = string.Empty;
            secondText = string.Empty;
            return false;
        }

        var midpointElement = textElementIndexes.Length / 2;
        var splitAt = textElementIndexes[midpointElement];
        firstText = trimmed[..splitAt].Trim();
        secondText = trimmed[splitAt..].Trim();
        return !string.IsNullOrWhiteSpace(firstText) && !string.IsNullOrWhiteSpace(secondText);
    }

    private static FrameAnalysisResult ReplaceDetectionText(
        FrameAnalysisResult analysis,
        string detectionId,
        string newText)
    {
        var ocrDetections = analysis.Ocr.Detections
            .Select(detection => string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal)
                ? detection with { Text = newText }
                : detection)
            .ToArray();
        var attributeDetections = analysis.Attributes.Detections
            .Select(detection => string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal)
                ? detection with { Text = newText }
                : detection)
            .ToArray();

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
    }

    private static FrameAnalysisResult SplitDetection(
        FrameAnalysisResult analysis,
        string detectionId,
        string secondDetectionId,
        string firstText,
        string secondText)
    {
        var ocrDetections = SplitDetectionRecords(
            analysis.Ocr.Detections,
            detectionId,
            secondDetectionId,
            firstText,
            secondText,
            detection => detection with { Text = firstText },
            detection => detection with { DetectionId = secondDetectionId, Text = secondText });
        var attributeDetections = SplitDetectionRecords(
            analysis.Attributes.Detections,
            detectionId,
            secondDetectionId,
            firstText,
            secondText,
            detection => detection with { Text = firstText },
            detection => detection with { DetectionId = secondDetectionId, Text = secondText });

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
    }

    private static IReadOnlyList<TDetection> SplitDetectionRecords<TDetection>(
        IReadOnlyList<TDetection> detections,
        string detectionId,
        string secondDetectionId,
        string firstText,
        string secondText,
        Func<TDetection, TDetection> createFirst,
        Func<TDetection, TDetection> createSecond)
        where TDetection : notnull
    {
        var updated = new List<TDetection>();
        foreach (var detection in detections)
        {
            var id = detection switch
            {
                OcrDetectionRecord ocr => ocr.DetectionId,
                TelopAttributeRecord attribute => attribute.DetectionId,
                _ => string.Empty
            };

            if (string.Equals(id, detectionId, StringComparison.Ordinal))
            {
                updated.Add(createFirst(detection));
                updated.Add(createSecond(detection));
            }
            else
            {
                updated.Add(detection);
            }
        }

        return updated;
    }

    private static FrameAnalysisResult RemoveDetection(FrameAnalysisResult analysis, string detectionId)
    {
        var ocrDetections = analysis.Ocr.Detections
            .Where(detection => !string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal))
            .ToArray();
        var attributeDetections = analysis.Attributes.Detections
            .Where(detection => !string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal))
            .ToArray();

        return analysis with
        {
            Ocr = analysis.Ocr with { Detections = ocrDetections },
            Attributes = analysis.Attributes with { Detections = attributeDetections }
        };
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

        string outputRootDirectory;
        try
        {
            outputRootDirectory = ResolveWritableOutputRootDirectory();
        }
        catch (Exception ex)
        {
            SetPipelineFailure("Output folder", "OUTPUT_ROOT_UNAVAILABLE", ex.Message);
            return;
        }

        ApplyPaddleOcrEnvironment();
        ClearPipelineFailure();
        if (startStage != AnalysisStartStage.Export)
        {
            _timelineEdits.Clear();
        }

        IsBusy = true;
        ProgressValue = 0;
        ProgressDetailText = "0%";
        PreviewState = startStage == AnalysisStartStage.Export ? "Exporting results" : "Running analysis";
        StatusMessage = CreateStartStatus(startStage);
        ActivityMessage = "Pipeline stage is running.";
        var currentStage = "Input validation";
        CancellationTokenSource? progressTickerCts = null;
        Task? progressTickerTask = null;

        try
        {
            var startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            progressTickerCts = new CancellationTokenSource();
            progressTickerTask = RunProgressTickerAsync(progressTickerCts.Token);
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
                var expectedFrames = EstimateExtractionStepCount(metadata.DurationMs, intervalSeconds);
                var progress = new Progress<double>(value =>
                {
                    ProgressValue = value * 0.6d;
                    SetTimedFrameProgress(UiText.ProgressFrameExtraction, value, expectedFrames, stopwatch);
                });
                result = await _videoProcessingService.ExtractFramesAsync(metadata, intervalSeconds, progress, outputRootDirectory);
                _latestFrameExtractionResult = result;
            }
            else
            {
                metadata = _latestMetadata!;
                result = _latestFrameExtractionResult!;
                ProgressValue = startStage == AnalysisStartStage.Ocr ? 10d : 70d;
                ProgressDetailText = startStage == AnalysisStartStage.Ocr
                    ? FormatFrameProgress(UiText.ProgressOcr, 0d, result.Frames.Count, null, UiText)
                    : $"Output: preparing {result.Frames.Count} analyzed frames.";
            }

            WorkDirectoryText = result.RunDirectory;
            OcrEngineText = _frameAnalysisService.EngineName;

            if (startStage != AnalysisStartStage.Export)
            {
                currentStage = "OCR";
                PreviewState = "Running OCR";
                ActivityMessage = "OCR worker is processing extracted frames.";
                var ocrProgress = new Progress<double>(value =>
                {
                    ProgressValue = 60d + (value * 0.3d);
                    SetTimedFrameProgress(UiText.ProgressOcr, value, result.Frames.Count, stopwatch);
                });
                _latestFrameAnalyses = await _frameAnalysisService.AnalyzeFramesAsync(result, ocrProgress);

                currentStage = "Attribute analysis";
                _activeProgressFrameState = null;
                ProgressDetailText = $"Attribute analysis: {_latestFrameAnalyses.Count} frames analyzed.";
                _latestSegments = _segmentMerger.Merge(_latestFrameAnalyses, intervalSeconds);
            }

            TimelineSegments.Clear();
            ResultRows.Clear();

            var detectionCount = _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count);
            var errorCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "error");
            var warningCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "warning");
            PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);

            currentStage = "Output";
            ProgressDetailText = $"Output: writing JSON, CSV, and subtitles for {result.Frames.Count} frames.";
            stopwatch.Stop();
            _latestExport = await _exportPackageWriter.WriteAsync(
                metadata,
                result,
                _latestFrameAnalyses,
                _latestSegments,
                _timelineEdits,
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
            ProgressDetailText = $"Logging: writing run summary for {result.Frames.Count} frames.";
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
            ProgressDetailText = $"Completed: {result.Frames.Count} frames, {detectionCount} detections, {_latestSegments.Count} segments.";

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
            ProgressDetailText = $"Failed at {currentStage}.";
            StatusMessage = $"Pipeline failed at {currentStage}.";
        }
        finally
        {
            _activeProgressFrameState = null;
            if (progressTickerCts is not null)
            {
                progressTickerCts.Cancel();
            }

            if (progressTickerTask is not null)
            {
                try
                {
                    await progressTickerTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            progressTickerCts?.Dispose();
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
        ProgressDetailText = "Metadata: reading source video.";
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
            ProgressDetailText = "Metadata loaded.";

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
            ProgressDetailText = "Metadata load failed.";
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
        SettingItems.Add(new SettingItem(UiText.FrameIntervalSettingLabel, $"{ParseFrameIntervalSeconds():F1} {UiText.SettingSecondsUnit}", UiText.FrameIntervalSettingDescription));
        SettingItems.Add(new SettingItem(UiText.Language, SelectedLanguageOption.DisplayName, UiText.LanguageSettingDescription));
        SettingItems.Add(new SettingItem(UiText.OcrEngineSettingLabel, _frameAnalysisService.EngineName, UiText.OcrEngineSettingDescription));
        SettingItems.Add(new SettingItem(UiText.PaddlePreprocessSettingTitle, FormatPaddlePreprocessSummary(), UiText.PaddlePreprocessSettingDescription));
        SettingItems.Add(new SettingItem(UiText.PaddleDetectionSettingTitle, FormatPaddleDetectionSummary(), UiText.PaddleDetectionSettingDescription));
        SettingItems.Add(new SettingItem(UiText.OutputSettingLabel, FormatOutputRootPreview(), UiText.OutputSettingDescription));
        SettingItems.Add(new SettingItem(UiText.OutputFormatsSettingLabel, "JSON / CSV / SRT / VTT / ASS", UiText.OutputFormatsSettingDescription));
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
        ProgressDetailText = "-";
        _timelineEdits.Clear();
        _manualEditSequence = 0;
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
        InfoCards.Add(new InfoCardItem(UiText.ExportInfoTitle, ExportDirectoryText, UiText.ExportInfoDescription, IsActionablePath(ExportDirectoryText), UiText.TimelineCopy));
    }

    private string FormatOutputRootPreview()
    {
        var root = string.IsNullOrWhiteSpace(OutputRootDirectoryText)
            ? OpenCvVideoProcessingService.ResolveDefaultRunsRootDirectory()
            : OutputRootDirectoryText.Trim();
        return $"{root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}{Path.DirectorySeparatorChar}{{run_id}}";
    }

    private static bool IsActionablePath(string? path)
    {
        return NormalizeActionPath(path) is not null;
    }

    private static string? NormalizeActionPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() == "-")
        {
            return null;
        }

        return path.Trim();
    }

    private string ResolveWritableOutputRootDirectory()
    {
        var rawPath = string.IsNullOrWhiteSpace(OutputRootDirectoryText)
            ? OpenCvVideoProcessingService.ResolveDefaultRunsRootDirectory()
            : OutputRootDirectoryText.Trim();
        var expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
        var fullPath = Path.GetFullPath(expandedPath);

        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Output folder points to a file: {fullPath}");
        }

        Directory.CreateDirectory(fullPath);
        var probePath = Path.Combine(fullPath, $".movie-telop-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probePath, "write-test");
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }

        OutputRootDirectoryText = fullPath;
        return fullPath;
    }

    private static string? ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return fullPath;
        }

        var parent = Path.GetDirectoryName(fullPath);
        return string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)
            ? null
            : parent;
    }

    private static int EstimateExtractionStepCount(long durationMs, double intervalSeconds)
    {
        if (durationMs <= 0 || intervalSeconds <= 0)
        {
            return 1;
        }

        var intervalMs = Math.Max(1d, intervalSeconds * 1000d);
        var baseCount = (int)Math.Floor(durationMs / intervalMs) + 1;
        var lastTimestampMs = (long)Math.Floor(durationMs / intervalMs) * intervalMs;
        return Math.Abs(lastTimestampMs - durationMs) < 0.5d ? baseCount : baseCount + 1;
    }

    private void SetTimedFrameProgress(string stageLabel, double percent, int totalFrames, Stopwatch stopwatch)
    {
        var state = new ProgressFrameState(stageLabel, percent, totalFrames, stopwatch);
        _activeProgressFrameState = state;
        ProgressDetailText = FormatFrameProgress(state.StageLabel, state.Percent, state.TotalFrames, state.Stopwatch.Elapsed, UiText);
    }

    private async Task RunProgressTickerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var state = _activeProgressFrameState;
                if (state is null)
                {
                    continue;
                }

                var text = FormatFrameProgress(
                    state.StageLabel,
                    state.Percent,
                    state.TotalFrames,
                    state.Stopwatch.Elapsed,
                    UiText);
                if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() => ProgressDetailText = text))
                {
                    ProgressDetailText = text;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string FormatFrameProgress(
        string stage,
        double percent,
        int totalFrames,
        TimeSpan? elapsed,
        LocalizedUiText uiText)
    {
        var normalizedPercent = Math.Clamp(percent, 0d, 100d);
        var total = Math.Max(1, totalFrames);
        var completed = Math.Clamp((int)Math.Ceiling(total * normalizedPercent / 100d), 0, total);
        var progress = $"{stage}: {completed} / {total} {uiText.ProgressFramesUnit} ({normalizedPercent:F0}%)";
        if (elapsed is null)
        {
            return progress;
        }

        var remaining = EstimateRemaining(elapsed.Value, completed, total);
        return $"{progress} / {uiText.ProgressElapsed} {FormatDuration(elapsed.Value)} / {uiText.ProgressRemaining} {FormatDuration(remaining)}";
    }

    private static TimeSpan EstimateRemaining(TimeSpan elapsed, int completedFrames, int totalFrames)
    {
        if (completedFrames <= 0 || totalFrames <= 0)
        {
            return TimeSpan.Zero;
        }

        var remainingFrames = Math.Max(0, totalFrames - completedFrames);
        var millisecondsPerFrame = elapsed.TotalMilliseconds / completedFrames;
        return TimeSpan.FromMilliseconds(Math.Max(0d, millisecondsPerFrame * remainingFrames));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
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
        ProgressDetailText = $"Failed at {stage}.";
        StatusMessage = $"{stage} failed: {message}";
    }

    private void PopulateTimelineAndResults(
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<SegmentRecord> segments)
    {
        if (segments.Count > 0)
        {
            var rows = segments
                .Select(segment =>
                {
                    var rangeLabel = $"{FormatTimestamp(segment.StartTimestampMs)} - {FormatTimestamp(segment.EndTimestampMs)}";
                    var previewAnalysis = FindPreviewAnalysisForSegment(segment, frameAnalyses);
                    var previewDetection = previewAnalysis is null ? null : FindDetectionForText(previewAnalysis, segment.Text);
                    var frameIndex = previewAnalysis?.Frame.FrameIndex;
                    var timestampMs = previewAnalysis?.Frame.TimestampMs;
                    var timelineRow = new TimelineSegment(
                        rangeLabel,
                        segment.Text,
                        FormatSegmentStyleSummary(segment),
                        segment.TextType,
                        FormatSegmentDetail(segment),
                        segment.Confidence,
                        frameIndex,
                        timestampMs,
                        segment.SegmentId,
                        previewDetection?.DetectionId);
                    var resultRow = new ResultRow(
                        rangeLabel,
                        segment.TextType,
                        segment.Text,
                        FormatSegmentDetail(segment),
                        frameIndex,
                        timestampMs,
                        segment.SegmentId,
                        previewDetection?.DetectionId);

                    return new
                    {
                        Segment = segment,
                        TopY = GetTopY(previewDetection),
                        TimelineRow = timelineRow,
                        ResultRow = resultRow
                    };
                })
                .OrderBy(row => row.Segment.StartTimestampMs)
                .ThenBy(row => row.TopY)
                .ToArray();

            foreach (var row in rows)
            {
                TimelineSegments.Add(row.TimelineRow);
                ResultRows.Add(row.ResultRow);
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
                    "Error",
                    analysis.Ocr.Error?.Message ?? "OCR worker failed.",
                    null,
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

            if (analysis.Ocr.Detections.Count > 0)
            {
                foreach (var detection in analysis.Ocr.Detections.OrderBy(GetTopY))
                {
                    var detail = FormatDetectionDetail(detection, analysis.Frame.ImagePath);
                    TimelineSegments.Add(new TimelineSegment(
                        timeLabel,
                        detection.Text,
                        detail,
                        "OCR",
                        detail,
                        detection.Confidence,
                        analysis.Frame.FrameIndex,
                        analysis.Frame.TimestampMs,
                        detectionId: detection.DetectionId));
                    ResultRows.Add(new ResultRow(
                        timeLabel,
                        "OCR",
                        detection.Text,
                        detail,
                        analysis.Frame.FrameIndex,
                        analysis.Frame.TimestampMs,
                        DetectionId: detection.DetectionId));
                }

                continue;
            }

            TimelineSegments.Add(new TimelineSegment(
                timeLabel,
                $"Frame {analysis.Frame.FrameIndex:D6}",
                "No telop detected",
                "OCR",
                Path.GetFileName(analysis.Frame.ImagePath),
                null,
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
            selection.Text,
            selection.DetectionId);
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
            selection.DetectionId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId,
            row.DetectionId));
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
            selection.DetectionId,
            row.FrameIndex,
            row.TimestampMs,
            row.SegmentId,
            row.DetectionId));
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
        string? leftDetectionId,
        int? rightFrameIndex,
        long? rightTimestampMs,
        string? rightSegmentId,
        string? rightDetectionId)
    {
        if (!string.IsNullOrWhiteSpace(leftSegmentId) || !string.IsNullOrWhiteSpace(rightSegmentId))
        {
            return string.Equals(leftSegmentId, rightSegmentId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(leftDetectionId) || !string.IsNullOrWhiteSpace(rightDetectionId))
        {
            return string.Equals(leftDetectionId, rightDetectionId, StringComparison.Ordinal);
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
        UpdatePreviewSequence(analysis);

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
        _isUpdatingPreviewSequence = true;
        try
        {
            PreviewSequenceValue = 0d;
            PreviewSequenceMaximum = 0d;
            HasPreviewSequence = false;
            PreviewSequenceLabel = "-";
        }
        finally
        {
            _isUpdatingPreviewSequence = false;
        }
    }

    private void UpdatePreviewSequence(FrameAnalysisResult analysis)
    {
        var index = _latestFrameAnalyses
            .Select((item, itemIndex) => new { item, itemIndex })
            .FirstOrDefault(item => string.Equals(item.item.Frame.ImagePath, analysis.Frame.ImagePath, StringComparison.OrdinalIgnoreCase))
            ?.itemIndex ?? 0;

        _isUpdatingPreviewSequence = true;
        try
        {
            PreviewSequenceMaximum = Math.Max(0, _latestFrameAnalyses.Count - 1);
            PreviewSequenceValue = Math.Clamp(index, 0, PreviewSequenceMaximum);
            HasPreviewSequence = _latestFrameAnalyses.Count > 0;
            PreviewSequenceLabel = $"{index + 1} / {_latestFrameAnalyses.Count}";
        }
        finally
        {
            _isUpdatingPreviewSequence = false;
        }
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
            return TextsMatch(detection.Text, selectedText)
                || DetectionTextBelongsToSelectedSegment(detection.Text, selectedText);
        }

        return false;
    }

    private static bool DetectionTextBelongsToSelectedSegment(string detectionText, string selectedText)
    {
        var normalizedDetection = NormalizeTextForSelection(detectionText);
        var normalizedSelection = NormalizeTextForSelection(selectedText);
        return normalizedDetection.Length > 0
            && normalizedSelection.Length > normalizedDetection.Length
            && normalizedSelection.Contains(normalizedDetection, StringComparison.Ordinal);
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
        return $"{segment.TextType} / {fontSize}";
    }

    private static string FormatSegmentDetail(SegmentRecord segment)
    {
        var colors = string.Join(
            " / ",
            new[] { segment.TextColor, segment.StrokeColor, segment.BackgroundColor }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(colors) ? "-" : colors;
    }

    private static string FormatDetectionDetail(OcrDetectionRecord detection, string imagePath)
    {
        return Path.GetFileName(imagePath);
    }

    private static OcrDetectionRecord? FindDetectionForText(FrameAnalysisResult analysis, string text)
    {
        return analysis.Ocr.Detections
            .Where(detection => TextsMatch(detection.Text, text))
            .OrderBy(GetTopY)
            .FirstOrDefault();
    }

    private static double GetTopY(OcrDetectionRecord? detection)
    {
        return detection is null || detection.BoundingBox.Count == 0
            ? double.MaxValue
            : detection.BoundingBox.Min(point => point.Y);
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
            : DefaultFrameIntervalSeconds;
    }

    private void ApplyPaddleOcrEnvironment()
    {
        SetEnvironment(PaddlePreprocessEnvironmentVariable, PaddlePreprocessEnabled ? "true" : "false");
        SetEnvironment(PaddleUpscaleEnvironmentVariable, FixedPaddleUpscale);
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
        return $"{enabled} / scale {FixedPaddleUpscale} / contrast {FormatSettingValue(PaddleContrastText)} / {sharpen}";
    }

    private string FormatPaddleDetectionSummary()
    {
        var orientation = PaddleUseTextlineOrientation ? "orientation ON" : "orientation OFF";
        var unwarping = PaddleUseDocUnwarping ? "unwarp ON" : "unwarp OFF";
        return $"det {FormatSettingValue(PaddleTextDetThreshText)} / box {FormatSettingValue(PaddleTextDetBoxThreshText)} / unclip {FormatSettingValue(PaddleTextDetUnclipRatioText)} / limit {FormatSettingValue(PaddleTextDetLimitSideLenText)} / {orientation} / {unwarping}";
    }

    private string FormatSettingValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? UiText.SettingDefaultValue : value.Trim();
    }

    private static string FormatSettingNumber(double value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string ReadEnvironment(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static double ReadDoubleSetting(string name, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
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
