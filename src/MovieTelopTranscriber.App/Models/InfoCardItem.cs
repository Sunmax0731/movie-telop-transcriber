using Microsoft.UI.Xaml;

namespace MovieTelopTranscriber.App.Models;

public sealed record InfoCardItem(string Title, string Value, string Description, bool CanCopy = false)
{
    public Visibility CopyButtonVisibility => CanCopy ? Visibility.Visible : Visibility.Collapsed;
}
