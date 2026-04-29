using Windows.Graphics;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

internal static class MainWindowLayoutStore
{
    private const int DefaultWidth = 1820;
    private const int DefaultHeight = 1080;
    private const int MinimumWidth = 1560;
    private const int MinimumHeight = 920;
    private const string LayoutFileName = "main-window-layout.json";

    public static SizeInt32 LoadOrDefault()
    {
        var savedLayout = App.LaunchSettings.Ui?.MainWindow;
        if (savedLayout?.Width is > 0 && savedLayout.Height is > 0)
        {
            return new SizeInt32(
                Math.Max(MinimumWidth, savedLayout.Width.Value),
                Math.Max(MinimumHeight, savedLayout.Height.Value));
        }

        try
        {
            var path = GetLayoutFilePath();
            if (!File.Exists(path))
            {
                return new SizeInt32(DefaultWidth, DefaultHeight);
            }

            var json = File.ReadAllText(path);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("width", out var widthElement)
                || !root.TryGetProperty("height", out var heightElement))
            {
                return new SizeInt32(DefaultWidth, DefaultHeight);
            }

            return new SizeInt32(
                Math.Max(MinimumWidth, widthElement.GetInt32()),
                Math.Max(MinimumHeight, heightElement.GetInt32()));
        }
        catch
        {
            return new SizeInt32(DefaultWidth, DefaultHeight);
        }
    }

    public static void Save(SizeInt32 size)
    {
        try
        {
            var width = Math.Max(MinimumWidth, size.Width);
            var height = Math.Max(MinimumHeight, size.Height);
            App.LaunchSettings.Ui ??= new UserInterfaceSettings();
            App.LaunchSettings.Ui.MainWindow = new MainWindowLaunchSettings
            {
                Width = width,
                Height = height
            };
            AppLaunchSettingsLoader.Save(App.LaunchSettings, App.LaunchSettingsPath);
        }
        catch
        {
            // Keep window layout persistence best-effort so the app never fails on close.
        }
    }

    private static string GetLayoutFilePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MovieTelopTranscriber");
        return Path.Combine(root, LayoutFileName);
    }
}
