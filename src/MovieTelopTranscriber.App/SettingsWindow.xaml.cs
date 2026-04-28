using Microsoft.UI.Xaml;
using MovieTelopTranscriber.App.ViewModels;
using Windows.Graphics;
using Microsoft.UI.Windowing;

namespace MovieTelopTranscriber.App;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Settings - Movie Telop Transcriber";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1680, 720));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    public MainPageViewModel ViewModel { get; }
}
