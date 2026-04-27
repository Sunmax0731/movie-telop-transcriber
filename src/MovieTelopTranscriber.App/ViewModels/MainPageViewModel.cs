using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using Windows.Storage.Pickers;

namespace MovieTelopTranscriber.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly OpenCvVideoProcessingService _videoProcessingService = new();

    public MainPageViewModel()
    {
        SettingItems = new ObservableCollection<SettingItem>();
        InfoCards = new ObservableCollection<InfoCardItem>();
        TimelineSegments = new ObservableCollection<TimelineSegment>();
        ResultRows = new ObservableCollection<ResultRow>();

        RefreshStaticCollections();
        ResetDynamicCollections();
    }

    public ObservableCollection<SettingItem> SettingItems { get; }

    public ObservableCollection<InfoCardItem> InfoCards { get; }

    public ObservableCollection<TimelineSegment> TimelineSegments { get; }

    public ObservableCollection<ResultRow> ResultRows { get; }

    [ObservableProperty]
    public partial string WindowDescription { get; set; } = "WinUI 3 desktop shell for video input, frame extraction, and timeline-linked review.";

    [ObservableProperty]
    public partial string VideoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupportedFormats { get; set; } = ".mp4, .mov, .avi, .mkv, .wmv";

    [ObservableProperty]
    public partial string PreviewState { get; set; } = "Video not loaded";

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
    public partial TimelineSegment? SelectedTimelineSegment { get; set; }

    public string SelectedSegmentSummary =>
        SelectedTimelineSegment is null
            ? "Nothing selected"
            : $"{SelectedTimelineSegment.RangeLabel} / {SelectedTimelineSegment.Text} / {SelectedTimelineSegment.StyleSummary}";

    partial void OnSelectedTimelineSegmentChanged(TimelineSegment? value)
    {
        OnPropertyChanged(nameof(SelectedSegmentSummary));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnFrameIntervalTextChanged(string value)
    {
        RefreshStaticCollections();
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
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(VideoPath) || !File.Exists(VideoPath))
        {
            StatusMessage = "Select a valid video file before running extraction.";
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        PreviewState = "Extracting frames";
        StatusMessage = "Frame extraction started.";
        ActivityMessage = "Frames are being written to the work directory.";

        try
        {
            var metadata = await _videoProcessingService.ReadMetadataAsync(VideoPath);
            var progress = new Progress<double>(value => ProgressValue = value);
            var intervalSeconds = ParseFrameIntervalSeconds();
            var result = await _videoProcessingService.ExtractFramesAsync(metadata, intervalSeconds, progress);

            WorkDirectoryText = result.FramesDirectory;
            TimelineSegments.Clear();
            ResultRows.Clear();

            foreach (var frame in result.Frames)
            {
                var timeLabel = FormatTimestamp(frame.TimestampMs);
                TimelineSegments.Add(new TimelineSegment(timeLabel, $"Frame {frame.FrameIndex:D6}", Path.GetFileName(frame.ImagePath)));
                ResultRows.Add(new ResultRow(timeLabel, "Frame", Path.GetFileName(frame.ImagePath), frame.ImagePath));
            }

            SelectedTimelineSegment = TimelineSegments.FirstOrDefault();
            PreviewState = result.Frames.Count > 0 ? $"Extracted {result.Frames.Count} frames" : "No frames extracted";
            ActivityMessage = $"Saved {result.Frames.Count} frames to {result.FramesDirectory}.";
            StatusMessage = $"Frame extraction completed. Run ID: {result.RunId}";

            RefreshInfoCards(metadata, result.Frames.Count);
        }
        catch (Exception ex)
        {
            PreviewState = "Extraction failed";
            ActivityMessage = ex.Message;
            StatusMessage = "Frame extraction failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        StatusMessage = "Settings window will be implemented later as a non-modal child window.";
    }

    [RelayCommand]
    private void OpenExport()
    {
        StatusMessage = "Export window will be implemented later as a non-modal child window.";
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
            PreviewState = "Ready";
            StatusMessage = $"Loaded {metadata.FileName}";
            ActivityMessage = "Metadata loaded. You can now run frame extraction.";

            RefreshInfoCards(metadata, 0);
            TimelineSegments.Clear();
            ResultRows.Clear();
            TimelineSegments.Add(new TimelineSegment("preview", metadata.FileName, "metadata loaded"));
            ResultRows.Add(new ResultRow("metadata", "Video", metadata.FileName, $"{ResolutionText} / {FpsText} fps / {CodecText}"));
            SelectedTimelineSegment = TimelineSegments[0];
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
        SettingItems.Add(new SettingItem("Frame interval", $"{ParseFrameIntervalSeconds():F1} sec", "Default extraction interval."));
        SettingItems.Add(new SettingItem("OCR engine", "PaddleOCR", "Reserved for the next implementation task."));
        SettingItems.Add(new SettingItem("Output", "work/runs/<run_id>/frames", "Frames are saved under the per-run work directory."));
    }

    private void ResetDynamicCollections()
    {
        RefreshInfoCards(null, 0);
        TimelineSegments.Clear();
        ResultRows.Clear();
        TimelineSegments.Add(new TimelineSegment("timeline", "No frames yet", "load a video to begin"));
        ResultRows.Add(new ResultRow("result", "Status", "No extracted frames", "Run frame extraction to populate this list."));
        SelectedTimelineSegment = TimelineSegments[0];
    }

    private void RefreshInfoCards(VideoMetadata? metadata, int frameCount)
    {
        InfoCards.Clear();
        InfoCards.Add(new InfoCardItem("Video", metadata?.FileName ?? "Not selected", "Current source video"));
        InfoCards.Add(new InfoCardItem("Frames", frameCount.ToString(), "Extracted frame count"));
        InfoCards.Add(new InfoCardItem("Work", WorkDirectoryText, "Current output directory"));
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
}
