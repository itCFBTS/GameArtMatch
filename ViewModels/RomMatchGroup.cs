using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>
/// One ROM's row in the Match tab's tree, with all its candidate images nested
/// underneath. Only one candidate may ever be selected per ROM — selecting one
/// deselects any other in the group (see OnCandidatePropertyChanged). That makes
/// "select all" meaningless for the group-level checkbox below, so it's repurposed:
/// checking it selects the best-scored candidate for this ROM, unchecking it clears
/// whichever one is selected — still a "select all" of sorts in FatMatch's original
/// HandleParentNode/HandleChildNode spirit, just adapted to the one-per-ROM rule.
/// </summary>
public partial class RomMatchGroup : ObservableObject
{
    private bool _suppressCascade;

    public string RomFileName { get; }

    /// <summary>The full, unfiltered set of candidates — selection/exclusivity logic
    /// always operates on this, regardless of what the Match tab's filters currently
    /// show. See VisibleCandidates for the filtered view the tree actually binds to.</summary>
    public ObservableCollection<MatchCandidate> Candidates { get; }

    /// <summary>The filtered view MatchViewModel.ApplyFilters keeps in sync with the
    /// current file-type/region/100%-only filters — bound directly by the TreeView.
    /// Filtered-out candidates are removed from this collection entirely (not just
    /// hidden), because merely hiding TreeViewItems via IsVisible left them logically
    /// present, which made arrow-key navigation get stuck trying to focus a collapsed,
    /// zero-size row instead of skipping over it.</summary>
    public ObservableCollection<MatchCandidate> VisibleCandidates { get; } = [];

    /// <summary>true = the best candidate is selected, false = none selected, null =
    /// some other (non-best) candidate is the one currently selected — shown as the
    /// checkbox's indeterminate state. Setting it to null (via the three-state click
    /// cycle) is a no-op; indeterminate isn't something the user deliberately picks.</summary>
    [ObservableProperty] public partial bool? IsAllSelected { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;

    public RomMatchGroup(string romFileName, IEnumerable<MatchCandidate> candidates)
    {
        RomFileName = romFileName;

        // Cluster byte-identical images (same ContentHash) adjacent to each other, so
        // MatchViewModel.ApplyFilters can label every one after the first "Same as
        // above". GroupBy is stable — groups come out in first-occurrence order and each
        // group's members keep their original relative order — so this preserves the
        // incoming best-match ordering (the highest-scoring member of a cluster still
        // leads) while pulling its identical siblings to sit right after it, even if a
        // differently-scored, different-content candidate would otherwise have sorted
        // between them.
        var clustered = candidates.GroupBy(c => c.ContentHash).SelectMany(g => g);
        Candidates = new ObservableCollection<MatchCandidate>(clustered);

        foreach (var candidate in Candidates)
            candidate.PropertyChanged += OnCandidatePropertyChanged;

        RecomputeIsAllSelected();
    }

    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MatchCandidate.IsSelected) || _suppressCascade)
            return;

        // Only one candidate may be selected per ROM — selecting one deselects any
        // other that was previously selected in this same group.
        if (sender is MatchCandidate { IsSelected: true } selected)
        {
            _suppressCascade = true;
            foreach (var candidate in Candidates)
                if (candidate != selected)
                    candidate.IsSelected = false;
            _suppressCascade = false;
        }

        RecomputeIsAllSelected();
    }

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (value is null || _suppressCascade)
            return;

        _suppressCascade = true;
        if (value.Value)
        {
            var best = Candidates.OrderByBestMatch().FirstOrDefault();
            foreach (var candidate in Candidates)
                candidate.IsSelected = candidate == best;
        }
        else
        {
            foreach (var candidate in Candidates)
                candidate.IsSelected = false;
        }
        _suppressCascade = false;

        RecomputeIsAllSelected();
    }

    private void RecomputeIsAllSelected()
    {
        _suppressCascade = true;
        var selected = Candidates.FirstOrDefault(c => c.IsSelected);
        var best = Candidates.OrderByBestMatch().FirstOrDefault();
        IsAllSelected = selected is null ? false : selected == best ? true : null;
        _suppressCascade = false;
    }
}
