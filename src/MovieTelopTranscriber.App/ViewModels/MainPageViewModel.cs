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
    private readonly TelopFrameAnalysisService _frameAnalysisService = new();
    private IReadOnlyList<FrameAnalysisResult> _latestFrameAnalyses = Array.Empty<FrameAnalysisResult>();

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
    public partial string OcrEngineText { get; set; } = "-";

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
            var progress = new Progress<double>(value => ProgressValue = value * 0.6d);
            var intervalSeconds = ParseFrameIntervalSeconds();
            var result = await _videoProcessingService.ExtractFramesAsync(metadata, intervalSeconds, progress);

            WorkDirectoryText = result.RunDirectory;
            OcrEngineText = _frameAnalysisService.EngineName;
            PreviewState = "Running OCR";
            ActivityMessage = "OCR worker is processing extracted frames.";

            var ocrProgress = new Progress<double>(value => ProgressValue = 60d + (value * 0.4d));
            _latestFrameAnalyses = await _frameAnalysisService.AnalyzeFramesAsync(result, ocrProgress);

            TimelineSegments.Clear();
            ResultRows.Clear();

            var detectionCount = 0;
            var errorCount = 0;
            foreach (var analysis in _latestFrameAnalyses)
            {
                var timeLabel = FormatTimestamp(analysis.Frame.TimestampMs);
                if (analysis.Ocr.Status == "error")
                {
                    errorCount++;
                    TimelineSegments.Add(new TimelineSegment(timeLabel, $"Frame {analysis.Frame.FrameIndex:D6}", "OCR error"));
                    ResultRows.Add(new ResultRow(
                        timeLabel,
                        "Error",
                        analysis.Ocr.Error?.Code ?? "OCR_PROCESS_FAILED",
                        analysis.Ocr.Error?.Message ?? "OCR worker failed."));
                    continue;
                }

                if (analysis.Attributes.Detections.Count == 0)
                {
                    TimelineSegments.Add(new TimelineSegment(timeLabel, $"Frame {analysis.Frame.FrameIndex:D6}", "No telop detected"));
                    ResultRows.Add(new ResultRow(
                        timeLabel,
                        "OCR",
                        "No text detected",
                        Path.GetFileName(analysis.Frame.ImagePath)));
                    continue;
                }

                foreach (var detection in analysis.Attributes.Detections)
                {
                    detectionCount++;
                    TimelineSegments.Add(new TimelineSegment(timeLabel, detection.Text, FormatStyleSummary(detection)));
                    ResultRows.Add(new ResultRow(
                        timeLabel,
                        detection.TextType,
                        detection.Text,
                        FormatDetectionDetail(detection)));
                }
            }

            SelectedTimelineSegment = TimelineSegments.FirstOrDefault();
            PreviewState = result.Frames.Count > 0 ? $"Analyzed {result.Frames.Count} frames" : "No frames extracted";
            ActivityMessage = $"Saved {result.Frames.Count} frames and {detectionCount} detections under {result.RunDirectory}.";
            StatusMessage = errorCount == 0
                ? $"Frame extraction and OCR completed. Run ID: {result.RunId}"
                : $"Frame extraction completed with {errorCount} OCR error(s). Run ID: {result.RunId}";

            RefreshInfoCards(metadata, result.Frames.Count, detectionCount);
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
            OcrEngineText = _frameAnalysisService.EngineName;
            _latestFrameAnalyses = Array.Empty<FrameAnalysisResult>();
            PreviewState = "Ready";
            StatusMessage = $"Loaded {metadata.FileName}";
            ActivityMessage = "Metadata loaded. You can now run frame extraction.";

            RefreshInfoCards(metadata, 0, 0);
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
        SettingItems.Add(new SettingItem("OCR engine", _frameAnalysisService.EngineName, "Set MOVIE_TELOP_OCR_WORKER to use an external JSON worker."));
        SettingItems.Add(new SettingItem("Output", "work/runs/<run_id>", "Frames, OCR responses, and attributes are saved under the per-run work directory."));
    }

    private void ResetDynamicCollections()
    {
        OcrEngineText = _frameAnalysisService.EngineName;
        RefreshInfoCards(null, 0, 0);
        TimelineSegments.Clear();
        ResultRows.Clear();
        TimelineSegments.Add(new TimelineSegment("timeline", "No frames yet", "load a video to begin"));
        ResultRows.Add(new ResultRow("result", "Status", "No extracted frames", "Run frame extraction to populate this list."));
        SelectedTimelineSegment = TimelineSegments[0];
    }

    private void RefreshInfoCards(VideoMetadata? metadata, int frameCount, int detectionCount)
    {
        InfoCards.Clear();
        InfoCards.Add(new InfoCardItem("Video", metadata?.FileName ?? "Not selected", "Current source video"));
        InfoCards.Add(new InfoCardItem("Frames", frameCount.ToString(), "Extracted frame count"));
        InfoCards.Add(new InfoCardItem("OCR", $"{detectionCount} detections", OcrEngineText));
        InfoCards.Add(new InfoCardItem("Work", WorkDirectoryText, "Current output directory"));
    }

    private static string FormatStyleSummary(TelopAttributeRecord detection)
    {
        var fontSize = detection.FontSize is null ? "size unknown" : $"{detection.FontSize:F1}{detection.FontSizeUnit}";
        return $"{detection.TextType} / {fontSize}";
    }

    private static string FormatDetectionDetail(TelopAttributeRecord detection)
    {
        var confidence = detection.Confidence is null ? "confidence unknown" : $"confidence {detection.Confidence:P1}";
        var colors = string.Join(
            " / ",
            new[] { detection.TextColor, detection.StrokeColor, detection.BackgroundColor }
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
}
