using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Foundation;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MovieTelopTranscriber.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    public static readonly DependencyProperty TimelineTimeColumnWidthProperty =
        DependencyProperty.Register(nameof(TimelineTimeColumnWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(92)));

    public static readonly DependencyProperty TimelineFrameColumnWidthProperty =
        DependencyProperty.Register(nameof(TimelineFrameColumnWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(96)));

    public static readonly DependencyProperty TimelineTextColumnWidthProperty =
        DependencyProperty.Register(nameof(TimelineTextColumnWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(160)));

    public static readonly DependencyProperty TimelineDetailColumnWidthProperty =
        DependencyProperty.Register(nameof(TimelineDetailColumnWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(110)));

    public static readonly DependencyProperty TimelineConfidenceColumnWidthProperty =
        DependencyProperty.Register(nameof(TimelineConfidenceColumnWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(104)));

    public static readonly DependencyProperty RightPaneWidthProperty =
        DependencyProperty.Register(nameof(RightPaneWidth), typeof(GridLength), typeof(MainPage), new PropertyMetadata(new GridLength(560)));

    private SettingsWindow? _settingsWindow;
    private string? _activeTimelineColumnResize;
    private double _lastTimelineResizeX;
    private bool _isResizingRightPane;
    private double _lastRightPaneResizeX;
    private readonly DispatcherTimer _previewPlaybackTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool _isPreviewPlaying;

    public MainPageViewModel ViewModel { get; } = new();

    public GridLength TimelineTimeColumnWidth
    {
        get => (GridLength)GetValue(TimelineTimeColumnWidthProperty);
        set => SetValue(TimelineTimeColumnWidthProperty, value);
    }

    public GridLength TimelineFrameColumnWidth
    {
        get => (GridLength)GetValue(TimelineFrameColumnWidthProperty);
        set => SetValue(TimelineFrameColumnWidthProperty, value);
    }

    public GridLength TimelineTextColumnWidth
    {
        get => (GridLength)GetValue(TimelineTextColumnWidthProperty);
        set => SetValue(TimelineTextColumnWidthProperty, value);
    }

    public GridLength TimelineDetailColumnWidth
    {
        get => (GridLength)GetValue(TimelineDetailColumnWidthProperty);
        set => SetValue(TimelineDetailColumnWidthProperty, value);
    }

    public GridLength TimelineConfidenceColumnWidth
    {
        get => (GridLength)GetValue(TimelineConfidenceColumnWidthProperty);
        set => SetValue(TimelineConfidenceColumnWidthProperty, value);
    }

    public GridLength RightPaneWidth
    {
        get => (GridLength)GetValue(RightPaneWidthProperty);
        set => SetValue(RightPaneWidthProperty, value);
    }

    public MainPage()
    {
        InitializeComponent();
        ViewModel.SettingsWindowRequested += OnSettingsWindowRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PreviewDetections.CollectionChanged += OnPreviewDetectionsChanged;
        _previewPlaybackTimer.Tick += OnPreviewPlaybackTimerTick;
        UpdatePreviewImage();
    }

    private void OnSettingsWindowRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(ViewModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainPageViewModel.PreviewImagePath))
        {
            UpdatePreviewImage();
            return;
        }

        if (e.PropertyName is nameof(MainPageViewModel.PreviewImageWidth)
            or nameof(MainPageViewModel.PreviewImageHeight))
        {
            RedrawPreviewOverlay();
            return;
        }

        if (e.PropertyName is nameof(MainPageViewModel.UiText))
        {
            UpdatePreviewPlaybackButtonContent();
        }
    }

    private void OnPreviewDetectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawPreviewOverlay();
    }

    private void OnPreviewFrameContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawPreviewOverlay();
    }

    private void OnPreviewFrameImageOpened(object sender, RoutedEventArgs e)
    {
        RedrawPreviewOverlay();
    }

    private void OnPreviewFrameImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewOverlayCanvas.Children.Clear();
        PreviewFrameImage.Source = null;
        UpdatePreviewVisibility(hasImage: false);
    }

    private void OnTimelineColumnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string columnKey)
        {
            return;
        }

        _activeTimelineColumnResize = columnKey;
        _lastTimelineResizeX = e.GetCurrentPoint(this).Position.X;
        element.CapturePointer(e.Pointer);
    }

    private void OnTimelineColumnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeTimelineColumnResize is null || sender is not FrameworkElement element)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _lastTimelineResizeX;
        _lastTimelineResizeX = currentX;
        ResizeTimelineColumn(_activeTimelineColumnResize, delta);
        e.Handled = true;
    }

    private void OnTimelineColumnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        _activeTimelineColumnResize = null;
    }

    private void OnRightPaneSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        _isResizingRightPane = true;
        _lastRightPaneResizeX = e.GetCurrentPoint(this).Position.X;
        element.CapturePointer(e.Pointer);
    }

    private void OnRightPaneSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingRightPane)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _lastRightPaneResizeX;
        _lastRightPaneResizeX = currentX;
        RightPaneWidth = ResizeGridLength(RightPaneWidth, -delta, 360);
        e.Handled = true;
    }

    private void OnRightPaneSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        _isResizingRightPane = false;
    }

    private void ResizeTimelineColumn(string columnKey, double delta)
    {
        switch (columnKey)
        {
            case "time":
                TimelineTimeColumnWidth = ResizeGridLength(TimelineTimeColumnWidth, delta, 72);
                break;
            case "frame":
                TimelineFrameColumnWidth = ResizeGridLength(TimelineFrameColumnWidth, delta, 84);
                break;
            case "text":
                TimelineTextColumnWidth = ResizeGridLength(TimelineTextColumnWidth, delta, 120);
                break;
            case "detail":
                TimelineDetailColumnWidth = ResizeGridLength(TimelineDetailColumnWidth, delta, 90);
                break;
        }
    }

    private void OnInfoCardCopyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.CopyPathCommand.Execute(path);
        }
    }

    private void OnEditTimelineClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.EditSelectedTimelineSegmentCommand.Execute(null);
        DispatcherQueue.TryEnqueue(FocusTimelineEditingTextBox);
    }

    private async void OnDeleteTimelineClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.UiText.DeleteTelopTitle,
            Content = ViewModel.UiText.DeleteTelopMessage,
            PrimaryButtonText = ViewModel.UiText.DeleteTelopPrimary,
            CloseButtonText = ViewModel.UiText.DeleteTelopCancel,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.DeleteSelectedTimelineSegmentCommand.Execute(null);
        }
    }

    private void OnTimelineTextEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TimelineSegment segment })
        {
            ViewModel.CommitTimelineTextEdit(segment);
        }
    }

    private void OnTimelineTextEditKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is FrameworkElement { Tag: TimelineSegment segment })
        {
            ViewModel.CommitTimelineTextEdit(segment);
            e.Handled = true;
        }
    }

    private void OnPreviewPlaybackClicked(object sender, RoutedEventArgs e)
    {
        _isPreviewPlaying = !_isPreviewPlaying;
        if (_isPreviewPlaying)
        {
            _previewPlaybackTimer.Start();
            if (!ViewModel.AdvancePreviewFrame())
            {
                _isPreviewPlaying = false;
                _previewPlaybackTimer.Stop();
            }
        }
        else
        {
            _previewPlaybackTimer.Stop();
        }

        UpdatePreviewPlaybackButtonContent();
    }

    private void OnPreviewPlaybackTimerTick(object? sender, object e)
    {
        if (!ViewModel.AdvancePreviewFrame())
        {
            _isPreviewPlaying = false;
            _previewPlaybackTimer.Stop();
            UpdatePreviewPlaybackButtonContent();
        }
    }

    private void UpdatePreviewPlaybackButtonContent()
    {
        PreviewPlaybackButton.Content = _isPreviewPlaying
            ? ViewModel.UiText.PreviewPause
            : ViewModel.UiText.PreviewPlay;
    }

    private void FocusTimelineEditingTextBox()
    {
        var textBox = FindTimelineEditingTextBox(TimelineListView);
        if (textBox is null)
        {
            return;
        }

        textBox.Focus(FocusState.Programmatic);
        textBox.SelectAll();
    }

    private static TextBox? FindTimelineEditingTextBox(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox { Tag: TimelineSegment { IsEditing: true } } textBox)
            {
                return textBox;
            }

            var nested = FindTimelineEditingTextBox(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static GridLength ResizeGridLength(GridLength current, double delta, double minimum)
    {
        var currentValue = current.IsAbsolute ? current.Value : minimum;
        return new GridLength(Math.Max(minimum, currentValue + delta));
    }

    private void UpdatePreviewImage()
    {
        var imagePath = ViewModel.PreviewImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            PreviewFrameImage.Source = null;
            PreviewOverlayCanvas.Children.Clear();
            UpdatePreviewVisibility(hasImage: false);
            return;
        }

        PreviewFrameImage.Source = new BitmapImage(new System.Uri(System.IO.Path.GetFullPath(imagePath), UriKind.Absolute));
        UpdatePreviewVisibility(hasImage: true);
        RedrawPreviewOverlay();
    }

    private void UpdatePreviewVisibility(bool hasImage)
    {
        PreviewFrameImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        PreviewOverlayCanvas.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        PreviewEmptyState.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RedrawPreviewOverlay()
    {
        PreviewOverlayCanvas.Children.Clear();

        var sourceWidth = ViewModel.PreviewImageWidth;
        var sourceHeight = ViewModel.PreviewImageHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0 || ViewModel.PreviewDetections.Count == 0)
        {
            return;
        }

        var containerWidth = PreviewFrameContainer.ActualWidth;
        var containerHeight = PreviewFrameContainer.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return;
        }

        PreviewOverlayCanvas.Width = containerWidth;
        PreviewOverlayCanvas.Height = containerHeight;

        var scale = Math.Min(containerWidth / sourceWidth, containerHeight / sourceHeight);
        var displayWidth = sourceWidth * scale;
        var displayHeight = sourceHeight * scale;
        var offsetX = (containerWidth - displayWidth) / 2d;
        var offsetY = (containerHeight - displayHeight) / 2d;

        foreach (var detection in ViewModel.PreviewDetections)
        {
            DrawDetectionOverlay(detection, scale, offsetX, offsetY);
        }
    }

    private void DrawDetectionOverlay(
        PreviewDetectionOverlay detection,
        double scale,
        double offsetX,
        double offsetY)
    {
        if (detection.BoundingBox.Count == 0)
        {
            return;
        }

        var strokeBrush = detection.IsHighlighted
            ? new SolidColorBrush(Colors.OrangeRed)
            : new SolidColorBrush(Colors.DeepSkyBlue);
        var fillBrush = detection.IsHighlighted
            ? new SolidColorBrush(ColorHelper.FromArgb(36, 255, 69, 0))
            : new SolidColorBrush(ColorHelper.FromArgb(24, 0, 191, 255));

        var polygon = new Polygon
        {
            Stroke = strokeBrush,
            StrokeThickness = detection.IsHighlighted ? 3 : 2,
            Fill = fillBrush,
            Points = new PointCollection()
        };

        foreach (var point in detection.BoundingBox)
        {
            polygon.Points.Add(new Point(offsetX + (point.X * scale), offsetY + (point.Y * scale)));
        }

        PreviewOverlayCanvas.Children.Add(polygon);

        var minX = detection.BoundingBox.Min(point => point.X);
        var minY = detection.BoundingBox.Min(point => point.Y);
        var label = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(224, 16, 16, 16)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 3),
            Child = new TextBlock
            {
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Text = CreateDetectionLabel(detection),
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            }
        };

        Canvas.SetLeft(label, offsetX + (minX * scale));
        Canvas.SetTop(label, Math.Max(0, offsetY + (minY * scale) - 24));
        PreviewOverlayCanvas.Children.Add(label);
    }

    private static string CreateDetectionLabel(PreviewDetectionOverlay detection)
    {
        return detection.Confidence is null
            ? detection.Text
            : $"{detection.Text} ({detection.Confidence:P0})";
    }
}
