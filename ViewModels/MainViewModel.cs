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

        // Save immediately whenever a persisted field changes, rather than requiring an
        // explicit "Save Settings" step.
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        MatchVm = new MatchViewModel(Settings, matchingService, renameService);
        ReportVm = new ReportViewModel(Settings, matchingService);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MatchSettings.RomsRootPath) or nameof(MatchSettings.ImagesRootPath)
            or nameof(MatchSettings.IsConsoleMode) or nameof(MatchSettings.RomsIncludeSubfolders)
            or nameof(MatchSettings.ImagesIncludeSubfolders))
        {
            _settingsStore.Save(new PersistedSettings
            {
                RomsRootPath = Settings.RomsRootPath,
                ImagesRootPath = Settings.ImagesRootPath,
                IsConsoleMode = Settings.IsConsoleMode,
                RomsIncludeSubfolders = Settings.RomsIncludeSubfolders,
                ImagesIncludeSubfolders = Settings.ImagesIncludeSubfolders,
            });
        }
    }

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void OpenOptions() => OptionsRequested?.Invoke(this, System.EventArgs.Empty);
}
