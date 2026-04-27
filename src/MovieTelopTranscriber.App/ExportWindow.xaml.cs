using Microsoft.UI.Xaml;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Graphics;

namespace MovieTelopTranscriber.App;

public sealed partial class ExportWindow : Window
{
    public ExportWindow(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Export - Movie Telop Transcriber";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(760, 760));
    }

    public MainPageViewModel ViewModel { get; }
}
