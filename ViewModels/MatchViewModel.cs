using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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

    /// <summary>Final ROM count from the Listing phase's "ROMs" reports and the Listing
    /// phase's "images" reports, held onto so StartAsync's progress handler can keep
    /// showing both once they're no longer the phase actively being reported —
    /// MatchProgress reports are stateless per-call, so something has to remember each
    /// figure across the later reports that don't carry it anymore.</summary>
    private int _lastRomsListed;
    private int _lastImagesListed;

    /// <summary>Drives the "ROMs •  images" bouncing-dot separator shown once both
    /// figures above are final and Indexing/Matching are running — same DispatcherTimer-
    /// driven "steadily changing string" technique as ActivityDotsText (see MatchView.
    /// axaml.cs), just recomposing the whole StatusText each tick instead of a
    /// standalone TextBlock, since the two numbers around it live here already.</summary>
    private DispatcherTimer? _separatorTimer;
    private int _separatorFrameIndex;

    /// <summary>A dot bouncing back and forth between the two numbers it separates —
    /// fixed-width so the surrounding text doesn't jitter as it moves. Purely decorative:
    /// once both counts are final, there's nothing left to report a real number for
    /// until Matching finishes (Indexing/Matching are typically fast enough that a
    /// specific X/Y count would just flicker by unreadably anyway — see the
    /// conversation this replaced).</summary>
    private static readonly string[] SeparatorFrames =
    [
        "  •  ", " •   ", "•    ", " •   ", "  •  ", "   • ", "    •", "   • ",
    ];

    /// <summary>Every ROM that has at least one candidate, from the most recent scan —
    /// unfiltered. Groups (below) is the filtered view actually bound to the TreeView.</summary>
    private readonly List<RomMatchGroup> _allGroups = [];

    /// <summary>Looks up an existing group by RomFileName while a scan is streaming in —
    /// needed because a same-named ROM in two different subfolders arrives as two
    /// separate OnRomMatched calls (RomsIncludeSubfolders on), and those need to fold
    /// into ONE displayed group (see RomMatchGroup.MergeCandidates) rather than showing
    /// as two identically-named groups, matching what the old batch-built code did by
    /// grouping over the complete candidate list up front.</summary>
    private readonly Dictionary<string, RomMatchGroup> _groupsByRomFileName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which group each candidate belongs to — a MatchCandidate doesn't know its
    /// own group, but the preview carousel is per-ROM, so highlighting a candidate row
    /// has to find the ROM it sits under (see SyncCarousel). Reference-keyed; kept in
    /// step with _allGroups by OnRomMatched and the ignore commands.</summary>
    private readonly Dictionary<MatchCandidate, RomMatchGroup> _groupByCandidate = new(ReferenceEqualityComparer.Instance);

    /// <summary>The filtered view of _allGroups that ApplyFilters keeps in sync with the
    /// current file-type/region/100%-only filters — bound directly to the TreeView.
    /// Filtered-out ROMs are removed from this collection entirely (not just hidden),
    /// for the same reason VisibleCandidates removes filtered-out images — see there.</summary>
    public ObservableCollection<RomMatchGroup> Groups { get; } = [];

    /// <summary>ROMs the last scan found zero candidates for above the accuracy
    /// threshold — a side effect of the same StartAsync pass (see MatchingService.
    /// FindMatchesAsync/MatchScanResult), read directly by ReportViewModel for the
    /// Report window's Missing tab instead of that window running its own separate,
    /// redundant re-scan just to reproduce the same list. Empty until the first scan
    /// ever completes.</summary>
    public IReadOnlyList<ReportEntry> MissingRoms { get; private set; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = "Press 'Start' when ready.";

    /// <summary>0-100, driven by MatchingService's throttled progress reports during a
    /// scan — a real determinate progress bar, since the ROM count is known upfront.</summary>
    [ObservableProperty] public partial double ProgressPercent { get; set; }

    /// <summary>True only while a Start scan is actually running — separate from the
    /// broader IsBusy (which also covers Rename) so the progress bar doesn't show a
    /// stale matching percentage during an unrelated rename operation.</summary>
    [ObservableProperty] public partial bool IsMatchRunning { get; set; }

    /// <summary>True from the moment Start is clicked until MatchingService starts
    /// scoring ROMs against the image index (MatchPhase.Matching) — covers both the
    /// Listing phase (enumerating ROM/image files) and the Indexing phase (tokenizing
    /// every image), neither of which has per-item feedback of its own, so the view
    /// shows the activity-dots cue over them instead of a bar that looks frozen.</summary>
    [ObservableProperty] public partial bool IsIndexing { get; set; }

    /// <summary>Fraction of the single overall progress bar given to the Indexing phase
    /// before Scanning fills the rest. Fixed rather than weighted by relative image/ROM
    /// counts, since indexing (tokenize one image) and scanning (score one ROM against
    /// the index) aren't comparable units of work.</summary>
    private const double IndexingPhaseWeight = 0.15;

    // --- Match tab filters: a post-scan display filter, doesn't affect scoring or
    // re-run matching — just shows/hides already-computed rows (see ApplyFilters). ---

    /// <summary>Populated after each scan from the distinct ROM extensions actually
    /// present in the results — "All" plus whatever's really there, not a fixed list.</summary>
    public ObservableCollection<string> AvailableFileTypes { get; } = [FileTypeAll];

    private const string FileTypeAll = "All";

    [ObservableProperty] public partial string SelectedFileType { get; set; } = FileTypeAll;

    /// <summary>Populated after each scan from the regions actually found among that
    /// scan's ROM/image filenames — "All" plus whichever of RegionFilter.Options are
    /// actually present, same "don't offer choices that can't do anything" approach as
    /// AvailableFileTypes above.</summary>
    public ObservableCollection<string> AvailableRegions { get; } = [RegionFilter.All];

    [ObservableProperty] public partial string SelectedRegion { get; set; } = RegionFilter.All;

    /// <summary>When on, only candidates scoring a full 100% are shown — a quick way to
    /// see just the sure things, hiding everything that still needs a human look. Kept
    /// as a >= 100 score check rather than IsExactMatch: the redefined tag-inclusive
    /// score already means only true exact/near-exact matches reach 100, so this stays
    /// simple and matches the checkbox's "Exact score only" wording. IsExactMatch remains
    /// the stricter, ground-truth flag (visual marker + best-match tie-break) in the rare
    /// case the two ever diverge.</summary>
    [ObservableProperty] public partial bool ShowOnlyExactScoreMatches { get; set; }

    /// <summary>When on, collapses each byte-identical-content cluster (see
    /// MatchCandidate.ContentHash/IsSameAsAbove) down to just its first — best-scoring —
    /// member, hiding the rest instead of merely labeling them. A candidate already
    /// filtered out by region/exact-score stays out regardless of this setting.</summary>
    [ObservableProperty] public partial bool HideSameImages { get; set; }

    partial void OnSelectedFileTypeChanged(string value) => ApplyFilters();

    partial void OnSelectedRegionChanged(string value) => ApplyFilters();

    partial void OnShowOnlyExactScoreMatchesChanged(bool value) => ApplyFilters();

    partial void OnHideSameImagesChanged(bool value) => ApplyFilters();

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
        var highlighted = SelectedTreeItem;
        _suppressCarouselSync = true;

        Groups.Clear();

        foreach (var group in _allGroups)
            if (ApplyFiltersToGroup(group))
                Groups.Add(group);

        // A filter can hide a previously-selected candidate (or reveal previously-hidden
        // ones) without any single candidate's own IsSelected value changing, so this
        // needs its own explicit re-check rather than relying solely on
        // OnCandidatePropertyChanged.
        RenameFilesCommand.NotifyCanExecuteChanged();

        RestoreTreeSelectionAfterFilter(highlighted);
    }

    /// <summary>Puts the tree's highlight back after ApplyFilters has rebuilt Groups from
    /// scratch — Groups.Clear() can drop the TreeView's SelectedItem (bounced back into
    /// SelectedTreeItem as null), and the row itself may legitimately be gone now. Falls
    /// through the same "what's the nearest thing still visible" ladder either way:
    /// the exact row if it survived; otherwise, for a candidate, the still-visible leader
    /// of its identical-content cluster (exactly what Hide Same Images collapses a "Same
    /// as above" row into — the carousel doesn't move, since it's the same image);
    /// otherwise the ROM's header row; otherwise nothing, and the preview pane closes.
    /// SyncCarousel runs once here rather than on every intermediate selection change
    /// during the rebuild (see _suppressCarouselSync), so the carousel never sees the
    /// transient "nothing selected" state and never drops its decoded bitmaps.</summary>
    private void RestoreTreeSelectionAfterFilter(object? highlighted)
    {
        var group = GroupFor(highlighted);
        object? restored = null;
        if (group is not null && Groups.Contains(group))
        {
            if (highlighted is MatchCandidate candidate)
            {
                restored = group.VisibleCandidates.Contains(candidate)
                    ? candidate
                    : group.VisibleCandidates.FirstOrDefault(c => c.ContentHash == candidate.ContentHash) ?? (object)group;
            }
            else
            {
                restored = group;
            }
        }

        _suppressCarouselSync = false;
        if (Equals(SelectedTreeItem, restored))
            SyncCarousel(); // no change to raise OnSelectedTreeItemChanged — sync explicitly, the visible set may still differ
        else
            SelectedTreeItem = restored;
    }

    /// <summary>The per-group half of ApplyFilters' work, pulled out so a scan streaming
    /// results in live (see OnRomMatched) can filter/shade just the one group that just
    /// arrived instead of rebuilding the whole Groups collection from scratch every time
    /// a new ROM's results come in. Returns whether the group has any visible candidate
    /// left (i.e. whether it belongs in Groups at all) — same semantics ApplyFilters'
    /// loop used to check inline.</summary>
    private bool ApplyFiltersToGroup(RomMatchGroup group)
    {
        var romTypeOk = SelectedFileType == FileTypeAll ||
                         string.Equals(Path.GetExtension(group.RomFileName), SelectedFileType, StringComparison.OrdinalIgnoreCase);
        var romRegionOk = RegionFilter.Matches(group.RomFileName, SelectedRegion, isImage: false);

        group.VisibleCandidates.Clear();
        if (romTypeOk && romRegionOk)
        {
            var candidatesPassingBasicFilters = new List<MatchCandidate>();
            foreach (var candidate in group.Candidates)
            {
                var regionOk = RegionFilter.Matches(candidate.ImageFileName, SelectedRegion, isImage: true);
                var scoreOk = !ShowOnlyExactScoreMatches || candidate.ScorePercent >= 100;
                if (regionOk && scoreOk)
                    candidatesPassingBasicFilters.Add(candidate);
            }

            // HideSameImages collapses a run of identical-content candidates down to
            // just the first (best-scoring, since RomMatchGroup already clusters them
            // in best-match order) — comparing against the last KEPT hash, not just the
            // previous candidate, so a whole run of 3+ duplicates collapses correctly
            // rather than only dropping every other one.
            var visible = new List<MatchCandidate>();
            string? lastKeptHash = null;
            foreach (var candidate in candidatesPassingBasicFilters)
            {
                if (HideSameImages && candidate.ContentHash == lastKeptHash)
                    continue;
                visible.Add(candidate);
                lastKeptHash = candidate.ContentHash;
            }

            // How many VISIBLE candidates share each content hash — a count of 1 means
            // a singleton, which gets no "Same as above" label and no background shade
            // at all. Counted against the final visible set (post-HideSameImages), for
            // the same "depends on what's actually shown" reason as the label itself —
            // when HideSameImages is on, every surviving candidate is unique-in-the-list
            // by construction, so shading naturally turns itself off with no special case.
            var hashCounts = visible
                .GroupBy(c => c.ContentHash)
                .ToDictionary(g => g.Key, g => g.Count());

            string? lastHash = null;
            var altShade = false;
            foreach (var candidate in visible)
            {
                var isMulti = hashCounts[candidate.ContentHash] > 1;

                // "Same as above" is relative to whatever's currently VISIBLE, not
                // fixed at scan time — so if a filter hides the representative of a
                // content-identical cluster, the next surviving member correctly
                // stops claiming to be "the same as" a row that isn't shown anymore.
                candidate.IsSameAsAbove = candidate.ContentHash == lastHash;

                if (candidate.ContentHash != lastHash)
                {
                    // Flip only on entering a new MULTI-member cluster, so a singleton
                    // sitting between two duplicate clusters doesn't consume a color
                    // slot — RomMatchGroup already clusters identical content adjacent,
                    // so this only ever toggles between genuinely distinct clusters.
                    if (isMulti)
                        altShade = !altShade;
                    lastHash = candidate.ContentHash;
                }

                candidate.IsContentShadeA = isMulti && !altShade;
                candidate.IsContentShadeB = isMulti && altShade;

                group.VisibleCandidates.Add(candidate);
            }
        }

        return group.VisibleCandidates.Count > 0;
    }

    /// <summary>Whatever's currently highlighted in the tree — a RomMatchGroup or a
    /// MatchCandidate, since both levels are selectable. Only a MatchCandidate drives
    /// the preview panel (see SelectedCandidate below).</summary>
    [ObservableProperty] public partial object? SelectedTreeItem { get; set; }

    /// <summary>The candidate currently being previewed — independent of any row's own
    /// IsSelected checkbox (that's "include in rename batch"; this is just "which one
    /// am I looking at right now"). Null when a ROM group header is selected instead.</summary>
    [ObservableProperty] public partial MatchCandidate? SelectedCandidate { get; set; }

    /// <summary>The right-hand art pane — one slide per DISTINCT image among the
    /// highlighted ROM's visible candidates, kept in step with the tree both ways (see
    /// SyncCarousel and PreviewCarouselViewModel's own doc comment).</summary>
    public PreviewCarouselViewModel Carousel { get; } = new();

    /// <summary>True while a ROM (header row or any of its candidates) is highlighted and
    /// has at least one visible candidate — MatchView collapses the pane's grid columns
    /// to zero width when false, not just the pane itself.</summary>
    [ObservableProperty] public partial bool IsPreviewVisible { get; set; }

    /// <summary>The ROM whose images the carousel is currently showing, or null while the
    /// pane is hidden — lets OnRomMatched notice when a mid-scan merge changes the very
    /// group being previewed.</summary>
    private RomMatchGroup? _previewGroup;

    /// <summary>Set for the duration of ApplyFilters' rebuild — see RestoreTreeSelectionAfterFilter.</summary>
    private bool _suppressCarouselSync;

    /// <summary>Raised right as a scan begins — MainViewModel listens for this to
    /// remember which Images folder was paired with the current ROMs folder, so
    /// picking that ROMs folder again later auto-recalls it. Deliberately fires on
    /// Start (not on every folder pick), matching "when a start scan was hit".</summary>
    public event System.EventHandler? ScanStarting;

    /// <summary>Raised right after a ROM is added to MatchSettings.IgnoredRomPaths —
    /// MainViewModel listens for this to persist the updated ignore list (it's the sole
    /// ISettingsStore owner; this ViewModel only ever mutates the shared, in-memory
    /// HashSet directly).</summary>
    public event System.EventHandler? RomIgnored;

    [NotifyCanExecuteChangedFor(nameof(RenameFilesCommand))]
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string StartCancelButtonText => IsBusy ? "Cancel" : "Start";

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(StartCancelButtonText));

    public MatchViewModel(MatchSettings settings, IMatchingService matchingService, IRenameService renameService)
    {
        _settings = settings;
        _matchingService = matchingService;
        _renameService = renameService;

        // Carousel -> tree: user navigated the carousel, so highlight the matching row.
        // That highlight comes straight back through OnSelectedTreeItemChanged ->
        // SyncCarousel -> Carousel.Show, which resolves to the slide already selected.
        Carousel.RepresentativeChosen += candidate => SelectedTreeItem = candidate;
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public MatchViewModel() : this(new MatchSettings(), new MatchingService(), new RenameService())
    {
    }

    partial void OnSelectedTreeItemChanged(object? value)
    {
        SelectedCandidate = value as MatchCandidate;
        if (!_suppressCarouselSync)
            SyncCarousel();
    }

    /// <summary>The ROM a tree row belongs to — the row itself for a header, its owning
    /// group for a candidate.</summary>
    private RomMatchGroup? GroupFor(object? treeItem) => treeItem switch
    {
        RomMatchGroup group => group,
        MatchCandidate candidate => _groupByCandidate.GetValueOrDefault(candidate),
        _ => null,
    };

    /// <summary>Tree -> carousel. Hands the carousel the highlighted ROM's VISIBLE
    /// candidates (the same collection the tree shows, so region/exact-score/Hide Same
    /// Images filters apply identically to both) and asks it to land on the highlighted
    /// candidate's ContentHash — the carousel collapses identical content itself, so a
    /// highlighted "Same as above" row resolves to the same slide as the row above it
    /// and the image simply doesn't move. Hides the pane when nothing (or a ROM with no
    /// visible candidates) is highlighted.</summary>
    private void SyncCarousel()
    {
        var group = GroupFor(SelectedTreeItem);
        if (group is null || group.VisibleCandidates.Count == 0)
        {
            _previewGroup = null;
            Carousel.Clear();
            IsPreviewVisible = false;
            return;
        }

        _previewGroup = group;
        Carousel.Show(group.VisibleCandidates, SelectedCandidate?.ContentHash);
        IsPreviewVisible = true;
    }

    /// <summary>Single Start/Cancel button's command — dispatches on IsBusy rather than
    /// exposing two separate commands, so there's only ever one button to disable/relabel
    /// in sync with the other (see StartCancelButtonText). Fire-and-forget on the Start
    /// branch is deliberate: StartAsync manages its own IsBusy try/finally and the button's
    /// feedback comes from that property changing, not from awaiting this call.</summary>
    [RelayCommand]
    private void ToggleStartCancel()
    {
        if (IsBusy)
            Cancel();
        else
            _ = StartAsync();
    }

    private async Task StartAsync()
    {
        ScanStarting?.Invoke(this, EventArgs.Empty);

        IsBusy = true;
        IsMatchRunning = true;
        IsIndexing = true;
        ProgressPercent = 0;
        StatusText = "Preparing scan";
        _lastRomsListed = 0;
        _lastImagesListed = 0;
        _separatorFrameIndex = 0;
        _separatorTimer?.Stop();
        _separatorTimer = null;
        _allGroups.Clear();
        _groupsByRomFileName.Clear();
        _groupByCandidate.Clear();
        Groups.Clear();
        SelectedTreeItem = null;

        // Reset up front rather than after the scan — results now stream into Groups
        // live (see OnRomMatched), so the filter dropdowns/selections need to already be
        // in their "fresh scan" state before the first result arrives, not after the last
        // one does.
        AvailableFileTypes.Clear();
        AvailableFileTypes.Add(FileTypeAll);
        AvailableRegions.Clear();
        AvailableRegions.Add(RegionFilter.All);
        SelectedFileType = FileTypeAll;
        SelectedRegion = RegionFilter.All;
        ShowOnlyExactScoreMatches = false;
        HideSameImages = false;
        AreGroupsExpanded = true; // matches RomMatchGroup's own default — see its doc comment for why expanding one group at a time as a scan streams in is cheap

        _cts = new CancellationTokenSource();

        var progress = new Progress<MatchProgress>(p =>
        {
            switch (p.Phase)
            {
                // Plain counts, no "found"/"scanned" labels — just the ROM figure,
                // joined by the image figure once that listing starts. _lastRomsListed
                // holds the ROM count so it stays on screen instead of being replaced
                // once CurrentName switches to "images" — see that field's declaration.
                case MatchPhase.Listing when p.CurrentName == "ROMs":
                    _lastRomsListed = p.Current;
                    StatusText = $"{_lastRomsListed:N0} ROMs";
                    break;
                case MatchPhase.Listing:
                    _lastImagesListed = p.Current;
                    StatusText = $"{_lastRomsListed:N0} ROMs, {_lastImagesListed:N0} images";
                    break;
                case MatchPhase.Indexing:
                    // No more "(X/Y)" here — once both counts are final there's nothing
                    // left worth reading a specific number for (Indexing/Matching are
                    // typically fast enough that an X/Y would just flicker by
                    // unreadably); StartSeparatorAnimation's bouncing dot is the "still
                    // working" cue for both this phase and Matching below instead. The
                    // bar itself still tracks real progress, just not narrated in text.
                    StartSeparatorAnimation();
                    ProgressPercent = p.PercentComplete * IndexingPhaseWeight;
                    break;
                case MatchPhase.Matching:
                    IsIndexing = false;
                    StartSeparatorAnimation();
                    ProgressPercent = IndexingPhaseWeight * 100 + p.PercentComplete * (1 - IndexingPhaseWeight);
                    break;
            }
        });

        // Local, not a private method — captures StatusText updates against the two
        // fields above via the same closure the "ROMs"/images cases already write to,
        // and there's no reason for anything outside this one progress handler to ever
        // start it.
        void StartSeparatorAnimation()
        {
            if (_separatorTimer is not null)
                return;

            _separatorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _separatorTimer.Tick += (_, _) =>
            {
                _separatorFrameIndex = (_separatorFrameIndex + 1) % SeparatorFrames.Length;
                StatusText = $"{_lastRomsListed:N0} ROMs{SeparatorFrames[_separatorFrameIndex]}{_lastImagesListed:N0} images";
            };
            _separatorTimer.Start();
        }

        var romMatched = new Progress<RomMatchResult>(OnRomMatched);

        try
        {
            var scanResult = await _matchingService.FindMatchesAsync(_settings, progress, romMatched, _cts.Token);
            MissingRoms = scanResult.Missing;

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
            IsIndexing = false;
            _separatorTimer?.Stop();
            _separatorTimer = null;
        }
    }

    /// <summary>Consumes MatchingService's live per-ROM stream (see RomMatchResult) —
    /// called once per ROM, well before the whole scan finishes, so results appear in
    /// the tree as they're computed instead of arriving all at once in a single
    /// multi-second UI freeze at the end. A same-named ROM in a different subfolder
    /// (RomsIncludeSubfolders on) arrives as a second call for a RomFileName already
    /// seen this scan — folded into the existing group via RomMatchGroup.MergeCandidates
    /// rather than creating a second, wrongly-separate group, matching what the old
    /// batch-built code did by grouping over the complete result up front.</summary>
    private void OnRomMatched(RomMatchResult result)
    {
        if (_groupsByRomFileName.TryGetValue(result.RomFileName, out var existingGroup))
        {
            existingGroup.MergeCandidates(result.Candidates);
            foreach (var candidate in result.Candidates)
            {
                candidate.PropertyChanged += OnCandidatePropertyChanged;
                _groupByCandidate[candidate] = existingGroup;
            }
            RegisterRegionsFrom(existingGroup, result.Candidates);

            if (ApplyFiltersToGroup(existingGroup) && !Groups.Contains(existingGroup))
                Groups.Add(existingGroup);
            if (existingGroup == _previewGroup)
                SyncCarousel(); // its visible set just changed under the carousel
            RenameFilesCommand.NotifyCanExecuteChanged();
            return;
        }

        var romGroup = new RomMatchGroup(result.RomFileName, result.Candidates.OrderByBestMatch(), _settings.RomsPath);
        romGroup.IsExpanded = AreGroupsExpanded; // matches the default (expanded) unless a mid-scan "Collapse All" click flipped it
        // So RenameFilesCommand's enabled state can react to a checkbox toggle anywhere
        // in the tree — see OnCandidatePropertyChanged.
        foreach (var candidate in romGroup.Candidates)
        {
            candidate.PropertyChanged += OnCandidatePropertyChanged;
            _groupByCandidate[candidate] = romGroup;
        }

        _allGroups.Add(romGroup);
        _groupsByRomFileName.Add(result.RomFileName, romGroup);
        RegisterFileType(romGroup.RomFileName);
        RegisterRegionsFrom(romGroup, romGroup.Candidates);

        if (ApplyFiltersToGroup(romGroup))
            Groups.Add(romGroup); // no sorted-insert needed — MatchingService.ListRoms now hands out ROMs pre-sorted

        RenameFilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Grows AvailableFileTypes incrementally as each new extension is first
    /// seen while a scan streams in, instead of discovering the whole set once at the
    /// end — keeps the same alphabetical order the old one-shot .OrderBy(...) produced.</summary>
    private void RegisterFileType(string romFileName)
    {
        var ext = Path.GetExtension(romFileName);
        if (string.IsNullOrEmpty(ext) || AvailableFileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return;

        var insertAt = 1; // index 0 is always FileTypeAll
        while (insertAt < AvailableFileTypes.Count &&
               string.Compare(AvailableFileTypes[insertAt], ext, StringComparison.OrdinalIgnoreCase) < 0)
            insertAt++;
        AvailableFileTypes.Insert(insertAt, ext);
    }

    /// <summary>Grows AvailableRegions incrementally as each new region is first found in
    /// an arriving group's ROM/candidate filenames, instead of discovering the whole set
    /// once at the end — inserts at each region's canonical rank in RegionFilter.Options
    /// (not discovery order), matching the old one-shot result exactly.</summary>
    private void RegisterRegionsFrom(RomMatchGroup group, IEnumerable<MatchCandidate> candidates)
    {
        foreach (var region in RegionFilter.Options)
        {
            if (region == RegionFilter.All || AvailableRegions.Contains(region))
                continue;

            // English Translated only ever restricts ROMs (RegionFilter.Matches exempts
            // images from it entirely, always returning true for them) — so checking
            // image filenames here would make it look "found" even when no ROM actually
            // carries the marker. Every other region legitimately shows up on either side.
            var found = region == RegionFilter.EnglishTranslated
                ? RegionFilter.Matches(group.RomFileName, region, isImage: false)
                : RegionFilter.Matches(group.RomFileName, region, isImage: false)
                  || candidates.Any(c => RegionFilter.Matches(c.ImageFileName, region, isImage: true));
            if (!found)
                continue;

            var insertAt = AvailableRegions.Count;
            for (var i = 1; i < AvailableRegions.Count; i++)
            {
                if (Array.IndexOf(RegionFilter.Options, AvailableRegions[i]) > Array.IndexOf(RegionFilter.Options, region))
                {
                    insertAt = i;
                    break;
                }
            }
            AvailableRegions.Insert(insertAt, region);
        }
    }

    /// <summary>Mirrors exactly what RenameFilesAsync itself acts on — a VISIBLE and
    /// selected candidate — so the button disables itself the moment there's nothing to
    /// rename, rather than being clickable and just showing a "Nothing selected" message.</summary>
    private bool CanRenameFiles() => !IsBusy && Groups.Any(g => g.VisibleCandidates.Any(c => c.IsSelected));

    /// <summary>Fires RenameFilesCommand's CanExecute re-check whenever any candidate's
    /// checkbox toggles anywhere in the tree — IsSelected isn't a single ObservableProperty
    /// on this ViewModel (it's spread across every MatchCandidate), so it can't use the
    /// usual [NotifyCanExecuteChangedFor] attribute the way IsBusy does above.</summary>
    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MatchCandidate.IsSelected))
            RenameFilesCommand.NotifyCanExecuteChanged();
    }

    private void Cancel() => _cts?.Cancel();

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

            var best = group.VisibleCandidates.OrderByBestMatch().First();
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
    private void DeselectAll()
    {
        foreach (var group in Groups)
            foreach (var candidate in group.VisibleCandidates)
                candidate.IsSelected = false;
    }

    /// <summary>Right-click action on a ROM row (or a multi-selection of them, via the
    /// TreeView's ctrl/shift/Ctrl+A multi-select — see MatchView.axaml.cs) — adds each to
    /// the persisted ignore list (see MatchSettings.IgnoredRomPaths) and removes it from
    /// the current results immediately, so it disappears from this scan too, not just
    /// future ones. Batched into one ApplyFilters/RomIgnored regardless of count, rather
    /// than once per ROM, since Ctrl+A could realistically select a large number at once.</summary>
    [RelayCommand]
    private void IgnoreRoms(IReadOnlyList<RomMatchGroup>? groups)
    {
        if (groups is null || groups.Count == 0)
            return;

        var anyIgnored = false;
        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.RomFullPath))
                continue;

            if (_settings.IgnoredRomPaths.Add(group.RomFullPath))
            {
                ForgetGroup(group);
                anyIgnored = true;
            }
        }

        if (!anyIgnored)
            return;

        ApplyFilters();
        RomIgnored?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops a group from every per-scan lookup at once — the caller's following
    /// ApplyFilters then takes it out of Groups (and, via RestoreTreeSelectionAfterFilter,
    /// closes the preview pane if that was the ROM being shown).</summary>
    private void ForgetGroup(RomMatchGroup group)
    {
        _allGroups.Remove(group);
        _groupsByRomFileName.Remove(group.RomFileName);
        foreach (var candidate in group.Candidates)
            _groupByCandidate.Remove(candidate);
    }

    /// <summary>Right-click "Ignore Folder" action (see MatchView.axaml's ROM row context
    /// menu, populated from RomMatchGroup.IgnorableAncestorFolders) — adds the folder to
    /// MatchSettings.IgnoredRomFolders (everything under it gets excluded from every
    /// future scan too, see MatchingService.ListRoms), and drops any currently-shown
    /// group whose ROM lives under it so it disappears from this scan immediately,
    /// same as IgnoreRoms above.</summary>
    [RelayCommand]
    private void IgnoreFolder(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !_settings.IgnoredRomFolders.Add(folderPath))
            return;

        var removed = _allGroups.Where(g => FolderAncestry.IsUnderFolder(g.RomFullPath, folderPath)).ToList();
        foreach (var group in removed)
            ForgetGroup(group);

        // Keeps the Report window's Missing tab in sync — otherwise a folder ignored
        // here could still hide an already-cached "missing" entry for a ROM that
        // happened to sit under it, until the next full scan naturally excludes it.
        RemoveFromMissingCacheUnderFolder(folderPath);

        ApplyFilters();
        RomIgnored?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called by ReportViewModel when a ROM gets individually ignored from the
    /// Report window's Missing list — MissingRoms is a snapshot from whenever the last
    /// scan ran, so simply mutating IgnoredRomPaths elsewhere wouldn't otherwise remove
    /// it from this cache until the next full scan happens to exclude it.</summary>
    public void RemoveFromMissingCache(IReadOnlyCollection<string> romFullPaths)
    {
        if (MissingRoms.Count == 0 || romFullPaths.Count == 0)
            return;

        var set = new HashSet<string>(romFullPaths, StringComparer.OrdinalIgnoreCase);
        MissingRoms = MissingRoms.Where(m => !set.Contains(m.RomFullPath)).ToList();
    }

    /// <summary>Same idea as RemoveFromMissingCache above, but for ReportViewModel's own
    /// "Ignore Folder" action (see ReportEntry.IgnorableAncestorFolders) rather than an
    /// exact-path ignore.</summary>
    public void RemoveFromMissingCacheUnderFolder(string folderPath)
    {
        if (MissingRoms.Count == 0)
            return;

        MissingRoms = MissingRoms.Where(m => !FolderAncestry.IsUnderFolder(m.RomFullPath, folderPath)).ToList();
    }

    /// <summary>Tracks which action the toggle button performs next, independent of any
    /// individual group's own expand state (a group can still be expanded/collapsed
    /// one at a time via the tree itself) — this is purely "what happens if I click the
    /// button now", not a live reflection of the tree's actual mixed state.</summary>
    [ObservableProperty] public partial bool AreGroupsExpanded { get; set; } = true;

    public string ExpandCollapseButtonText => AreGroupsExpanded ? "Collapse All" : "Expand All";

    partial void OnAreGroupsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandCollapseButtonText));

    /// <summary>Cancels an in-flight ToggleExpandCollapseAsync batch when a second click
    /// arrives before the first finishes — the newer click's desired end state wins
    /// rather than the two racing to set IsExpanded out of order.</summary>
    private CancellationTokenSource? _expandCollapseCts;

    /// <summary>Expanding (or collapsing) every group at once means the TreeView has to
    /// realize/lay out every candidate row across the whole tree in one synchronous
    /// burst — exactly the multi-second stall a live-streamed scan avoids by having
    /// groups arrive collapsed and one at a time (see OnRomMatched). Setting IsExpanded
    /// on all of them in a single foreach reintroduces that same stall at this button
    /// instead. Batching the assignment with a yield every BatchSize groups spreads that
    /// same realization cost across several render frames instead of one, the same
    /// "don't do it all in one uninterrupted burst" fix, just applied here since the
    /// data (unlike a scan's results) is already fully in memory — there's nothing to
    /// stream, only the UI-side realization cost to spread out.</summary>
    private const int ExpandCollapseBatchSize = 40;

    [RelayCommand]
    private async Task ToggleExpandCollapseAsync()
    {
        _expandCollapseCts?.Cancel();
        var cts = _expandCollapseCts = new CancellationTokenSource();
        var token = cts.Token;

        var expand = !AreGroupsExpanded;
        AreGroupsExpanded = expand; // flips the button's label immediately, before the batch below even starts

        var sinceYield = 0;
        foreach (var group in Groups)
        {
            group.IsExpanded = expand;

            if (++sinceYield >= ExpandCollapseBatchSize)
            {
                sinceYield = 0;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (token.IsCancellationRequested)
                    return;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRenameFiles))]
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
