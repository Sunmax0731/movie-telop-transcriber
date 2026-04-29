using Windows.Graphics;

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
            var path = GetLayoutFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = $$"""
                {
                  "width": {{width}},
                  "height": {{height}}
                }
                """;
            File.WriteAllText(path, json);
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
