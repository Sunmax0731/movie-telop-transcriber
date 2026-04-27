using Microsoft.UI.Xaml;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Graphics;

namespace MovieTelopTranscriber.App;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Settings - Movie Telop Transcriber";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(560, 720));
    }

    public MainPageViewModel ViewModel { get; }
}
