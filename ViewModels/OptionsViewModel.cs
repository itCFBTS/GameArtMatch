using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>What the Appearance page lists: every regular theme, plus any hidden one
    /// that's been unlocked — in catalog order either way.</summary>
    public ObservableCollection<AppTheme> Themes { get; } = [];

    private readonly HashSet<string> _unlockedThemeIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> UnlockedThemeIds => _unlockedThemeIds;

    /// <summary>Lists a hidden theme on the Appearance page from now on. Returns whether
    /// it was newly unlocked (MainViewModel persists when it was).</summary>
    public bool UnlockTheme(AppTheme theme)
    {
        if (!theme.IsHidden || !_unlockedThemeIds.Add(theme.Id))
            return false;
        RebuildThemeList();
        return true;
    }

    private void RebuildThemeList()
    {
        Themes.Clear();
        foreach (var theme in ThemeCatalog.All)
            if (!theme.IsHidden || _unlockedThemeIds.Contains(theme.Id))
                Themes.Add(theme);
    }

    /// <summary>The active colour theme — applied live the moment it changes (the
    /// Appearance page's list binds straight to it). MainViewModel persists it.</summary>
    [ObservableProperty] public partial AppTheme SelectedTheme { get; set; }

    partial void OnSelectedThemeChanged(AppTheme value) => ThemeService.Apply(value);

    public OptionsViewModel(MatchSettings settings, IgnoredRomsViewModel ignoredRomsVm, string settingsFolderPath,
        string? themeId, IEnumerable<string>? unlockedThemeIds)
    {
        Settings = settings;
        IgnoredRomsVm = ignoredRomsVm;
        SettingsFolderPath = settingsFolderPath;

        foreach (var id in unlockedThemeIds ?? [])
            _unlockedThemeIds.Add(id);
        RebuildThemeList();

        // A saved hidden theme that somehow isn't unlocked (hand-edited settings) still
        // applies, and unlocks itself so it's listed.
        var saved = ThemeCatalog.Find(themeId);
        if (saved.IsHidden)
            UnlockTheme(saved);
        SelectedTheme = saved; // applies the saved theme at startup
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public OptionsViewModel() : this(new MatchSettings(), new IgnoredRomsViewModel(), "", null, null)
    {
    }

    [RelayCommand]
    private void ShowPage(OptionsPage page) => SelectedPage = page;
}
