using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;
using GameArtMatch.Models;
using GameArtMatch.Services;
using GameArtMatch.Themes;

namespace GameArtMatch.ViewModels;

/// <summary>The Options pane's nav destinations — top-level (not nested in
/// OptionsViewModel) so XAML can pass one as a CommandParameter via x:Static.</summary>
public enum OptionsPage { Paths, Matching, Renaming, IgnoredRoms, Appearance, About }

/// <summary>
/// Backs the floating Options pane (see OptionsView, hosted over the main window by
/// MainWindow) — which page its sidebar nav has showing, plus everything the pages bind
/// to. Opening/closing is MainViewModel.IsOptionsOpen; this ViewModel lives as long as
/// the main window, so the pane reopens on whichever page was last shown.
/// </summary>
public partial class OptionsViewModel : ViewModelBase
{
    public MatchSettings Settings { get; }
    public IgnoredRomsViewModel IgnoredRomsVm { get; }
    public AboutViewModel AboutVm { get; } = new();

    /// <summary>Folder the persisted settings.json lives in — backs the pane's "Open
    /// Settings Folder" button (see OptionsView.axaml.cs).</summary>
    public string SettingsFolderPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathsPage), nameof(IsMatchingPage), nameof(IsRenamingPage),
        nameof(IsIgnoredRomsPage), nameof(IsAppearancePage), nameof(IsAboutPage), nameof(PageTitle))]
    public partial OptionsPage SelectedPage { get; set; } = OptionsPage.Paths;

    // One bool per page — each drives both that page's visibility and its nav row's
    // .active highlight, same as MainViewModel.IsMatchPageActive/IsReportPageActive.
    public bool IsPathsPage => SelectedPage == OptionsPage.Paths;
    public bool IsMatchingPage => SelectedPage == OptionsPage.Matching;
    public bool IsRenamingPage => SelectedPage == OptionsPage.Renaming;
    public bool IsIgnoredRomsPage => SelectedPage == OptionsPage.IgnoredRoms;
    public bool IsAppearancePage => SelectedPage == OptionsPage.Appearance;
    public bool IsAboutPage => SelectedPage == OptionsPage.About;

    public string PageTitle => SelectedPage switch
    {
        OptionsPage.Paths => "Paths",
        OptionsPage.Matching => "Matching",
        OptionsPage.Renaming => "Renaming",
        OptionsPage.IgnoredRoms => "Ignored ROMs",
        OptionsPage.Appearance => "Appearance",
        _ => "About",
    };

    /// <summary>What the Appearance page lists — every theme except hidden (easter-egg)
    /// ones, which are only ever reached their own way (Level 0: "/noclip").</summary>
    public IReadOnlyList<AppTheme> Themes { get; } = ThemeCatalog.All.Where(t => !t.IsHidden).ToList();

    /// <summary>The Appearance list's selection — SelectedTheme when it's a listed theme,
    /// otherwise nothing (a hidden theme is active). Separate from SelectedTheme so the
    /// ListBox, which can only select what it lists, never pushes null back into the
    /// active theme; picking a listed theme from Level 0 simply switches out of it.</summary>
    public AppTheme? ListedSelection
    {
        get => SelectedTheme.IsHidden ? null : SelectedTheme;
        set
        {
            if (value is not null)
                SelectedTheme = value;
        }
    }

    /// <summary>The active colour theme — applied live the moment it changes (the
    /// Appearance page's list binds straight to it). MainViewModel persists it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListedSelection))]
    public partial AppTheme SelectedTheme { get; set; }

    partial void OnSelectedThemeChanged(AppTheme value) => ThemeService.Apply(value);

    public OptionsViewModel(MatchSettings settings, IgnoredRomsViewModel ignoredRomsVm, string settingsFolderPath,
        string? themeId)
    {
        Settings = settings;
        IgnoredRomsVm = ignoredRomsVm;
        SettingsFolderPath = settingsFolderPath;
        // Applies the saved theme at startup — never a hidden one: MainViewModel doesn't
        // save those, and an older settings file that did falls back to the default.
        var saved = ThemeCatalog.Find(themeId);
        SelectedTheme = saved.IsHidden ? ThemeCatalog.Default : saved;
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public OptionsViewModel() : this(new MatchSettings(), new IgnoredRomsViewModel(), "", null)
    {
    }

    [RelayCommand]
    private void ShowPage(OptionsPage page) => SelectedPage = page;
}
