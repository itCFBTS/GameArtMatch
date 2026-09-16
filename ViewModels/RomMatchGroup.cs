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

    /// <summary>Full path to the ROM on disk — every candidate in this group shares the
    /// same one, so it's read from whichever member happens to be first. Used by
    /// MatchViewModel.IgnoreRomCommand, which needs the full path (not just the
    /// filename) to persist an unambiguous entry in MatchSettings.IgnoredRomPaths.</summary>
    public string RomFullPath { get; }

    /// <summary>Every folder between this ROM and the scan's RomsPath, nearest first —
    /// e.g. for a ROM at ".../Saturn/Extras/Palettes/file.pal" scanned with RomsPath
    /// ".../Saturn", this is [".../Saturn/Extras/Palettes", ".../Saturn/Extras"].
    /// RomsPath itself is deliberately never included — ignoring the folder you
    /// explicitly configured as the scan root would silently zero out an entire
    /// system with no obvious explanation why, so "Ignore Folder" (see MatchView.axaml's
    /// context menu) never offers it as an option. Backs that menu's ItemsSource.</summary>
    public IReadOnlyList<string> IgnorableAncestorFolders { get; }

    public bool HasIgnorableAncestorFolders => IgnorableAncestorFolders.Count > 0;

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

    /// <summary>Defaults to expanded — safe because MatchViewModel.OnRomMatched adds one
    /// group at a time as a scan streams in, so the TreeView only ever has to realize one
    /// group's candidate rows per call rather than every group's at once. That per-call
    /// realization naturally interleaves with rendering the same way the rest of a
    /// streamed scan does, amortizing the cost across the whole Matching phase instead of
    /// dumping it into a single synchronous burst at some later moment (a full scan
    /// finishing, or a first "Expand All" click).</summary>
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;

    public RomMatchGroup(string romFileName, IEnumerable<MatchCandidate> candidates, string romsPath)
    {
        RomFileName = romFileName;

        // Materialized once so both RomFullPath and the clustering below see a stable
        // list rather than re-enumerating (or only ever partially enumerating) the
        // incoming sequence.
        var candidateList = candidates.ToList();
        RomFullPath = candidateList.Count > 0 ? candidateList[0].RomFullPath : "";
        IgnorableAncestorFolders = FolderAncestry.ComputeIgnorableAncestorFolders(RomFullPath, romsPath);

        // Cluster byte-identical images (same ContentHash) adjacent to each other, so
        // MatchViewModel.ApplyFilters can label every one after the first "Same as
        // above". GroupBy is stable — groups come out in first-occurrence order and each
        // group's members keep their original relative order — so this preserves the
        // incoming best-match ordering (the highest-scoring member of a cluster still
        // leads) while pulling its identical siblings to sit right after it, even if a
        // differently-scored, different-content candidate would otherwise have sorted
        // between them.
        var clustered = candidateList.GroupBy(c => c.ContentHash).SelectMany(g => g);
        Candidates = new ObservableCollection<MatchCandidate>(clustered);

        foreach (var candidate in Candidates)
            candidate.PropertyChanged += OnCandidatePropertyChanged;

        RecomputeIsAllSelected();
    }

    /// <summary>Folds a second batch of candidates into this already-constructed group —
    /// needed because live-streamed results can hand two ROMs the same RomFileName (e.g.
    /// same-named files in two different subfolders when RomsIncludeSubfolders is on),
    /// which the old batch code transparently merged into one group by grouping over the
    /// COMPLETE candidate list up front. Re-clusters the COMBINED set from scratch (not a
    /// concatenation of two independently-sorted runs) so the global best-match ordering
    /// matches what the old one-shot construction would have produced.</summary>
    public void MergeCandidates(IReadOnlyList<MatchCandidate> newCandidates)
    {
        var combined = Candidates.Concat(newCandidates)
            .OrderByBestMatch()
            .GroupBy(c => c.ContentHash)
            .SelectMany(g => g)
            .ToList();

        Candidates.Clear();
        foreach (var candidate in combined)
            Candidates.Add(candidate);

        foreach (var candidate in newCandidates)
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
