using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.Services;
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
                vm.OptionsRequested += (_, _) =>
                {
                    // Ignored ROMs can change (from the Match tab or Report window) while
                    // Options is closed — re-read before showing rather than relying on
                    // whatever IgnoredRomsVm last saw at MainViewModel construction time.
                    vm.IgnoredRomsVm.Refresh();
                    new OptionsWindow { DataContext = vm.Settings, IgnoredRomsVm = vm.IgnoredRomsVm }.ShowDialog(this);
                };
                // Non-modal (Show, not ShowDialog) — Report is a reference window you'd
                // reasonably want open alongside continued work in the Match tab (e.g.
                // ignoring more ROMs, then hitting Refresh), unlike the blocking Options
                // dialog.
                vm.ReportRequested += (_, _) => new ReportWindow { DataContext = vm.ReportVm }.Show(this);
            }
        };
    }

    // Directory.CreateDirectory first — on a fresh install nothing has ever been
    // persisted yet (MainViewModel.Persist only runs once a setting actually changes),
    // so the folder may not exist yet; without this, the very first click on a clean
    // install would try to reveal a path that isn't there.
    private void OnOpenSettingsFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        System.IO.Directory.CreateDirectory(vm.SettingsFolderPath);
        FileExplorerService.RevealFolder(vm.SettingsFolderPath);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // e.Source (not sender) deliberately — this handler is attached to the parent
    // "File Type: ..." MenuItem, and Click bubbles up from whichever auto-generated
    // child (one per AvailableFileTypes entry) was actually clicked; sender here would
    // always be the parent itself, while e.Source is the specific child that raised the
    // event. Same technique as MatchView's OnIgnoreFolderClick.
    private void OnFileTypeClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: string fileType } && DataContext is MainViewModel vm)
            vm.MatchVm.SelectedFileType = fileType;
    }

    // Sets each auto-generated child's checkmark right before the submenu is shown,
    // rather than via a live two-way binding — nothing needs to reflect a change while
    // the submenu isn't open, and there's no single item whose own DataContext is both
    // "this item's value" and "the currently selected value" to compare against.
    private void OnFileTypeSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MainViewModel vm } menu)
            return;

        for (var i = 0; i < menu.ItemCount; i++)
        {
            if (menu.ContainerFromIndex(i) is not MenuItem { DataContext: string fileType } item)
                continue;

            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = string.Equals(fileType, vm.MatchVm.SelectedFileType, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnRegionClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: string region } && DataContext is MainViewModel vm)
            vm.MatchVm.SelectedRegion = region;
    }

    private void OnRegionSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MainViewModel vm } menu)
            return;

        for (var i = 0; i < menu.ItemCount; i++)
        {
            if (menu.ContainerFromIndex(i) is not MenuItem { DataContext: string region } item)
                continue;

            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = string.Equals(region, vm.MatchVm.SelectedRegion, StringComparison.OrdinalIgnoreCase);
        }
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
