using Microsoft.UI.Xaml;

namespace MovieTelopTranscriber.App.Models;

public sealed record InfoCardItem(
    string Title,
    string Value,
    string Description,
    bool CanCopy = false,
    string CopyButtonText = "Copy",
    bool CanOpen = false,
    string OpenButtonText = "Open")
{
    public Visibility CopyButtonVisibility => CanCopy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OpenButtonVisibility => CanOpen ? Visibility.Visible : Visibility.Collapsed;
}
