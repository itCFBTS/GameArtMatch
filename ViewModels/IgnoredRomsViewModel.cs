using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the Options window's "Ignored ROMs" tab — lets the user see and
/// un-ignore ROMs previously excluded via "Ignore Selected ROM(s)" (the Match tab's
/// results tree, or Report's Missing/Matched tabs), grouped by containing folder so an
/// entire system's worth of ignores (e.g. everything directly under /roms/Saturn) can
/// be collapsed at once — plus whole folders excluded via "Ignore Folder" (see
/// IgnoredFolders below). Replaces the Report window's old Ignored tab — same underlying
/// MatchSettings.IgnoredRomPaths/IgnoredRomFolders, just surfaced here now that it's
/// config/management rather than a scan result.</summary>
public partial class IgnoredRomsViewModel : ObservableObject
{
    private readonly MatchSettings _settings;

    public ObservableCollection<IgnoredRomGroup> Groups { get; } = [];

    public bool HasEntries => Groups.Count > 0;

    /// <summary>Whole folders ignored via "Ignore Folder" (see MatchView's ROM row context
    /// menu) — kept separate from Groups above (individually-ignored files) since these
    /// are two different underlying settings (MatchSettings.IgnoredRomFolders vs.
    /// IgnoredRomPaths); a file that merely happens to sit in an otherwise-normal folder
    /// shouldn't be confused with a folder that's excluded wholesale.</summary>
    public ObservableCollection<string> IgnoredFolders { get; } = [];

    public bool HasIgnoredFolders => IgnoredFolders.Count > 0;

    /// <summary>Raised whenever UnignoreRoms/UnignoreFolders changes MatchSettings'
    /// ignore lists — MainViewModel listens to persist, same pattern as
    /// MatchViewModel.RomIgnored and ReportViewModel.IgnoredRomPathsChanged.</summary>
    public event EventHandler? IgnoredRomPathsChanged;

    public IgnoredRomsViewModel(MatchSettings settings)
    {
        _settings = settings;
        Refresh();
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public IgnoredRomsViewModel() : this(new MatchSettings())
    {
    }

    /// <summary>Re-reads MatchSettings.IgnoredRomPaths — called at construction and again
    /// each time the Options window opens (see MainWindow.axaml.cs), since paths can
    /// change from the Match tab's or Report window's own "Ignore Selected ROM(s)" while
    /// this window was closed. Only paths that still exist on disk are shown — same
    /// "silently skip anything since deleted/moved" behavior the old Report Ignored tab
    /// had; nothing here prunes IgnoredRomPaths itself over a missing file.</summary>
    public void Refresh()
    {
        Groups.Clear();
        foreach (var folderGroup in _settings.IgnoredRomPaths
                     .Where(File.Exists)
                     .GroupBy(p => Path.GetDirectoryName(p) ?? "", StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entries = folderGroup
                .Select(p => new IgnoredRomEntry(p))
                .OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase);
            Groups.Add(new IgnoredRomGroup(folderGroup.Key, entries));
        }

        IgnoredFolders.Clear();
        foreach (var folder in _settings.IgnoredRomFolders
                     .Where(Directory.Exists)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            IgnoredFolders.Add(folder);
        }

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasIgnoredFolders));
    }

    [RelayCommand]
    private void UnignoreRoms(IReadOnlyList<string>? fullPaths)
    {
        if (fullPaths is null || fullPaths.Count == 0)
            return;

        var anyChanged = false;
        foreach (var path in fullPaths)
            if (_settings.IgnoredRomPaths.Remove(path))
                anyChanged = true;

        if (!anyChanged)
            return;

        Refresh();
        IgnoredRomPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UnignoreFolders(IReadOnlyList<string>? folderPaths)
    {
        if (folderPaths is null || folderPaths.Count == 0)
            return;

        var anyChanged = false;
        foreach (var folder in folderPaths)
            if (_settings.IgnoredRomFolders.Remove(folder))
                anyChanged = true;

        if (!anyChanged)
            return;

        Refresh();
        IgnoredRomPathsChanged?.Invoke(this, EventArgs.Empty);
    }
}
