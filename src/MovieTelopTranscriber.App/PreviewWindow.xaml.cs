using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using MovieTelopTranscriber.App.Controls;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Graphics;

namespace MovieTelopTranscriber.App;

public sealed class PreviewWindow : Window
{
    public PreviewWindow(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;

        Title = "Preview - Movie Telop Transcriber";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1120, 840));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
        }

        var rootGrid = new Grid
        {
            Padding = new Thickness(16),
            RowSpacing = 12
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = ViewModel.UiText.PreviewSection,
            FontSize = 20,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }
        };

        var summaryText = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords
        };
        summaryText.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.SelectedSegmentSummary))
        });

        var detailText = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.8
        };
        detailText.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewDetailText))
        });

        var headerPanel = new StackPanel { Spacing = 4 };
        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(summaryText);
        headerPanel.Children.Add(detailText);

        var previewFrameView = new PreviewFrameView
        {
            EmptyCaptionText = ViewModel.UiText.PreviewShell,
            FrameMinHeight = 560
        };
        previewFrameView.SetBinding(PreviewFrameView.ImagePathProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewImagePath))
        });
        previewFrameView.SetBinding(PreviewFrameView.SourceWidthProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewImageWidth))
        });
        previewFrameView.SetBinding(PreviewFrameView.SourceHeightProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewImageHeight))
        });
        previewFrameView.SetBinding(PreviewFrameView.DetectionsProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewDetections))
        });
        previewFrameView.SetBinding(PreviewFrameView.EmptyStateTextProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewState))
        });

        var footerGrid = new Grid { ColumnSpacing = 10 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new Slider { Minimum = 0 };
        slider.SetBinding(Slider.MaximumProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewSequenceMaximum))
        });
        slider.SetBinding(Slider.IsEnabledProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.HasPreviewSequence))
        });
        slider.SetBinding(Slider.ValueProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewSequenceValue)),
            Mode = BindingMode.TwoWay
        });

        var sequenceLabel = new TextBlock
        {
            MinWidth = 52,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        sequenceLabel.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(MainPageViewModel.PreviewSequenceLabel))
        });

        footerGrid.Children.Add(slider);
        Grid.SetColumn(sequenceLabel, 1);
        footerGrid.Children.Add(sequenceLabel);

        rootGrid.Children.Add(headerPanel);
        Grid.SetRow(previewFrameView, 1);
        rootGrid.Children.Add(previewFrameView);
        Grid.SetRow(footerGrid, 2);
        rootGrid.Children.Add(footerGrid);

        Content = rootGrid;
    }

    public MainPageViewModel ViewModel { get; }
}
