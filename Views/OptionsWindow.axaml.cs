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

public partial class OptionsWindow : Window
{
    // A real Avalonia StyledProperty, not a plain CLR auto-property — this gets set via
    // an object initializer (new OptionsWindow { DataContext = ..., IgnoredRomsVm = ... }
    // in MainWindow.axaml.cs) AFTER InitializeComponent() has already built the visual
    // tree and evaluated the Ignored ROMs tab's {Binding $parent[Window].IgnoredRomsVm}
    // once (finding it still null at that point). A plain auto-property never raises a
    // change notification, so that binding would stay stuck at null forever; a
    // StyledProperty does, so the binding correctly re-evaluates the moment this is set.
    public static readonly StyledProperty<IgnoredRomsViewModel?> IgnoredRomsVmProperty =
        AvaloniaProperty.Register<OptionsWindow, IgnoredRomsViewModel?>(nameof(IgnoredRomsVm));

    public IgnoredRomsViewModel? IgnoredRomsVm
    {
        get => GetValue(IgnoredRomsVmProperty);
        set => SetValue(IgnoredRomsVmProperty, value);
    }

    public OptionsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenIgnoredFilePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: IgnoredRomEntry rightClicked })
            FileExplorerService.Reveal(rightClicked.FullPath);
    }

    private void OnUnignoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: IgnoredRomEntry rightClicked } || IgnoredRomsVm is not { } vm)
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
        if (sender is not MenuItem { DataContext: string rightClicked } || IgnoredRomsVm is not { } vm)
            return;

        var selected = IgnoredFoldersList.SelectedItems?.OfType<string>().ToList() ?? [];
        var targets = selected.Contains(rightClicked) ? selected : new List<string> { rightClicked };

        vm.UnignoreFoldersCommand.Execute(targets);
    }

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
