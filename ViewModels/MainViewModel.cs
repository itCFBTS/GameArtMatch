using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;
using GameArtMatch.Services;

namespace GameArtMatch.ViewModels;

/// <summary>
/// The single-window shell (sidebar + Match/Report pages). Owns the shared
/// MatchSettings (bound directly by the sidebar's Sources section and Options) and
/// hands it to both MatchViewModel and ReportViewModel, so there's one set of
/// folder/option inputs feeding both the fuzzy-match wizard and the missing report —
/// replacing the original's launcher window + two separate top-level windows.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;

    /// <summary>ROMs folder -> Images folder, recorded whenever a scan is started (see
    /// MatchViewModel.ScanStarting) and consulted whenever RomsPath changes afterward,
    /// so re-selecting a ROMs folder you've used before auto-recalls its paired Images
    /// folder. Case-insensitive since paths may differ only by case across platforms.</summary>
    private readonly Dictionary<string, string> _romsToImagesPathMap;

    public MatchSettings Settings { get; }
    public MatchViewModel MatchVm { get; }
    public ReportViewModel ReportVm { get; }

    public IgnoredRomsViewModel IgnoredRomsVm { get; }

    /// <summary>Folder the persisted settings.json lives in — backs Options' "Open
    /// Settings Folder" button (see OptionsWindow.axaml.cs).</summary>
    public string SettingsFolderPath => _settingsStore.FolderPath;

    /// <summary>Which sidebar destination is showing — Match (false) or Report (true).
    /// MainWindow keeps both views alive and just toggles visibility, rather than
    /// swapping ContentControl.Content, so flipping pages never rebuilds the results
    /// tree (ViewLocator would construct a fresh MatchView on every swap).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMatchPageActive))]
    public partial bool IsReportPageActive { get; set; }

    public bool IsMatchPageActive => !IsReportPageActive;

    /// <summary>Report is a snapshot of the last scan's missing list — re-copy it each
    /// time the page is shown so it's never stale from an earlier scan.</summary>
    partial void OnIsReportPageActiveChanged(bool value)
    {
        if (value)
            ReportVm.RefreshCommand.Execute(null);
    }

    [ObservableProperty] public partial bool IsSidebarOpen { get; set; } = true;

    /// <summary>Raised (via the view) to show the Options dialog — reached from the
    /// sidebar's footer (or Ctrl+,), an Avalonia-idiomatic modal preferences window.</summary>
    public event System.EventHandler? OptionsRequested;

    public MainViewModel() : this(new MatchingService(), new RenameService(), new SettingsStore())
    {
    }

    public MainViewModel(IMatchingService matchingService, IRenameService renameService, ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        Settings = new MatchSettings();
        var persisted = _settingsStore.Load();
        Settings.RomsRootPath = persisted.RomsRootPath ?? "";
        Settings.ImagesRootPath = persisted.ImagesRootPath ?? "";
        Settings.IsConsoleMode = persisted.IsConsoleMode;

        // Nullable on purpose — only override MatchSettings' own (true) default when the
        // user has actually saved a choice before; an empty/fresh settings file must not
        // silently flip these back to false.
        if (persisted.RomsIncludeSubfolders.HasValue)
            Settings.RomsIncludeSubfolders = persisted.RomsIncludeSubfolders.Value;
        if (persisted.ImagesIncludeSubfolders.HasValue)
            Settings.ImagesIncludeSubfolders = persisted.ImagesIncludeSubfolders.Value;

        _romsToImagesPathMap = persisted.RomsToImagesPathMap is { } map
            ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (persisted.IgnoredRomPaths is { } ignored)
            foreach (var path in ignored)
                Settings.IgnoredRomPaths.Add(path);

        if (persisted.IgnoredRomFolders is { } ignoredFolders)
            foreach (var folder in ignoredFolders)
                Settings.IgnoredRomFolders.Add(folder);

        // Plain "resume where I left off" — restored as-is, deliberately BEFORE the
        // PropertyChanged subscription below attaches, so this exact restoration wins
        // at startup rather than the map (below) potentially overriding ImagesPath.
        Settings.RomsPath = persisted.LastRomsPath ?? "";
        Settings.ImagesPath = persisted.LastImagesPath ?? "";

        // Save immediately whenever a persisted field changes, rather than requiring an
        // explicit "Save Settings" step. Also — for RomsPath specifically — auto-recalls
        // whichever Images folder was last paired with it, if any (see the map above).
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        MatchVm = new MatchViewModel(Settings, matchingService, renameService);
        ReportVm = new ReportViewModel(Settings, MatchVm);
        IgnoredRomsVm = new IgnoredRomsViewModel(Settings);

        MatchVm.ScanStarting += OnMatchScanStarting;
        MatchVm.RomIgnored += OnRomIgnored;
        ReportVm.IgnoredRomPathsChanged += OnRomIgnored;
        IgnoredRomsVm.IgnoredRomPathsChanged += OnRomIgnored;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MatchSettings.RomsPath)
            && _romsToImagesPathMap.TryGetValue(Settings.RomsPath, out var rememberedImagesPath))
        {
            Settings.ImagesPath = rememberedImagesPath; // re-enters this handler once more for ImagesPath, then saves below
        }

        if (e.PropertyName is nameof(MatchSettings.RomsRootPath) or nameof(MatchSettings.ImagesRootPath)
            or nameof(MatchSettings.IsConsoleMode) or nameof(MatchSettings.RomsIncludeSubfolders)
            or nameof(MatchSettings.ImagesIncludeSubfolders) or nameof(MatchSettings.RomsPath)
            or nameof(MatchSettings.ImagesPath))
        {
            Persist();
        }
    }

    private void OnMatchScanStarting(object? sender, System.EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Settings.RomsPath) || string.IsNullOrWhiteSpace(Settings.ImagesPath))
            return; // nothing meaningful to remember yet

        _romsToImagesPathMap[Settings.RomsPath] = Settings.ImagesPath;
        Persist();
    }

    // Shared by MatchVm.RomIgnored and ReportVm.IgnoredRomPathsChanged — both just mean
    // "the ignore list changed, please persist it," regardless of whether a ROM was added
    // or removed, or from which window it happened.
    private void OnRomIgnored(object? sender, System.EventArgs e) => Persist();

    private void Persist()
    {
        _settingsStore.Save(new PersistedSettings
        {
            RomsRootPath = Settings.RomsRootPath,
            ImagesRootPath = Settings.ImagesRootPath,
            IsConsoleMode = Settings.IsConsoleMode,
            RomsIncludeSubfolders = Settings.RomsIncludeSubfolders,
            ImagesIncludeSubfolders = Settings.ImagesIncludeSubfolders,
            LastRomsPath = Settings.RomsPath,
            LastImagesPath = Settings.ImagesPath,
            RomsToImagesPathMap = new Dictionary<string, string>(_romsToImagesPathMap, StringComparer.OrdinalIgnoreCase),
            IgnoredRomPaths = new List<string>(Settings.IgnoredRomPaths),
            IgnoredRomFolders = new List<string>(Settings.IgnoredRomFolders),
        });
    }

    [RelayCommand]
    private void OpenOptions() => OptionsRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void ShowMatchPage() => IsReportPageActive = false;

    [RelayCommand]
    private void ShowReportPage() => IsReportPageActive = true;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;
}
