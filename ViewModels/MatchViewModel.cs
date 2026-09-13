using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;
using GameArtMatch.Services;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the "Match" tab — the old frmMatch's Start/Cancel + Results tree.</summary>
public partial class MatchViewModel : ViewModelBase
{
    private readonly MatchSettings _settings;
    private readonly IMatchingService _matchingService;
    private readonly IRenameService _renameService;
    private CancellationTokenSource? _cts;
    private int _previewRequestId;

    /// <summary>Every ROM that has at least one candidate, from the most recent scan —
    /// unfiltered. Groups (below) is the filtered view actually bound to the TreeView.</summary>
    private readonly List<RomMatchGroup> _allGroups = [];

    /// <summary>The filtered view of _allGroups that ApplyFilters keeps in sync with the
    /// current file-type/region/100%-only filters — bound directly to the TreeView.
    /// Filtered-out ROMs are removed from this collection entirely (not just hidden),
    /// for the same reason VisibleCandidates removes filtered-out images — see there.</summary>
    public ObservableCollection<RomMatchGroup> Groups { get; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = "Press 'Start' when ready.";

    /// <summary>0-100, driven by MatchingService's throttled progress reports during a
    /// scan — a real determinate progress bar, since the ROM count is known upfront.</summary>
    [ObservableProperty] public partial double ProgressPercent { get; set; }

    /// <summary>True only while a Start scan is actually running — separate from the
    /// broader IsBusy (which also covers Rename) so the progress bar doesn't show a
    /// stale matching percentage during an unrelated rename operation.</summary>
    [ObservableProperty] public partial bool IsMatchRunning { get; set; }

    // --- Match tab filters: a post-scan display filter, doesn't affect scoring or
    // re-run matching — just shows/hides already-computed rows (see ApplyFilters). ---

    /// <summary>Populated after each scan from the distinct ROM extensions actually
    /// present in the results — "All" plus whatever's really there, not a fixed list.</summary>
    public ObservableCollection<string> AvailableFileTypes { get; } = [FileTypeAll];

    private const string FileTypeAll = "All";

    [ObservableProperty] public partial string SelectedFileType { get; set; } = FileTypeAll;

    public string[] RegionOptions => RegionFilter.Options;

    [ObservableProperty] public partial string SelectedRegion { get; set; } = RegionFilter.All;

    /// <summary>When on, only candidates scoring a full 100% are shown — a quick way to
    /// see just the sure things, hiding everything that still needs a human look.</summary>
    [ObservableProperty] public partial bool ShowOnly100PercentMatches { get; set; }

    partial void OnSelectedFileTypeChanged(string value) => ApplyFilters();

    partial void OnSelectedRegionChanged(string value) => ApplyFilters();

    partial void OnShowOnly100PercentMatchesChanged(bool value) => ApplyFilters();

    /// <summary>File type only ever restricts ROMs (never images). Region restricts both
    /// — except English Translated, which by design shows every image and only narrows
    /// down which ROMs qualify. 100%-only restricts candidates by score. A ROM group is
    /// only shown if it passes both ROM-side checks AND has at least one still-visible
    /// candidate under it.
    ///
    /// Rebuilds Groups/VisibleCandidates from scratch each time rather than toggling a
    /// visibility flag — filtered-out rows need to be genuinely absent from the bound
    /// collections, not just invisible, or arrow-key navigation gets stuck trying to
    /// focus a collapsed, zero-size TreeViewItem instead of skipping over it.</summary>
    private void ApplyFilters()
    {
        Groups.Clear();

        foreach (var group in _allGroups)
        {
            var romTypeOk = SelectedFileType == FileTypeAll ||
                             string.Equals(Path.GetExtension(group.RomFileName), SelectedFileType, StringComparison.OrdinalIgnoreCase);
            var romRegionOk = RegionFilter.Matches(group.RomFileName, SelectedRegion, isImage: false);

            group.VisibleCandidates.Clear();
            if (romTypeOk && romRegionOk)
            {
                foreach (var candidate in group.Candidates)
                {
                    var regionOk = RegionFilter.Matches(candidate.ImageFileName, SelectedRegion, isImage: true);
                    var scoreOk = !ShowOnly100PercentMatches || candidate.ScorePercent >= 100;
                    if (regionOk && scoreOk)
                        group.VisibleCandidates.Add(candidate);
                }
            }

            if (group.VisibleCandidates.Count > 0)
                Groups.Add(group);
        }
    }

    /// <summary>Whatever's currently highlighted in the tree — a RomMatchGroup or a
    /// MatchCandidate, since both levels are selectable. Only a MatchCandidate drives
    /// the preview panel (see SelectedCandidate below).</summary>
    [ObservableProperty] public partial object? SelectedTreeItem { get; set; }

    /// <summary>The candidate currently being previewed — independent of any row's own
    /// IsSelected checkbox (that's "include in rename batch"; this is just "which one
    /// am I looking at right now"). Null when a ROM group header is selected instead.</summary>
    [ObservableProperty] public partial MatchCandidate? SelectedCandidate { get; set; }

    [ObservableProperty] public partial Bitmap? PreviewImage { get; set; }
    [ObservableProperty] public partial string? PreviewError { get; set; }

    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameFilesCommand))]
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public MatchViewModel(MatchSettings settings, IMatchingService matchingService, IRenameService renameService)
    {
        _settings = settings;
        _matchingService = matchingService;
        _renameService = renameService;
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public MatchViewModel() : this(new MatchSettings(), new MatchingService(), new RenameService())
    {
    }

    partial void OnSelectedTreeItemChanged(object? value) => SelectedCandidate = value as MatchCandidate;

    partial void OnSelectedCandidateChanged(MatchCandidate? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(MatchCandidate? candidate)
    {
        var requestId = ++_previewRequestId;
        PreviewError = null;

        if (candidate is null)
        {
            SetPreviewImage(null);
            return;
        }

        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(candidate.ImageFullPath);
                return new Bitmap(stream);
            });

            // A newer selection came in while this load was in flight — drop this result
            // rather than flash a stale image over the row the user has since moved to.
            if (requestId != _previewRequestId)
            {
                bitmap.Dispose();
                return;
            }

            SetPreviewImage(bitmap);
        }
        catch (Exception ex)
        {
            // Broad catch deliberately: a missing file, an unreadable/corrupt image, or a
            // codec failure should all just show a friendly message here, never crash or
            // silently leave the preview blank with no explanation.
            if (requestId != _previewRequestId)
                return;

            SetPreviewImage(null);
            PreviewError = $"Couldn't load image: {ex.Message}";
        }
    }

    private void SetPreviewImage(Bitmap? bitmap)
    {
        PreviewImage?.Dispose();
        PreviewImage = bitmap;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsBusy = true;
        IsMatchRunning = true;
        ProgressPercent = 0;
        _allGroups.Clear();
        Groups.Clear();
        SelectedTreeItem = null;
        _cts = new CancellationTokenSource();
        var progress = new Progress<MatchProgress>(p =>
        {
            StatusText = $"Scanning: {p.CurrentName} ({p.Current}/{p.Total})";
            ProgressPercent = p.PercentComplete;
        });

        try
        {
            var matches = await _matchingService.FindMatchesAsync(_settings, progress, _cts.Token);

            foreach (var group in matches
                         .GroupBy(m => m.RomFileName, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                _allGroups.Add(new RomMatchGroup(group.Key, group.OrderByDescending(c => c.ScorePercent)));
            }

            AvailableFileTypes.Clear();
            AvailableFileTypes.Add(FileTypeAll);
            foreach (var ext in _allGroups
                         .Select(g => Path.GetExtension(g.RomFileName))
                         .Where(ext => !string.IsNullOrEmpty(ext))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase))
            {
                AvailableFileTypes.Add(ext);
            }

            SelectedFileType = FileTypeAll;
            SelectedRegion = RegionFilter.All;
            ShowOnly100PercentMatches = false;
            ApplyFilters();

            var totalCandidates = Groups.Sum(g => g.Candidates.Count);
            StatusText = Groups.Count == 0
                ? "No matches found above the accuracy threshold."
                : $"Found {totalCandidates} candidate(s) across {Groups.Count} ROM(s). Select the ones to rename.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            IsMatchRunning = false;
        }
    }

    private bool CanStart() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsBusy;

    // Both of these — and the rename below — only act on Groups/VisibleCandidates, i.e.
    // whatever the current file-type/region filter actually shows. A row hidden by the
    // filter is left completely untouched, so switching filters back and forth never
    // causes an invisible, surprise selection.

    [RelayCommand]
    private void SelectBestMatched()
    {
        foreach (var group in Groups)
        {
            if (group.VisibleCandidates.Count == 0)
                continue;

            var best = group.VisibleCandidates.OrderByDescending(c => c.ScorePercent).First();
            foreach (var candidate in group.VisibleCandidates)
                candidate.IsSelected = candidate == best;
        }
    }

    [RelayCommand]
    private void SelectSingleMatches()
    {
        foreach (var group in Groups)
        {
            var only = group.VisibleCandidates.Count == 1 ? group.VisibleCandidates[0] : null;
            foreach (var candidate in group.VisibleCandidates)
                candidate.IsSelected = candidate == only;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in Groups)
            group.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in Groups)
            group.IsExpanded = false;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task RenameFilesAsync()
    {
        var selected = Groups.SelectMany(g => g.VisibleCandidates).Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nothing selected to rename.";
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            var summary = await _renameService.RenameAsync(selected, _settings, _cts.Token);
            StatusText = summary.ToStatusText();

            // The operation was attempted for all of these — clear their checkboxes so a
            // second click of "Rename Selected" doesn't just repeat the same batch. Ones
            // that were skipped/failed are still visible in the tree to re-select and retry.
            foreach (var candidate in selected)
                candidate.IsSelected = false;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Rename cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
