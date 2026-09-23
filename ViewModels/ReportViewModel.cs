using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the sidebar's Report page (see ReportView) — just the Missing list now
/// (Matched was dropped; ignored ROMs are managed from the Options pane instead, see
/// IgnoredRomsViewModel). Reads directly from MatchViewModel.MissingRoms — a cached
/// side effect of that ViewModel's own last scan — rather than running a second,
/// redundant scan of its own just to reproduce the same list; that used to make opening
/// this window noticeably re-scan everything from scratch for no reason.</summary>
public partial class ReportViewModel : ViewModelBase
{
    private readonly MatchSettings _settings;
    private readonly MatchViewModel _matchViewModel;

    public ObservableCollection<ReportEntry> Missing { get; } = [];

    /// <summary>Just the leaf folder name of the currently-selected ROMs folder (e.g.
    /// "SMS"), or null if none is selected — used by ReportView.axaml.cs to name the
    /// exported file after whichever system this report is actually for.</summary>
    public string? RomsFolderName => string.IsNullOrWhiteSpace(_settings.RomsPath) ? null : _settings.RomsPathDisplayName;

    /// <summary>Raised whenever IgnoreRomsAsync changes MatchSettings.IgnoredRomPaths —
    /// MainViewModel listens to persist, the same pattern as MatchViewModel.RomIgnored
    /// and IgnoredRomsViewModel.IgnoredRomPathsChanged (this ViewModel never touches
    /// ISettingsStore directly).</summary>
    public event EventHandler? IgnoredRomPathsChanged;

    /// <summary>True while a scan runs — the page swaps its (by then stale) list for a
    /// "Scanning…" message until the new results land.</summary>
    public bool IsScanning => _matchViewModel.IsMatchRunning;

    /// <summary>Set once the first scan's missing list arrives — tells "no scan yet"
    /// apart from "scanned, nothing missing" in the empty state.</summary>
    private bool _hasScanned;

    public bool IsListVisible => !IsScanning && Missing.Count > 0;

    public bool IsEmptyStateVisible => !IsListVisible;

    public string EmptyStateText =>
        IsScanning ? "Scanning..."
        : !_hasScanned ? "Run a scan on the Match page to see which ROMs have no art."
        : "Every ROM in the last scan has art.";

    public ReportViewModel(MatchSettings settings, MatchViewModel matchViewModel)
    {
        _settings = settings;
        _matchViewModel = matchViewModel;
        _matchViewModel.PropertyChanged += OnMatchViewModelPropertyChanged;
        Missing.CollectionChanged += (_, _) => RaiseDisplayStateChanged();
    }

    private void OnMatchViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MatchViewModel.MissingRoms):
                _hasScanned = true;
                Refresh();
                break;
            case nameof(MatchViewModel.IsMatchRunning):
                OnPropertyChanged(nameof(IsScanning));
                RaiseDisplayStateChanged();
                break;
        }
    }

    private void RaiseDisplayStateChanged()
    {
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public ReportViewModel() : this(new MatchSettings(), new MatchViewModel())
    {
    }

    /// <summary>Copies MatchViewModel.MissingRoms as of right now — run whenever that
    /// list changes (a scan finishing, or an ignore pruning it; see
    /// OnMatchViewModelPropertyChanged), so the page is never stale and needs no Refresh
    /// button. Synchronous: just copying an already-computed list, no I/O.</summary>
    private void Refresh()
    {
        Missing.Clear();
        foreach (var entry in _matchViewModel.MissingRoms.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
            Missing.Add(entry);
    }

    /// <summary>Right-click action from the Missing tab — adds each entry's ROM to the
    /// persisted ignore list (it'll show up under Options > Ignored ROMs instead) and
    /// removes it from view immediately. Also prunes MatchViewModel's own cache (see
    /// RemoveFromMissingCache) so it doesn't silently reappear here on the next re-copy
    /// before the next full scan gets a chance to exclude it naturally.</summary>
    [RelayCommand]
    private void IgnoreRoms(IReadOnlyList<ReportEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
            return;

        var ignored = new List<string>();
        foreach (var entry in entries)
        {
            if (!_settings.IgnoredRomPaths.Add(entry.RomFullPath))
                continue;

            Missing.Remove(entry);
            ignored.Add(entry.RomFullPath);
        }

        if (ignored.Count == 0)
            return;

        _matchViewModel.RemoveFromMissingCache(ignored);
        IgnoredRomPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Right-click "Ignore Folder" action (see ReportEntry.
    /// IgnorableAncestorFolders and MatchView.axaml's equivalent ROM row menu) — adds
    /// the folder to MatchSettings.IgnoredRomFolders (everything under it gets excluded
    /// from every future scan too, see MatchingService.ListRoms), drops any
    /// currently-shown Missing entry that lives under it, and prunes MatchViewModel's
    /// cache the same way IgnoreRoms above does.</summary>
    [RelayCommand]
    private void IgnoreFolder(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !_settings.IgnoredRomFolders.Add(folderPath))
            return;

        var removed = Missing.Where(e => FolderAncestry.IsUnderFolder(e.RomFullPath, folderPath)).ToList();
        foreach (var entry in removed)
            Missing.Remove(entry);

        _matchViewModel.RemoveFromMissingCacheUnderFolder(folderPath);
        IgnoredRomPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Plain-text rendering of everything currently loaded, for the Export
    /// button (see ReportView.axaml.cs) — assembled from whatever Refresh last
    /// copied in, not re-queried, so Export always reflects exactly what's on screen.</summary>
    public string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GameArtMatch Report — generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        sb.AppendLine();
        sb.AppendLine($"=== Missing ({Missing.Count}) ===");
        foreach (var entry in Missing)
            sb.AppendLine(entry.RomFileName);

        return sb.ToString();
    }
}
