using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.Models;
using GameArtMatch.Services;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class OptionsView : UserControl
{
    public OptionsView()
    {
        InitializeComponent();
    }

    private OptionsViewModel? Vm => DataContext as OptionsViewModel;

    // Directory.CreateDirectory first — on a fresh install nothing has ever been
    // persisted yet (MainViewModel.Persist only runs once a setting actually changes),
    // so the folder may not exist yet; without this, the very first click on a clean
    // install would try to reveal a path that isn't there.
    private void OnOpenSettingsFolderClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Vm?.SettingsFolderPath))
            return;

        System.IO.Directory.CreateDirectory(Vm.SettingsFolderPath);
        FileExplorerService.RevealFolder(Vm.SettingsFolderPath);
    }

    private void OnOpenIgnoredFilePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: IgnoredRomEntry rightClicked })
            FileExplorerService.Reveal(rightClicked.FullPath);
    }

    private void OnUnignoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: IgnoredRomEntry rightClicked } || Vm?.IgnoredRomsVm is not { } vm)
            return;

        // Same "act on the whole selection if the right-clicked row is part of it,
        // otherwise just that row" convention as MatchView/ReportView's Ignore handlers.
        var selected = IgnoredTree.SelectedItems.OfType<IgnoredRomEntry>().Select(x => x.FullPath).ToList();
        var targets = selected.Contains(rightClicked.FullPath) ? selected : new List<string> { rightClicked.FullPath };

        vm.UnignoreRomsCommand.Execute(targets);
    }

    private void OnOpenIgnoredFolderPathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: string folderPath })
            FileExplorerService.RevealFolder(folderPath);
    }

    private void OnUnignoreFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: string rightClicked } || Vm?.IgnoredRomsVm is not { } vm)
            return;

        var selected = IgnoredFoldersList.SelectedItems?.OfType<string>().ToList() ?? [];
        var targets = selected.Contains(rightClicked) ? selected : new List<string> { rightClicked };

        vm.UnignoreFoldersCommand.Execute(targets);
    }

    // Root pickers: no suggested start location — these set the base folder itself.
    private async void OnBrowseRomsRootClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Settings is not { } settings) return;
        var path = await PickFolderAsync("Choose your ROMs root folder");
        if (path is not null) settings.RomsRootPath = path;
    }

    private async void OnBrowseImagesRootClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Settings is not { } settings) return;
        var path = await PickFolderAsync("Choose your images root folder");
        if (path is not null) settings.ImagesRootPath = path;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        // TopLevel lookup rather than a Window's own StorageProvider — this pane is a
        // UserControl hosted inside MainWindow now, not a Window of its own.
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
