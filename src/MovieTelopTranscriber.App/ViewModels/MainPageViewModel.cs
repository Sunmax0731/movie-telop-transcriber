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
    private readonly OpenCvVideoProcessingService _videoProcessingService = new();
    private readonly TelopFrameAnalysisService _frameAnalysisService = new();
    private readonly TelopSegmentMerger _segmentMerger = new();
    private readonly ProjectBundleService _projectBundleService = new();
    private readonly MainPageAnalysisOutputCoordinator _analysisOutputCoordinator = new();
    private static readonly LanguageOption[] SupportedLanguageOptions =
    [
        new("ja", "日本語"),
        new("en", "English"),
        new("zh", "中文"),
        new("ko", "한국어")
    ];

    private static readonly SelectionOption[] SupportedPaddleDeviceOptions =
    [
        new("cpu", "CPU"),
        new("gpu:0", "GPU (gpu:0)")
    ];

    private IReadOnlyList<FrameAnalysisResult> _latestFrameAnalyses = Array.Empty<FrameAnalysisResult>();
    private IReadOnlyList<SegmentRecord> _latestSegments = Array.Empty<SegmentRecord>();
    private readonly List<EditOperationRecord> _timelineEdits = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _segmentDetectionIds = new(StringComparer.Ordinal);
    private VideoMetadata? _latestMetadata;
    private FrameExtractionResult? _latestFrameExtractionResult;
    private ExportWriteResult? _latestExport;
    private double _latestFrameIntervalSeconds = 1.0d;
    private bool _isSynchronizingSelection;
    private bool _isUpdatingPreviewSequence;
    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private ProgressFrameState? _activeProgressFrameState;
    private int _manualEditSequence;
    private MainPageOcrWarmupState _ocrWarmupState = MainPageOcrWarmupState.Empty;
    private bool _settingsPersistenceReady;
    private string? _currentProjectFilePath;
    private string? _loadedProjectExtractionDirectory;

    public MainPageViewModel()
    {
        SettingItems = new ObservableCollection<SettingItem>();
        LanguageOptions = new ObservableCollection<LanguageOption>(SupportedLanguageOptions);
        InfoCards = new ObservableCollection<InfoCardItem>();
        TimelineSegments = new ObservableCollection<TimelineSegment>();
        ResultRows = new ObservableCollection<ResultRow>();
        PreviewDetections = new ObservableCollection<PreviewDetectionOverlay>();

        SelectedLanguageOption = ResolveDefaultLanguageOption();
        ApplyStoredUiState(MainPageUserSettingsCoordinator.ResolveSavedUserInterfaceSettings(
            App.LaunchSettings.Ui,
            LanguageOptions,
            App.LaunchSettingsPath));
        UiText = LocalizedUiText.ForLanguage(SelectedLanguageOption.Code);
        SelectedPaddleDeviceOption = MainPageUserSettingsCoordinator.ResolvePaddleDeviceOption(
            Environment.GetEnvironmentVariable(MainPageUserSettingsCoordinator.PaddleDeviceEnvironmentVariable),
            SupportedPaddleDeviceOptions);
        ApplyPaddleOcrEnvironment();
        RefreshStaticCollections();
        ResetDynamicCollections();
        _settingsPersistenceReady = true;
    }

    public ObservableCollection<SettingItem> SettingItems { get; }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    public ObservableCollection<InfoCardItem> InfoCards { get; }

    public ObservableCollection<TimelineSegment> TimelineSegments { get; }

    public ObservableCollection<ResultRow> ResultRows { get; }

    public ObservableCollection<PreviewDetectionOverlay> PreviewDetections { get; }

    public IReadOnlyList<int> PaddleWorkerCountOptions { get; } = [1, 2];

    public IReadOnlyList<SelectionOption> PaddleDeviceOptions { get; } = SupportedPaddleDeviceOptions;

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

    public bool IsPaddleWorkerCountEditable => CanInteract && MainPageUserSettingsCoordinator.IsGpuDevice(SelectedPaddleDeviceOption.Key);

    [ObservableProperty]
    public partial string FrameIntervalText { get; set; } = "1.0";

    [ObservableProperty]
    public partial double FrameIntervalValue { get; set; } = DefaultFrameIntervalSeconds;

    [ObservableProperty]
    public partial string OutputRootDirectoryText { get; set; } = OpenCvVideoProcessingService.ResolveDefaultRunsRootDirectory();

    [ObservableProperty]
    public partial bool PaddlePreprocessEnabled { get; set; } = MainPageUserSettingsCoordinator.ReadBoolEnvironment(MainPageUserSettingsCoordinator.PaddlePreprocessEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleContrastText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleContrastEnvironmentVariable, "1.1");

    [ObservableProperty]
    public partial double PaddleContrastValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleContrastEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleContrast);

    [ObservableProperty]
    public partial bool PaddleSharpenEnabled { get; set; } = MainPageUserSettingsCoordinator.ReadBoolEnvironment(MainPageUserSettingsCoordinator.PaddleSharpenEnvironmentVariable, true);

    [ObservableProperty]
    public partial string PaddleTextDetThreshText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleTextDetThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetThreshValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleTextDetThreshEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleTextDetThresh);

    [ObservableProperty]
    public partial string PaddleTextDetBoxThreshText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleTextDetBoxThreshEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetBoxThreshValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleTextDetBoxThreshEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleTextDetBoxThresh);

    [ObservableProperty]
    public partial string PaddleTextDetUnclipRatioText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleTextDetUnclipRatioEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetUnclipRatioValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleTextDetUnclipRatioEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleTextDetUnclipRatio);

    [ObservableProperty]
    public partial string PaddleTextDetLimitSideLenText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleTextDetLimitSideLenEnvironmentVariable, string.Empty);

    [ObservableProperty]
    public partial double PaddleTextDetLimitSideLenValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleTextDetLimitSideLenEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleTextDetLimitSideLen);

    [ObservableProperty]
    public partial string PaddleMinTextSizeText { get; set; } = MainPageUserSettingsCoordinator.ReadEnvironment(MainPageUserSettingsCoordinator.PaddleMinTextSizeEnvironmentVariable, "0");

    [ObservableProperty]
    public partial double PaddleMinTextSizeValue { get; set; } = MainPageUserSettingsCoordinator.ReadDoubleSetting(MainPageUserSettingsCoordinator.PaddleMinTextSizeEnvironmentVariable, MainPageUserSettingsCoordinator.DefaultPaddleMinTextSize);

    [ObservableProperty]
    public partial bool PaddleUseTextlineOrientation { get; set; } = MainPageUserSettingsCoordinator.ReadBoolEnvironment(MainPageUserSettingsCoordinator.PaddleUseTextlineOrientationEnvironmentVariable, false);

    [ObservableProperty]
    public partial bool PaddleUseDocUnwarping { get; set; } = MainPageUserSettingsCoordinator.ReadBoolEnvironment(MainPageUserSettingsCoordinator.PaddleUseDocUnwarpingEnvironmentVariable, false);

    [ObservableProperty]
    public partial SelectionOption SelectedPaddleDeviceOption { get; set; } = SupportedPaddleDeviceOptions[0];

    [ObservableProperty]
    public partial int PaddleWorkerCount { get; set; } = MainPageUserSettingsCoordinator.ReadPaddleWorkerCountSetting();

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
        OnPropertyChanged(nameof(IsPaddleWorkerCountEditable));
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
        FrameIntervalText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnFrameIntervalTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleContrastValueChanged(double value)
    {
        PaddleContrastText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnOutputRootDirectoryTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddlePreprocessEnabledChanged(bool value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleContrastTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleSharpenEnabledChanged(bool value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetThreshTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetThreshValueChanged(double value)
    {
        PaddleTextDetThreshText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetBoxThreshTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetBoxThreshValueChanged(double value)
    {
        PaddleTextDetBoxThreshText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.##");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetUnclipRatioTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetUnclipRatioValueChanged(double value)
    {
        PaddleTextDetUnclipRatioText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.#");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetLimitSideLenTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleTextDetLimitSideLenValueChanged(double value)
    {
        PaddleTextDetLimitSideLenText = Math.Round(value).ToString(CultureInfo.InvariantCulture);
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleMinTextSizeTextChanged(string value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleMinTextSizeValueChanged(double value)
    {
        PaddleMinTextSizeText = MainPageUserSettingsCoordinator.FormatSettingNumber(value, "0.#");
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleUseTextlineOrientationChanged(bool value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleUseDocUnwarpingChanged(bool value)
    {
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnSelectedPaddleDeviceOptionChanged(SelectionOption value)
    {
        PaddleWorkerCount = MainPageUserSettingsCoordinator.NormalizePaddleWorkerCount(PaddleWorkerCount, value.Key);
        OnPropertyChanged(nameof(IsPaddleWorkerCountEditable));
        RefreshStaticCollections();
        OnUserSettingsChanged();
    }

    partial void OnPaddleWorkerCountChanged(int value)
    {
        var normalized = MainPageUserSettingsCoordinator.NormalizePaddleWorkerCount(value, SelectedPaddleDeviceOption.Key);
        if (normalized != value)
        {
            PaddleWorkerCount = normalized;
            return;
        }

        RefreshStaticCollections();
        OnUserSettingsChanged();
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
        OnUserSettingsChanged();
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
    private async Task OpenProjectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".mtproj");
        picker.FileTypeFilter.Add(".zip");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadProjectAsync(file.Path);
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (_latestMetadata is null || _latestFrameExtractionResult is null || _latestFrameAnalyses.Count == 0)
        {
            StatusMessage = "Run frame extraction and OCR before saving a project.";
            return;
        }

        var targetPath = _currentProjectFilePath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Movie Telop Project", [".mtproj"]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_latestMetadata.FileName);
            picker.DefaultFileExtension = ".mtproj";

            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            targetPath = file.Path;
        }

        await SaveProjectBundleAsync(targetPath);
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
        PaddleContrastValue = MainPageUserSettingsCoordinator.DefaultPaddleContrast;
        PaddleSharpenEnabled = true;
        PaddleTextDetThreshText = string.Empty;
        PaddleTextDetBoxThreshText = string.Empty;
        PaddleTextDetUnclipRatioText = string.Empty;
        PaddleTextDetLimitSideLenText = string.Empty;
        PaddleMinTextSizeText = "0";
        PaddleTextDetThreshValue = MainPageUserSettingsCoordinator.DefaultPaddleTextDetThresh;
        PaddleTextDetBoxThreshValue = MainPageUserSettingsCoordinator.DefaultPaddleTextDetBoxThresh;
        PaddleTextDetUnclipRatioValue = MainPageUserSettingsCoordinator.DefaultPaddleTextDetUnclipRatio;
        PaddleTextDetLimitSideLenValue = MainPageUserSettingsCoordinator.DefaultPaddleTextDetLimitSideLen;
        PaddleMinTextSizeValue = MainPageUserSettingsCoordinator.DefaultPaddleMinTextSize;
        PaddleUseTextlineOrientation = false;
        PaddleUseDocUnwarping = false;
        PaddleWorkerCount = 1;
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

        var currentIndex = TimelineSegments.IndexOf(SelectedTimelineSegment);
        var outcome = MainPageTimelineEditCoordinator.Delete(BuildTimelineEditState(), SelectedTimelineSegment);
        ApplyTimelineEditOutcome(outcome, currentIndex);
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

        if (!MainPageTimelineEditCoordinator.CanMergeTimelineSegments(SelectedTimelineSegment, next, out var mergeStatusMessage))
        {
            StatusMessage = mergeStatusMessage;
            return;
        }

        var outcome = MainPageTimelineEditCoordinator.Merge(BuildTimelineEditState(), SelectedTimelineSegment, next);
        if (!outcome.Changed)
        {
            StatusMessage = outcome.StatusMessage;
            return;
        }

        ApplyTimelineEditOutcome(outcome);
    }

    [RelayCommand]
    private void SplitSelectedTimelineSegment()
    {
        if (SelectedTimelineSegment?.CanEdit != true)
        {
            StatusMessage = "Select an editable telop row before splitting.";
            return;
        }

        if (MainPageTimelineEditCoordinator.IsLikelyTimecodeText(SelectedTimelineSegment.Text))
        {
            StatusMessage = "Timecode-like rows cannot be split as telop text.";
            return;
        }

        if (!MainPageTimelineEditCoordinator.TrySplitText(SelectedTimelineSegment.Text, out var firstText, out var secondText))
        {
            StatusMessage = "Split requires a line break or whitespace in the selected text.";
            return;
        }

        var outcome = MainPageTimelineEditCoordinator.Split(BuildTimelineEditState(), SelectedTimelineSegment, firstText, secondText);
        if (!outcome.Changed)
        {
            StatusMessage = outcome.StatusMessage;
            return;
        }

        ApplyTimelineEditOutcome(outcome);
    }

    public void CommitTimelineTextEdit(TimelineSegment? segment)
    {
        if (segment is null)
        {
            return;
        }

        var outcome = MainPageTimelineEditCoordinator.UpdateText(
            BuildTimelineEditState(),
            segment,
            segment.Text);
        if (!outcome.Changed)
        {
            segment.IsEditing = false;
            StatusMessage = outcome.StatusMessage;
            return;
        }

        ApplyTimelineEditOutcome(outcome);
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
        var selectionState = PreviewSelectionCoordinator.BuildFrameSelectionState(
            _latestFrameAnalyses,
            TimelineSegments,
            ResultRows,
            index,
            SelectedTimelineSegment,
            SelectedResultRow);
        ApplyPreviewSelectionState(selectionState);
    }

    private MainPageTimelineEditState BuildTimelineEditState()
    {
        var detectionIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in _segmentDetectionIds)
        {
            detectionIds[pair.Key] = pair.Value.ToArray();
        }

        return new MainPageTimelineEditState(
            _latestFrameAnalyses,
            _latestSegments,
            _timelineEdits.ToArray(),
            detectionIds,
            _manualEditSequence);
    }

    private void ApplyTimelineEditOutcome(MainPageTimelineEditOutcome outcome, int? preferredIndex = null)
    {
        _latestFrameAnalyses = outcome.State.LatestFrameAnalyses;
        _latestSegments = outcome.State.LatestSegments;
        _timelineEdits.Clear();
        _timelineEdits.AddRange(outcome.State.TimelineEdits);
        _segmentDetectionIds.Clear();
        foreach (var pair in outcome.State.SegmentDetectionIds)
        {
            _segmentDetectionIds[pair.Key] = pair.Value;
        }

        _manualEditSequence = outcome.State.ManualEditSequence;
        RebuildTimelineAndResults(outcome.PreferredSegmentId, outcome.PreferredDetectionId, preferredIndex);
        StatusMessage = outcome.StatusMessage;
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

    private void RebuildTimelineAndResults(string? preferredSegmentId, string? preferredDetectionId, int? preferredIndex = null)
    {
        TimelineSegments.Clear();
        ResultRows.Clear();
        PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);

        TimelineSegment? nextSelection;
        if (!string.IsNullOrWhiteSpace(preferredSegmentId) || !string.IsNullOrWhiteSpace(preferredDetectionId))
        {
            nextSelection = PreviewSelectionCoordinator.FindTimelineSelection(
                TimelineSegments,
                preferredSegmentId,
                preferredDetectionId);
        }
        else if (preferredIndex is not null && TimelineSegments.Count > 0)
        {
            nextSelection = TimelineSegments[Math.Clamp(preferredIndex.Value, 0, TimelineSegments.Count - 1)];
        }
        else
        {
            nextSelection = TimelineSegments.FirstOrDefault();
        }

        SelectedTimelineSegment = nextSelection;
        RefreshInfoCards(
            _latestMetadata,
            _latestFrameExtractionResult?.Frames.Count ?? 0,
            _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count),
            _latestSegments.Count);
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
            var frameExtractionDurationMs = 0d;
            var warmupResult = OcrWorkerWarmupResult.Skipped;
            var ocrDurationMs = 0d;
            var segmentMergeDurationMs = 0d;
            var exportWriteDurationMs = 0d;
            var logWriteDurationMs = 0d;
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
                EnsureBackgroundOcrWarmupStarted();
                _latestMetadata = metadata;
                _latestFrameIntervalSeconds = intervalSeconds;

                currentStage = "Frame extraction";
                var expectedFrames = EstimateExtractionStepCount(metadata.DurationMs, intervalSeconds);
                var progress = new Progress<double>(value =>
                {
                    ProgressValue = value * 0.6d;
                    SetTimedFrameProgress(UiText.ProgressFrameExtraction, value, expectedFrames, stopwatch);
                });
                var frameExtractionStopwatch = Stopwatch.StartNew();
                result = await _videoProcessingService.ExtractFramesAsync(metadata, intervalSeconds, progress, outputRootDirectory);
                frameExtractionStopwatch.Stop();
                frameExtractionDurationMs = frameExtractionStopwatch.Elapsed.TotalMilliseconds;
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
                warmupResult = await ResolveOcrWarmupAsync();
                if (warmupResult.Attempted && !warmupResult.Succeeded)
                {
                    ActivityMessage = "OCR warmup failed. Continuing with extracted frames.";
                }
                var ocrProgress = new Progress<double>(value =>
                {
                    ProgressValue = 60d + (value * 0.3d);
                    SetTimedFrameProgress(UiText.ProgressOcr, value, result.Frames.Count, stopwatch);
                });
                var ocrStopwatch = Stopwatch.StartNew();
                _latestFrameAnalyses = await _frameAnalysisService.AnalyzeFramesAsync(result, ocrProgress);
                ocrStopwatch.Stop();
                ocrDurationMs = ocrStopwatch.Elapsed.TotalMilliseconds;

                currentStage = "Attribute analysis";
                _activeProgressFrameState = null;
                ProgressDetailText = $"Attribute analysis: {_latestFrameAnalyses.Count} frames analyzed.";
                var segmentMergeStopwatch = Stopwatch.StartNew();
                _latestSegments = _segmentMerger.Merge(_latestFrameAnalyses, intervalSeconds);
                segmentMergeStopwatch.Stop();
                segmentMergeDurationMs = segmentMergeStopwatch.Elapsed.TotalMilliseconds;
                RebuildSegmentDetectionMap(_latestSegments, _latestFrameAnalyses);
            }

            TimelineSegments.Clear();
            ResultRows.Clear();

            var detectionCount = _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count);
            var errorCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "error");
            var warningCount = _latestFrameAnalyses.Count(analysis => analysis.Ocr.Status == "warning");
            var firstOcrError = _latestFrameAnalyses
                .Select(analysis => analysis.Ocr.Error)
                .FirstOrDefault(error => error is not null);
            if (firstOcrError is not null)
            {
                LastFailedStageText = "OCR";
                LastErrorCodeText = firstOcrError.Code;
                LastErrorMessageText = firstOcrError.Message;
            }

            PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);

            currentStage = "Output";
            ProgressDetailText = $"Output: writing JSON, CSV, and subtitles for {result.Frames.Count} frames.";
            stopwatch.Stop();
            var outputOutcome = await _analysisOutputCoordinator.WriteAsync(
                new MainPageAnalysisOutputRequest(
                    metadata,
                    result,
                    _latestFrameAnalyses,
                    _latestSegments,
                    _timelineEdits,
                    intervalSeconds,
                    OcrEngineText,
                    ResolveEffectivePaddleWorkerCount(OcrEngineText),
                    startedAt,
                    frameExtractionDurationMs,
                    warmupResult,
                    ocrDurationMs,
                    segmentMergeDurationMs));
            _latestExport = outputOutcome.Export;
            exportWriteDurationMs = outputOutcome.PerformanceSummary.ExportWriteMs;
            logWriteDurationMs = outputOutcome.PerformanceSummary.LogWriteMs;
            detectionCount = outputOutcome.DetectionCount;
            warningCount = outputOutcome.WarningCount;
            errorCount = outputOutcome.ErrorCount;
            firstOcrError = outputOutcome.FirstOcrError;
            ExportDirectoryText = _latestExport.OutputDirectory;
            JsonOutputPathText = _latestExport.JsonPath;
            SegmentsCsvOutputPathText = _latestExport.SegmentsCsvPath;
            FramesCsvOutputPathText = _latestExport.FramesCsvPath;

            currentStage = "Logging";
            ProgressDetailText = $"Logging: writing run summary for {result.Frames.Count} frames.";
            var logWriteResult = outputOutcome.LogWriteResult;
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

            ActivityMessage = firstOcrError is null
                ? $"Saved {result.Frames.Count} frames, {detectionCount} detections, {_latestSegments.Count} segments, and run logs under {result.RunDirectory}."
                : $"Saved {result.Frames.Count} frames with {errorCount} OCR error(s). Latest error: {firstOcrError.Code}. Run directory: {result.RunDirectory}.";
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

    private async Task LoadProjectAsync(string projectFilePath)
    {
        IsBusy = true;
        ProgressValue = 0;
        ProgressDetailText = "Project: opening saved bundle.";
        PreviewState = "Opening project";
        ActivityMessage = "Opening project file.";

        try
        {
            ReleaseLoadedProjectExtractionDirectory();
            var loadResult = await _projectBundleService.LoadAsync(projectFilePath);
            var projectState = ProjectLoadSaveCoordinator.BuildLoadState(
                projectFilePath,
                loadResult,
                LanguageOptions);

            _currentProjectFilePath = projectState.CurrentProjectFilePath;
            _loadedProjectExtractionDirectory = projectState.LoadedProjectExtractionDirectory;
            _latestMetadata = projectState.LatestMetadata;
            _latestFrameExtractionResult = projectState.LatestFrameExtractionResult;
            _latestFrameAnalyses = projectState.LatestFrameAnalyses;
            _latestSegments = projectState.LatestSegments;
            _latestFrameIntervalSeconds = projectState.LatestFrameIntervalSeconds;
            _timelineEdits.Clear();
            _timelineEdits.AddRange(projectState.TimelineEdits);
            ClearPipelineFailure();

            ApplyStoredUiState(projectState.UiState);

            VideoPath = projectState.VideoPath;
            DurationText = FormatTimestamp(projectState.LatestMetadata.DurationMs);
            ResolutionText = $"{projectState.LatestMetadata.Width} x {projectState.LatestMetadata.Height}";
            FpsText = $"{projectState.LatestMetadata.Fps:F3}";
            CodecText = projectState.LatestMetadata.Codec;
            WorkDirectoryText = projectState.WorkDirectoryText;
            OcrEngineText = projectState.OcrEngineText;
            ExportDirectoryText = projectState.ExportDirectoryText;
            JsonOutputPathText = projectState.JsonOutputPathText;
            SegmentsCsvOutputPathText = projectState.SegmentsCsvOutputPathText;
            FramesCsvOutputPathText = projectState.FramesCsvOutputPathText;
            LogDirectoryText = "-";
            RunLogPathText = "-";
            RunSummaryPathText = "-";
            _latestExport = projectState.LatestExport;

            TimelineSegments.Clear();
            ResultRows.Clear();
            PopulateTimelineAndResults(_latestFrameAnalyses, _latestSegments);
            ApplySelectionFromProjectManifest(loadResult.Manifest.SelectedSegmentId, loadResult.Manifest.SelectedDetectionId);
            RefreshInfoCards(
                _latestMetadata,
                _latestFrameExtractionResult.Frames.Count,
                _latestFrameAnalyses.Sum(analysis => analysis.Attributes.Detections.Count),
                _latestSegments.Count);

            PreviewState = "Project loaded";
            ActivityMessage = projectState.SourceVideoExists
                ? "Project loaded. Timeline and preview were restored from the saved bundle."
                : "Project loaded. Source video path is missing, but bundled frames and timeline were restored.";
            StatusMessage = projectState.SourceVideoExists
                ? $"Loaded project: {Path.GetFileName(projectFilePath)}"
                : $"Loaded project with missing source video path: {Path.GetFileName(projectFilePath)}";
            ProgressDetailText = "Project loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to open project.";
            ActivityMessage = ex.Message;
            ProgressDetailText = "Project load failed.";
            PreviewState = "Project load failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProjectBundleAsync(string projectFilePath)
    {
        if (_latestMetadata is null || _latestFrameExtractionResult is null)
        {
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        ProgressDetailText = "Project: saving current timeline bundle.";
        ActivityMessage = "Saving project file.";
        PreviewState = "Saving project";

        try
        {
            var saveState = ProjectLoadSaveCoordinator.BuildSaveState(
                BuildUserSettingsState(),
                App.LaunchSettings.Ui?.MainWindow,
                OcrEngineText,
                _frameAnalysisService.EngineName,
                SelectedTimelineSegment?.SegmentId,
                SelectedTimelineSegment?.DetectionId);

            await _projectBundleService.SaveAsync(
                projectFilePath,
                _latestMetadata,
                _latestFrameExtractionResult,
                _latestFrameAnalyses,
                _latestSegments,
                _timelineEdits,
                saveState.FrameIntervalSeconds,
                saveState.OcrEngine,
                saveState.UiSettings,
                saveState.SelectedSegmentId,
                saveState.SelectedDetectionId);

            _currentProjectFilePath = projectFilePath;
            StatusMessage = $"Saved project: {projectFilePath}";
            ActivityMessage = "Project file was saved.";
            ProgressDetailText = "Project saved.";
            PreviewState = "Project saved";
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to save project.";
            ActivityMessage = ex.Message;
            ProgressDetailText = "Project save failed.";
            PreviewState = "Project save failed";
        }
        finally
        {
            IsBusy = false;
        }
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
            _currentProjectFilePath = null;
            ReleaseLoadedProjectExtractionDirectory();
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
            _segmentDetectionIds.Clear();
            _latestExport = null;
            _latestFrameIntervalSeconds = ParseFrameIntervalSeconds();
            ClearPipelineFailure();
            EnsureBackgroundOcrWarmupStarted();
            PreviewState = "Ready";
            StatusMessage = $"Loaded {metadata.FileName}";
            ActivityMessage = _ocrWarmupState.PendingTask is null
                ? "Metadata loaded. You can now run frame extraction."
                : "Metadata loaded. OCR warmup is running in background.";
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
        _segmentDetectionIds.Clear();
        _manualEditSequence = 0;
        RefreshInfoCards(null, 0, 0, 0);
        TimelineSegments.Clear();
        ResultRows.Clear();
        TimelineSegments.Add(new TimelineSegment("timeline", "No frames yet", "load a video to begin"));
        ResultRows.Add(new ResultRow("result", "Status", "No extracted frames", "Run frame extraction to populate this list."));
        SelectFirstPreviewSelection();
        ClearPreview("Video not loaded", "Select a video file to display frame preview.");
    }

    private void EnsureBackgroundOcrWarmupStarted()
    {
        _ocrWarmupState = MainPageOcrWarmupCoordinator.EnsureStarted(
            _ocrWarmupState,
            BuildOcrWarmupSettings(),
            RunOcrWarmupWithIsolatedDirectoryAsync);
    }

    private async Task<OcrWorkerWarmupResult> ResolveOcrWarmupAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await MainPageOcrWarmupCoordinator.ResolveAsync(
            _ocrWarmupState,
            BuildOcrWarmupSettings(),
            () => ProgressDetailText = "OCR warmup: preparing worker.",
            RunOcrWarmupWithIsolatedDirectoryAsync,
            cancellationToken);
        _ocrWarmupState = resolution.State;
        return resolution.Result;
    }

    private async Task<OcrWorkerWarmupResult> RunOcrWarmupWithIsolatedDirectoryAsync(CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "MovieTelopTranscriber",
            "ocr-warmup",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            return await _frameAnalysisService.WarmupAsync(workDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new OcrWorkerWarmupResult(
                "error",
                0d,
                0d,
                0d,
                0d,
                0d,
                new ProcessingError("OCR_WARMUP_FAILED", "OCR warmup could not be completed.", ex.Message, true));
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private MainPageOcrWarmupSettings BuildOcrWarmupSettings()
    {
        return new MainPageOcrWarmupSettings(
            _frameAnalysisService.EngineName,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_LANG") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_MIN_SCORE") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PREPROCESS") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_UPSCALE") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_CONTRAST") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SHARPEN") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_MIN_TEXT_SIZE") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION") ?? string.Empty,
            Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING") ?? string.Empty,
            Environment.GetEnvironmentVariable(MainPageUserSettingsCoordinator.PaddleWorkerCountEnvironmentVariable) ?? string.Empty);
    }

    private void RefreshInfoCards(VideoMetadata? metadata, int frameCount, int detectionCount, int segmentCount)
    {
        InfoCards.Clear();
        InfoCards.Add(new InfoCardItem(UiText.VideoInfoTitle, metadata?.FileName ?? "Not selected", UiText.VideoInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.FramesInfoTitle, frameCount.ToString(), UiText.FramesInfoDescription));
        InfoCards.Add(new InfoCardItem(UiText.OcrInfoTitle, $"{detectionCount} detections", OcrEngineText));
        InfoCards.Add(new InfoCardItem(UiText.SegmentsInfoTitle, segmentCount.ToString(), UiText.SegmentsInfoDescription));
        var canOpenExportDirectory = IsActionablePath(ExportDirectoryText);
        InfoCards.Add(new InfoCardItem(
            UiText.ExportInfoTitle,
            ExportDirectoryText,
            UiText.ExportInfoDescription,
            canOpenExportDirectory,
            UiText.TimelineCopy,
            canOpenExportDirectory,
            UiText.OpenPathButton));
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
                    var segmentDetectionIds = ResolveDetectionIdsForSegment(segment, frameAnalyses);
                    var previewAnalysis = FindPreviewAnalysisForSegment(segment, frameAnalyses, segmentDetectionIds);
                    var previewDetection = previewAnalysis is null
                        ? null
                        : FindDetectionForSegmentPreview(previewAnalysis, segment.Text, segmentDetectionIds);
                    var frameIndex = previewAnalysis?.Frame.FrameIndex;
                    var timestampMs = previewAnalysis?.Frame.TimestampMs;
                    var timelineRow = new TimelineSegment(
                        rangeLabel,
                        segment.Text,
                        FormatSegmentStyleSummary(segment),
                        segment.TextType,
                        FormatSegmentDetail(segment),
                        segment.FontSize,
                        segment.FontSizeUnit,
                        segment.Confidence,
                        frameIndex,
                        timestampMs,
                        segment.SegmentId,
                        previewDetection?.DetectionId,
                        segmentDetectionIds);
                    var resultRow = new ResultRow(
                        rangeLabel,
                        segment.TextType,
                        segment.Text,
                        FormatSegmentDetail(segment),
                        frameIndex,
                        timestampMs,
                        segment.SegmentId,
                        previewDetection?.DetectionId,
                        segmentDetectionIds);

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
                    confidence: null,
                    frameIndex: analysis.Frame.FrameIndex,
                    timestampMs: analysis.Frame.TimestampMs));
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
                        fontSize: null,
                        fontSizeUnit: null,
                        confidence: detection.Confidence,
                        frameIndex: analysis.Frame.FrameIndex,
                        timestampMs: analysis.Frame.TimestampMs,
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
                confidence: null,
                frameIndex: analysis.Frame.FrameIndex,
                timestampMs: analysis.Frame.TimestampMs));
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
        var selectionState = PreviewSelectionCoordinator.SelectFirst(TimelineSegments, ResultRows);
        ApplyPreviewSelectionState(selectionState);
    }

    private void UpdatePreviewFromTimelineSelection(TimelineSegment? selection)
    {
        var selectionState = PreviewSelectionCoordinator.BuildTimelineSelectionState(
            selection,
            ResultRows,
            SelectedResultRow);
        ApplyPreviewSelectionState(selectionState);
    }

    private void UpdatePreviewFromResultSelection(ResultRow? selection)
    {
        var selectionState = PreviewSelectionCoordinator.BuildResultSelectionState(
            selection,
            TimelineSegments,
            SelectedTimelineSegment);
        ApplyPreviewSelectionState(selectionState);
    }

    private void ApplyPreviewSelectionState(MainPagePreviewSelectionState selectionState)
    {
        _isSynchronizingSelection = true;
        try
        {
            if (!ReferenceEquals(SelectedTimelineSegment, selectionState.TimelineSelection))
            {
                SelectedTimelineSegment = selectionState.TimelineSelection;
            }

            if (!ReferenceEquals(SelectedResultRow, selectionState.ResultSelection))
            {
                SelectedResultRow = selectionState.ResultSelection;
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        if (selectionState.PreviewRequest is null)
        {
            ClearPreview("No frame selected", "Select a timeline row or result row to display a frame.");
            return;
        }

        UpdatePreview(selectionState.PreviewRequest);
    }

    private void UpdatePreview(PreviewSelectionRequest request)
    {
        var analysis = PreviewSelectionCoordinator.ResolvePreviewAnalysis(_latestFrameAnalyses, request);
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
            var highlighted = IsHighlightedDetection(
                detection,
                request.DetectionId,
                request.DetectionIds,
                request.SegmentId,
                request.SelectedText);
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
        var index = PreviewSelectionCoordinator.ResolvePreviewSequenceIndex(_latestFrameAnalyses, analysis);

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

    private static bool IsHighlightedDetection(
        OcrDetectionRecord detection,
        string? detectionId,
        IReadOnlyCollection<string>? detectionIds,
        string? segmentId,
        string? selectedText)
    {
        if (detectionIds?.Contains(detection.DetectionId) == true)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(detectionId))
        {
            return string.Equals(detection.DetectionId, detectionId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(segmentId) && !string.IsNullOrWhiteSpace(selectedText))
        {
            return TextsMatch(detection.Text, selectedText)
                || DetectionTextIsRelatedToSelectedSegment(detection.Text, selectedText);
        }

        return false;
    }

    private static bool DetectionTextIsRelatedToSelectedSegment(string detectionText, string selectedText)
    {
        var normalizedDetection = NormalizeTextForSelection(detectionText);
        var normalizedSelection = NormalizeTextForSelection(selectedText);
        if (normalizedDetection.Length == 0 || normalizedSelection.Length == 0)
        {
            return false;
        }

        return normalizedSelection.Contains(normalizedDetection, StringComparison.Ordinal)
            || normalizedDetection.Contains(normalizedSelection, StringComparison.Ordinal);
    }

    private static FrameAnalysisResult? FindPreviewAnalysisForSegment(
        SegmentRecord segment,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyCollection<string> detectionIds)
    {
        if (detectionIds.Count > 0)
        {
            var detectionFrame = frameAnalyses.FirstOrDefault(analysis =>
                analysis.Ocr.Detections.Any(detection => detectionIds.Contains(detection.DetectionId)));
            if (detectionFrame is not null)
            {
                return detectionFrame;
            }
        }

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
                analysis.Ocr.Detections.Any(detection => TextsMatch(detection.Text, segment.Text)
                    || DetectionTextIsRelatedToSelectedSegment(detection.Text, segment.Text)))
            ?? candidates.FirstOrDefault(analysis => analysis.Ocr.Detections.Count > 0)
            ?? candidates.FirstOrDefault();
    }

    private static IReadOnlyList<string> FindDetectionIdsForSegment(
        SegmentRecord segment,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses)
    {
        return frameAnalyses
            .Where(analysis =>
                analysis.Frame.TimestampMs >= segment.StartTimestampMs
                && analysis.Frame.TimestampMs <= segment.EndTimestampMs)
            .SelectMany(analysis => analysis.Ocr.Detections)
            .Where(detection => TextsMatch(detection.Text, segment.Text)
                || DetectionTextIsRelatedToSelectedSegment(detection.Text, segment.Text))
            .OrderBy(GetTopY)
            .Select(detection => detection.DetectionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void RebuildSegmentDetectionMap(
        IReadOnlyList<SegmentRecord> segments,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses)
    {
        _segmentDetectionIds.Clear();
        foreach (var segment in segments)
        {
            _segmentDetectionIds[segment.SegmentId] = FindDetectionIdsForSegment(segment, frameAnalyses);
        }
    }

    private IReadOnlyList<string> ResolveDetectionIdsForSegment(
        SegmentRecord segment,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses)
    {
        if (_segmentDetectionIds.TryGetValue(segment.SegmentId, out var detectionIds))
        {
            return detectionIds;
        }

        detectionIds = FindDetectionIdsForSegment(segment, frameAnalyses);
        _segmentDetectionIds[segment.SegmentId] = detectionIds;
        return detectionIds;
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

    private static OcrDetectionRecord? FindDetectionForSegmentPreview(
        FrameAnalysisResult analysis,
        string text,
        IReadOnlyCollection<string> detectionIds)
    {
        return analysis.Ocr.Detections
            .Where(detection => detectionIds.Contains(detection.DetectionId))
            .OrderBy(GetTopY)
            .FirstOrDefault()
            ?? FindDetectionForText(analysis, text)
            ?? analysis.Ocr.Detections
                .Where(detection => DetectionTextIsRelatedToSelectedSegment(detection.Text, text))
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

    private void ApplyStoredUiState(MainPageStoredUiState uiState)
    {
        if (uiState.SelectedLanguageOption is not null)
        {
            SelectedLanguageOption = uiState.SelectedLanguageOption;
        }

        if (uiState.FrameIntervalValue.HasValue && !string.IsNullOrWhiteSpace(uiState.FrameIntervalText))
        {
            FrameIntervalValue = uiState.FrameIntervalValue.Value;
            FrameIntervalText = uiState.FrameIntervalText;
        }

        if (!string.IsNullOrWhiteSpace(uiState.OutputRootDirectoryText))
        {
            OutputRootDirectoryText = uiState.OutputRootDirectoryText;
        }
    }

    private void ApplySelectionFromProjectManifest(string? selectedSegmentId, string? selectedDetectionId)
    {
        var nextSelection = PreviewSelectionCoordinator.FindTimelineSelection(
            TimelineSegments,
            selectedSegmentId,
            selectedDetectionId);

        SelectedTimelineSegment = nextSelection;
        UpdatePreviewFromTimelineSelection(SelectedTimelineSegment);
    }

    private void ReleaseLoadedProjectExtractionDirectory()
    {
        if (string.IsNullOrWhiteSpace(_loadedProjectExtractionDirectory))
        {
            return;
        }

        _projectBundleService.DeleteExtractionDirectory(_loadedProjectExtractionDirectory);
        _loadedProjectExtractionDirectory = null;
    }

    private MainPageUserSettingsState BuildUserSettingsState()
    {
        return new MainPageUserSettingsState(
            SelectedLanguageOption.Code,
            ParseFrameIntervalSeconds(),
            OutputRootDirectoryText,
            PaddlePreprocessEnabled,
            PaddleContrastText,
            PaddleSharpenEnabled,
            PaddleTextDetThreshText,
            PaddleTextDetBoxThreshText,
            PaddleTextDetUnclipRatioText,
            PaddleTextDetLimitSideLenText,
            PaddleMinTextSizeText,
            PaddleUseTextlineOrientation,
            PaddleUseDocUnwarping,
            SelectedPaddleDeviceOption.Key,
            PaddleWorkerCount);
    }

    private void OnUserSettingsChanged()
    {
        if (!_settingsPersistenceReady)
        {
            return;
        }

        MainPageUserSettingsCoordinator.PersistUserSettings(
            App.LaunchSettings,
            App.LaunchSettingsPath,
            BuildUserSettingsState(),
            App.LaunchSettings.Ui?.MainWindow);
    }

    private void ApplyPaddleOcrEnvironment()
    {
        MainPageUserSettingsCoordinator.ApplyPaddleOcrEnvironment(BuildUserSettingsState());
    }

    private string FormatPaddlePreprocessSummary()
    {
        var enabled = PaddlePreprocessEnabled ? "ON" : "OFF";
        var sharpen = PaddleSharpenEnabled ? "sharp ON" : "sharp OFF";
        return $"{enabled} / scale {MainPageUserSettingsCoordinator.FixedPaddleUpscale} / contrast {FormatSettingValue(PaddleContrastText)} / {sharpen}";
    }

    private string FormatPaddleDetectionSummary()
    {
        var workerSummary = MainPageUserSettingsCoordinator.IsGpuDevice(SelectedPaddleDeviceOption.Key)
            ? $"{MainPageUserSettingsCoordinator.NormalizePaddleWorkerCount(PaddleWorkerCount, SelectedPaddleDeviceOption.Key)} workers"
            : "CPU fixed to 1 worker";
        var orientation = PaddleUseTextlineOrientation ? "orientation ON" : "orientation OFF";
        var unwarping = PaddleUseDocUnwarping ? "unwarp ON" : "unwarp OFF";
        return $"device {SelectedPaddleDeviceOption.Key} / det {FormatSettingValue(PaddleTextDetThreshText)} / box {FormatSettingValue(PaddleTextDetBoxThreshText)} / unclip {FormatSettingValue(PaddleTextDetUnclipRatioText)} / limit {FormatSettingValue(PaddleTextDetLimitSideLenText)} / min size {FormatSettingValue(PaddleMinTextSizeText)} / {workerSummary} / {orientation} / {unwarping}";
    }

    private string FormatSettingValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? UiText.SettingDefaultValue : value.Trim();
    }

    private int ResolveEffectivePaddleWorkerCount(string ocrEngine)
    {
        return string.Equals(ocrEngine, "paddleocr", StringComparison.OrdinalIgnoreCase)
            ? MainPageUserSettingsCoordinator.NormalizePaddleWorkerCount(PaddleWorkerCount, SelectedPaddleDeviceOption.Key)
            : 1;
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
