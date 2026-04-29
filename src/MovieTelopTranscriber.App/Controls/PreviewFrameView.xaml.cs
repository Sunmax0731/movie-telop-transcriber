using System.Collections;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using MovieTelopTranscriber.App.Models;
using Windows.Foundation;

namespace MovieTelopTranscriber.App.Controls;

public sealed class PreviewFrameView : UserControl
{
    public static readonly DependencyProperty ImagePathProperty =
        DependencyProperty.Register(nameof(ImagePath), typeof(string), typeof(PreviewFrameView), new PropertyMetadata(null, OnPreviewPropertyChanged));

    public static readonly DependencyProperty SourceWidthProperty =
        DependencyProperty.Register(nameof(SourceWidth), typeof(int), typeof(PreviewFrameView), new PropertyMetadata(0, OnPreviewPropertyChanged));

    public static readonly DependencyProperty SourceHeightProperty =
        DependencyProperty.Register(nameof(SourceHeight), typeof(int), typeof(PreviewFrameView), new PropertyMetadata(0, OnPreviewPropertyChanged));

    public static readonly DependencyProperty DetectionsProperty =
        DependencyProperty.Register(nameof(Detections), typeof(IEnumerable), typeof(PreviewFrameView), new PropertyMetadata(null, OnDetectionsChanged));

    public static readonly DependencyProperty EmptyStateTextProperty =
        DependencyProperty.Register(nameof(EmptyStateText), typeof(string), typeof(PreviewFrameView), new PropertyMetadata(string.Empty, OnPreviewPropertyChanged));

    public static readonly DependencyProperty EmptyCaptionTextProperty =
        DependencyProperty.Register(nameof(EmptyCaptionText), typeof(string), typeof(PreviewFrameView), new PropertyMetadata(string.Empty, OnPreviewPropertyChanged));

    public static readonly DependencyProperty FrameMinHeightProperty =
        DependencyProperty.Register(nameof(FrameMinHeight), typeof(double), typeof(PreviewFrameView), new PropertyMetadata(280d, OnPreviewPropertyChanged));

    private readonly Border _frameContainer;
    private readonly Image _frameImage;
    private readonly Canvas _overlayCanvas;
    private readonly StackPanel _emptyStatePanel;
    private readonly TextBlock _emptyStateTextBlock;
    private readonly TextBlock _emptyCaptionTextBlock;
    private INotifyCollectionChanged? _observedDetections;

    public PreviewFrameView()
    {
        _frameImage = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.Uniform
        };
        _frameImage.ImageOpened += OnFrameImageOpened;
        _frameImage.ImageFailed += OnFrameImageFailed;

        _overlayCanvas = new Canvas
        {
            IsHitTestVisible = false
        };

        _emptyStateTextBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        _emptyCaptionTextBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        _emptyStatePanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };
        _emptyStatePanel.Children.Add(new FontIcon
        {
            Glyph = "\uE714",
            FontSize = 32
        });
        _emptyStatePanel.Children.Add(_emptyStateTextBlock);
        _emptyStatePanel.Children.Add(_emptyCaptionTextBlock);

        var layoutGrid = new Grid();
        layoutGrid.Children.Add(_frameImage);
        layoutGrid.Children.Add(_overlayCanvas);
        layoutGrid.Children.Add(_emptyStatePanel);

        _frameContainer = new Border
        {
            Background = Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"] as Brush,
            CornerRadius = new CornerRadius(6),
            Child = layoutGrid
        };
        _frameContainer.SizeChanged += OnFrameContainerSizeChanged;

        Content = _frameContainer;
        UpdatePreviewImage();
    }

    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public int SourceWidth
    {
        get => (int)GetValue(SourceWidthProperty);
        set => SetValue(SourceWidthProperty, value);
    }

    public int SourceHeight
    {
        get => (int)GetValue(SourceHeightProperty);
        set => SetValue(SourceHeightProperty, value);
    }

    public IEnumerable? Detections
    {
        get => (IEnumerable?)GetValue(DetectionsProperty);
        set => SetValue(DetectionsProperty, value);
    }

    public string EmptyStateText
    {
        get => (string)GetValue(EmptyStateTextProperty);
        set => SetValue(EmptyStateTextProperty, value);
    }

    public string EmptyCaptionText
    {
        get => (string)GetValue(EmptyCaptionTextProperty);
        set => SetValue(EmptyCaptionTextProperty, value);
    }

    public double FrameMinHeight
    {
        get => (double)GetValue(FrameMinHeightProperty);
        set => SetValue(FrameMinHeightProperty, value);
    }

    private static void OnPreviewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewFrameView view)
        {
            view.UpdatePreviewImage();
        }
    }

    private static void OnDetectionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PreviewFrameView view)
        {
            return;
        }

        if (view._observedDetections is not null)
        {
            view._observedDetections.CollectionChanged -= view.OnDetectionsCollectionChanged;
        }

        view._observedDetections = e.NewValue as INotifyCollectionChanged;
        if (view._observedDetections is not null)
        {
            view._observedDetections.CollectionChanged += view.OnDetectionsCollectionChanged;
        }

        view.RedrawOverlay();
    }

    private void OnDetectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawOverlay();
    }

    private void OnFrameContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawOverlay();
    }

    private void OnFrameImageOpened(object sender, RoutedEventArgs e)
    {
        RedrawOverlay();
    }

    private void OnFrameImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _overlayCanvas.Children.Clear();
        _frameImage.Source = null;
        UpdatePreviewVisibility(hasImage: false);
    }

    private void UpdatePreviewImage()
    {
        _frameContainer.MinHeight = FrameMinHeight;
        _emptyStateTextBlock.Text = EmptyStateText;
        _emptyCaptionTextBlock.Text = EmptyCaptionText;

        if (string.IsNullOrWhiteSpace(ImagePath) || !File.Exists(ImagePath))
        {
            _frameImage.Source = null;
            _overlayCanvas.Children.Clear();
            UpdatePreviewVisibility(hasImage: false);
            return;
        }

        _frameImage.Source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(ImagePath), UriKind.Absolute));
        UpdatePreviewVisibility(hasImage: true);
        RedrawOverlay();
    }

    private void UpdatePreviewVisibility(bool hasImage)
    {
        _frameImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        _overlayCanvas.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        _emptyStatePanel.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RedrawOverlay()
    {
        _overlayCanvas.Children.Clear();

        if (SourceWidth <= 0 || SourceHeight <= 0)
        {
            return;
        }

        var detections = Detections?.OfType<PreviewDetectionOverlay>().ToList() ?? [];
        if (detections.Count == 0)
        {
            return;
        }

        var containerWidth = _frameContainer.ActualWidth;
        var containerHeight = _frameContainer.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return;
        }

        _overlayCanvas.Width = containerWidth;
        _overlayCanvas.Height = containerHeight;

        var scale = Math.Min(containerWidth / SourceWidth, containerHeight / SourceHeight);
        var displayWidth = SourceWidth * scale;
        var displayHeight = SourceHeight * scale;
        var offsetX = (containerWidth - displayWidth) / 2d;
        var offsetY = (containerHeight - displayHeight) / 2d;

        foreach (var detection in detections)
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

        _overlayCanvas.Children.Add(polygon);

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
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            }
        };

        Canvas.SetLeft(label, offsetX + (minX * scale));
        Canvas.SetTop(label, Math.Max(0, offsetY + (minY * scale) - 24));
        _overlayCanvas.Children.Add(label);
    }

    private static string CreateDetectionLabel(PreviewDetectionOverlay detection)
    {
        return detection.Confidence is null
            ? detection.Text
            : $"{detection.Text} ({detection.Confidence:P0})";
    }
}
