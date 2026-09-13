using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.AboutRequested += (_, _) => new AboutWindow { DataContext = new AboutViewModel() }.ShowDialog(this);
                vm.OptionsRequested += (_, _) => new OptionsWindow { DataContext = vm.Settings }.ShowDialog(this);
            }
        };
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // Per-system pickers: start from the persisted root, if one is set, so picking a
    // specific system's folder is a couple of clicks instead of navigating from scratch.
    private async void OnBrowseRomsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await PickFolderAsync("Choose your ROMs directory", vm.Settings.RomsRootPath);
        if (path is not null) vm.Settings.RomsPath = path;
    }

    private async void OnBrowseImagesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await PickFolderAsync("Choose your images directory", vm.Settings.ImagesRootPath);
        if (path is not null) vm.Settings.ImagesPath = path;
    }

    private async Task<string?> PickFolderAsync(string title, string? startInPath)
    {
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(startInPath))
        {
            try
            {
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(startInPath);
            }
            catch
            {
                // Root path no longer exists (moved drive, renamed folder, etc.) — just fall
                // back to the picker's own default location instead of failing the pick.
            }
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
