using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MovieTelopTranscriber.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    private SettingsWindow? _settingsWindow;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.SettingsWindowRequested += OnSettingsWindowRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PreviewDetections.CollectionChanged += OnPreviewDetectionsChanged;
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
