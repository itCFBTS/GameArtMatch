using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.Models;
using GameArtMatch.Services;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();

        // Tunnel so this always sees Ctrl+A before any of ListBox's own key handling —
        // same technique (and same reason) as MatchView.axaml.cs's Ctrl+A/Space handling
        // on the results TreeView. DataContext is read lazily inside each lambda (at
        // keypress time, not here at construction time), since it isn't set yet when this
        // constructor runs — ReportView is embedded in ReportWindow, whose DataContext is
        // assigned externally after construction.
        MissingList.AddHandler(InputElement.KeyDownEvent,
            (_, e) => HandleSelectAll(e, MissingList, (DataContext as ReportViewModel)?.Missing), RoutingStrategies.Tunnel);
    }

    private static void HandleSelectAll(KeyEventArgs e, ListBox list, System.Collections.IEnumerable? items)
    {
        if (e.Key != Key.A || e.KeyModifiers != KeyModifiers.Control || items is null)
            return;

        list.SelectedItems!.Clear();
        foreach (var item in items)
            list.SelectedItems.Add(item);
        e.Handled = true;
    }

    /// <summary>Right-clicking a row that's part of the current selection acts on the
    /// whole selection; right-clicking one that isn't acts on just that row — same
    /// convention as the Match tab's ROM context menu (see MatchView.axaml.cs).</summary>
    private static List<T> ResolveTargets<T>(ListBox list, T rightClicked) where T : notnull
    {
        var selected = list.SelectedItems?.OfType<T>().ToList() ?? [];
        return selected.Contains(rightClicked) ? selected : [rightClicked];
    }

    private void OnIgnoreFromMissingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ReportEntry rightClicked } || DataContext is not ReportViewModel vm)
            return;

        vm.IgnoreRomsCommand.Execute(ResolveTargets(MissingList, rightClicked));
    }

    // Always acts on just the right-clicked row — see MatchView.axaml.cs's equivalent
    // handlers/FileExplorerService's doc comment for why this deliberately doesn't use
    // ResolveTargets like the Ignore handlers above/below do.
    private void OnOpenFilePathFromMissingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ReportEntry rightClicked })
            FileExplorerService.Reveal(rightClicked.RomFullPath);
    }

    // e.Source (not sender) deliberately — this handler is attached to the parent
    // "Ignore Folder" MenuItem, and Click bubbles up from whichever auto-generated
    // child (one per IgnorableAncestorFolders entry) was actually clicked; sender here
    // would always be the parent itself (DataContext = ReportEntry, not a folder path),
    // while e.Source is the specific child that raised the event — same pattern as
    // MatchView.axaml.cs's OnIgnoreFolderClick.
    private void OnIgnoreFolderFromMissingClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: string folderPath } && DataContext is ReportViewModel vm)
            vm.IgnoreFolderCommand.Execute(folderPath);
    }

    // TopLevel.GetTopLevel(this) rather than a Window-typed field — this UserControl is
    // hosted inside ReportWindow, but doesn't need to know that to reach StorageProvider.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var documents = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        // e.g. "GameArtMatch-Report-SMS-20260914-003845" when a ROMs folder is selected,
        // or just "GameArtMatch-Report-20260914-003845" when none is. Sanitized since the
        // folder name comes from the filesystem (valid there) but this app ships for both
        // Windows and Linux, and a folder name can contain characters (":", etc.) that are
        // fine in a Linux path but invalid in a Windows filename.
        var romsSegment = vm.RomsFolderName is { } name ? $"-{SanitizeForFileName(name)}" : "";
        var suggestedFileName = $"GameArtMatch-Report{romsSegment}-{DateTime.Now:yyyyMMdd-HHmmss}";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Report",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            SuggestedStartLocation = documents,
            FileTypeChoices = [new FilePickerFileType("Text File") { Patterns = ["*.txt"] }],
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new System.IO.StreamWriter(stream);
        await writer.WriteAsync(vm.BuildExportText());
    }

    private static string SanitizeForFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
