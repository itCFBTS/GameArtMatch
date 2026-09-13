using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GameArtMatch.Models;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class MatchView : UserControl
{
    public MatchView()
    {
        InitializeComponent();

        // Tunnel (not bubble) so this runs before any native TreeView/TreeViewItem
        // Space-key handling gets a chance to consume the event first — that native
        // handling is what was swallowing Space when a row was reached via arrow keys
        // rather than a click (see OnResultsTreeKeyDown for the rest of the story).
        ResultsTree.AddHandler(InputElement.KeyDownEvent, OnResultsTreeKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnResultsTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
            return;

        // Prefer whatever row visually has keyboard focus right now (walking up from the
        // event's original source); fall back to the bound selection in case focus turns
        // out to still be sitting on the TreeView itself rather than the row's own
        // container (arrow-key navigation and click-to-focus don't necessarily behave
        // identically here).
        var candidate = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as MatchCandidate
                        ?? (DataContext as MatchViewModel)?.SelectedCandidate;

        if (candidate is not null)
        {
            candidate.IsSelected = !candidate.IsSelected;
            e.Handled = true;
        }
    }
}
