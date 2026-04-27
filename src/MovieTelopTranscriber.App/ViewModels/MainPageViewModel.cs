using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    public MainPageViewModel()
    {
        SettingItems = new ObservableCollection<SettingItem>
        {
            new("Frame interval", "1.0 sec", "Default capture interval. This will become user-configurable in a later task."),
            new("OCR engine", "PaddleOCR", "Initial placeholder based on the offline worker design."),
            new("Export format", "JSON / CSV", "Segment-level and frame-level outputs are planned.")
        };

        InfoCards = new ObservableCollection<InfoCardItem>
        {
            new("Video", "Not selected", "The app targets common formats with MP4 as the primary input."),
            new("Pipeline", "Idle", "Frame extraction and OCR will be connected in the next implementation tasks."),
            new("Sample rows", "3", "Temporary records for shell verification.")
        };

        TimelineSegments = new ObservableCollection<TimelineSegment>
        {
            new("00:00.0 - 00:02.0", "Opening title", "white / black outline / no plate"),
            new("00:05.0 - 00:07.0", "Interview subtitle", "yellow / black outline / no plate"),
            new("00:12.0 - 00:15.0", "Program notice", "white / blue plate")
        };

        ResultRows = new ObservableCollection<ResultRow>
        {
            new("00:00.0 - 00:02.0", "Title", "Opening title", "font=Yu Gothic UI, text=#FFFFFF, outline=#000000"),
            new("00:05.0 - 00:07.0", "Subtitle", "Today special feature starts here", "font=Meiryo UI, text=#F2D64B, outline=#000000"),
            new("00:12.0 - 00:15.0", "Notice", "Every Tuesday 21:00", "font=Yu Gothic UI Semibold, text=#FFFFFF, background=#1E3A8A")
        };

        SelectedTimelineSegment = TimelineSegments[0];
    }

    public ObservableCollection<SettingItem> SettingItems { get; }

    public ObservableCollection<InfoCardItem> InfoCards { get; }

    public ObservableCollection<TimelineSegment> TimelineSegments { get; }

    public ObservableCollection<ResultRow> ResultRows { get; }

    [ObservableProperty]
    public partial string WindowDescription { get; set; } = "WinUI 3 desktop shell for video input, OCR flow, and timeline-linked telop review.";

    [ObservableProperty]
    public partial string VideoPath { get; set; } = @"D:\Samples\input.mp4";

    [ObservableProperty]
    public partial string SupportedFormats { get; set; } = ".mp4, .mov, .avi, .mkv, .wmv";

    [ObservableProperty]
    public partial string PreviewState { get; set; } = "Video not loaded";

    [ObservableProperty]
    public partial string ActivityMessage { get; set; } = "Application shell initialized. Video loading, frame extraction, and OCR worker wiring are scheduled in the next tasks.";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "App foundation is ready. The three-pane layout and timeline binding shell can now be reviewed.";

    [ObservableProperty]
    public partial double ProgressValue { get; set; } = 18;

    [ObservableProperty]
    public partial bool ShowTimelineSelection { get; set; } = true;

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

    [RelayCommand]
    private void SelectVideo()
    {
        StatusMessage = "Video picker is not wired yet. This command is currently a shell placeholder.";
    }

    [RelayCommand]
    private void StartAnalysis()
    {
        PreviewState = "Preparing analysis";
        ActivityMessage = "Analysis pipeline is not connected yet. The shell only updates the visible state for now.";
        ProgressValue = 24;
        StatusMessage = "Run command accepted. Frame extraction and OCR worker integration will land in follow-up issues.";
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
}
