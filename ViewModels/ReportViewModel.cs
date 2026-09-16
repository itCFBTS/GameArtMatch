using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the Report window (see ReportWindow) — just the Missing tab now
/// (Matched was dropped; ignored ROMs are managed from the Options window instead, see
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

    public ReportViewModel(MatchSettings settings, MatchViewModel matchViewModel)
    {
        _settings = settings;
        _matchViewModel = matchViewModel;
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public ReportViewModel() : this(new MatchSettings(), new MatchViewModel())
    {
    }

    /// <summary>Copies MatchViewModel.MissingRoms as of right now — run automatically as
    /// soon as the Report window opens (see ReportWindow.axaml.cs), and re-runnable via
    /// the window's own Refresh button afterward, e.g. after running a fresh scan on the
    /// Match tab while the Report window stays open. Synchronous: unlike the old
    /// scan-based version, this is just copying an already-computed list, no I/O.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Missing.Clear();
        foreach (var entry in _matchViewModel.MissingRoms.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
            Missing.Add(entry);
    }

    /// <summary>Right-click action from the Missing tab — adds each entry's ROM to the
    /// persisted ignore list (it'll show up under Options > Ignored ROMs instead) and
    /// removes it from view immediately. Also prunes MatchViewModel's own cache (see
    /// RemoveFromMissingCache) so it doesn't silently reappear here on a later Refresh
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
    /// populated, not re-queried, so Export always reflects exactly what's on screen.</summary>
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
