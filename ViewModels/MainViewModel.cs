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
    /// Settings Folder" button (see OptionsView.axaml.cs).</summary>
    public string SettingsFolderPath => _settingsStore.FolderPath;

    /// <summary>Which sidebar destination is showing — Match (false) or Report (true).
    /// MainWindow keeps both views alive and just toggles visibility, rather than
    /// swapping ContentControl.Content, so flipping pages never rebuilds the results
    /// tree (ViewLocator would construct a fresh MatchView on every swap).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMatchPageActive))]
    public partial bool IsReportPageActive { get; set; }

    public bool IsMatchPageActive => !IsReportPageActive;


    [ObservableProperty] public partial bool IsSidebarOpen { get; set; } = true;

    /// <summary>The floating Options pane MainWindow shows over everything while
    /// IsOptionsOpen — an in-window overlay (Claude-desktop-settings style) rather than
    /// a separate OS window.</summary>
    public OptionsViewModel OptionsVm { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial bool IsOptionsOpen { get; set; }

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
        OptionsVm = new OptionsViewModel(Settings, IgnoredRomsVm, SettingsFolderPath, persisted.ThemeId);
        OptionsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionsViewModel.SelectedTheme))
                Persist();
        };

        MatchVm.Noclipped += (_, _) => OnNoclip();
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
            ThemeId = PersistableThemeId(),
        });
    }

    /// <summary>Hidden themes (Level 0) are never saved: while one is active, settings
    /// keep the theme it was entered from, so the app never starts inside the easter
    /// egg — it's something you find, not somewhere you wake up.</summary>
    private string? PersistableThemeId() =>
        OptionsVm is null ? null
        : OptionsVm.SelectedTheme.IsHidden ? (_themeBeforeNoclip ?? Themes.ThemeCatalog.Default).Id
        : OptionsVm.SelectedTheme.Id;

    /// <summary>Raised when "/noclip" is typed into the Match page's search box — MainWindow
    /// plays the fluorescent-flicker transition and calls ApplyTheme at its darkest
    /// moment, so the theme swaps while the screen is out.</summary>
    public event EventHandler<NoclipEventArgs>? NoclipTransition;

    /// <summary>Theme to return to when noclipping back out of Level 0 — in-memory only;
    /// after a restart it's the default theme.</summary>
    private Themes.AppTheme? _themeBeforeNoclip;

    /// <summary>The easter egg: switches to the hidden Level 0 theme (never listed on the
    /// Appearance page — this is the only way in); typed again from Level 0, goes back to
    /// whatever theme came before.</summary>
    private void OnNoclip()
    {
        if (Themes.ThemeCatalog.TryFind(Themes.ThemeCatalog.NoclipThemeId) is not { } level0)
            return;

        var entering = !ReferenceEquals(OptionsVm.SelectedTheme, level0);
        var target = entering ? level0 : _themeBeforeNoclip ?? Themes.ThemeCatalog.Default;
        if (entering)
            _themeBeforeNoclip = OptionsVm.SelectedTheme;

        NoclipTransition?.Invoke(this, new NoclipEventArgs(entering, () => OptionsVm.SelectedTheme = target));
    }

    [RelayCommand]
    private void OpenOptions()
    {
        // Ignored ROMs can change (from the Match or Report page) while Options is
        // closed — re-read before showing rather than relying on whatever IgnoredRomsVm
        // last saw.
        IgnoredRomsVm.Refresh();
        IsOptionsOpen = true;
    }

    [RelayCommand(CanExecute = nameof(IsOptionsOpen))]
    private void CloseOptions() => IsOptionsOpen = false;

    [RelayCommand]
    private void ShowMatchPage() => IsReportPageActive = false;

    [RelayCommand]
    private void ShowReportPage() => IsReportPageActive = true;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;
}

/// <summary>See MainViewModel.NoclipTransition.</summary>
public sealed class NoclipEventArgs(bool entering, Action applyTheme) : EventArgs
{
    /// <summary>True going into Level 0, false coming back out.</summary>
    public bool Entering { get; } = entering;
    public Action ApplyTheme { get; } = applyTheme;
}
