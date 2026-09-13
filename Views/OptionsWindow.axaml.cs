using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.Models;

namespace GameArtMatch.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // Root pickers: no suggested start location — these set the base folder itself.
    private async void OnBrowseRomsRootClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchSettings settings) return;
        var path = await PickFolderAsync("Choose your ROMs root folder");
        if (path is not null) settings.RomsRootPath = path;
    }

    private async void OnBrowseImagesRootClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchSettings settings) return;
        var path = await PickFolderAsync("Choose your images root folder");
        if (path is not null) settings.ImagesRootPath = path;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
