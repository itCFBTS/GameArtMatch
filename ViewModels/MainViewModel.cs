using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;
using GameArtMatch.Services;

namespace GameArtMatch.ViewModels;

/// <summary>
/// The single-window shell. Owns the shared MatchSettings (bound directly by the
/// Paths and Options tabs) and hands it to both MatchViewModel and ReportViewModel,
/// so there's one set of folder/option inputs feeding both the fuzzy-match wizard
/// and the missing/matched report — replacing the original's launcher window +
/// two separate top-level windows.
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

    /// <summary>Raised (via the view) to show the About dialog — kept out of the tab strip
    /// per the request to move it into a menu instead of a launcher button.</summary>
    public event System.EventHandler? AboutRequested;

    /// <summary>Raised (via the view) to show the Options dialog — moved out of its own
    /// tab and into File > Options, an Avalonia-idiomatic modal preferences window.</summary>
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
        ReportVm = new ReportViewModel(Settings, matchingService);

        MatchVm.ScanStarting += OnMatchScanStarting;
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
        });
    }

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void OpenOptions() => OptionsRequested?.Invoke(this, System.EventArgs.Empty);
}
