using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.Models;
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
        MatchedList.AddHandler(InputElement.KeyDownEvent,
            (_, e) => HandleSelectAll(e, MatchedList, (DataContext as ReportViewModel)?.Matched), RoutingStrategies.Tunnel);
        IgnoredList.AddHandler(InputElement.KeyDownEvent,
            (_, e) => HandleSelectAll(e, IgnoredList, (DataContext as ReportViewModel)?.Ignored), RoutingStrategies.Tunnel);
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

    private void OnIgnoreFromMatchedClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ReportEntry rightClicked } || DataContext is not ReportViewModel vm)
            return;

        vm.IgnoreRomsCommand.Execute(ResolveTargets(MatchedList, rightClicked));
    }

    private void OnUnignoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: string rightClicked } || DataContext is not ReportViewModel vm)
            return;

        vm.UnignoreRomsCommand.Execute(ResolveTargets(IgnoredList, rightClicked));
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

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Report",
            SuggestedFileName = $"GameArtMatch-Report-{DateTime.Now:yyyyMMdd-HHmmss}",
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
}
