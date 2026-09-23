using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace GameArtMatch.Themes;

/// <summary>One selectable colour theme — a Themes/*.axaml ResourceDictionary plus the
/// metadata the Options pane's Appearance page shows. Loaded lazily the first time its
/// colours are needed (a swatch or ThemeService.Apply), then kept.</summary>
public sealed class AppTheme(string id, string name, string description, ThemeVariant baseVariant,
    bool isHidden = false)
{
    private ResourceDictionary? _resources;

    /// <summary>Stable key persisted in settings.json — never rename an existing one.</summary>
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;

    /// <summary>Dark or Light — which of Fluent's control-chrome variants the theme sits
    /// on (see the theme files' header comment).</summary>
    public ThemeVariant BaseVariant { get; } = baseVariant;

    /// <summary>Easter-egg themes: never listed on the Appearance page (see
    /// OptionsViewModel.Themes); reachable only their own way.</summary>
    public bool IsHidden { get; } = isHidden;

    public ResourceDictionary Resources =>
        _resources ??= (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://GameArtMatch/Themes/{Id}.axaml"));

    // Swatch colours for the Appearance page's previews, read straight from the theme's
    // own dictionary so they're right before the theme is ever applied.
    public IBrush Background => Brush("BackgroundBrush");
    public IBrush Surface => Brush("SurfaceBrush");
    public IBrush Foreground => Brush("ForegroundBrush");
    public IBrush ForegroundMuted => Brush("ForegroundMutedBrush");
    public IBrush Accent => Brush("AccentBrush");
    public IBrush Border => Brush("BorderBrush");

    private IBrush Brush(string key) =>
        Resources.TryGetResource(key, null, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}

/// <summary>
/// Every colour theme the app ships. To add one: copy a Themes/*.axaml file (same keys,
/// new values — keep text at least 4.5:1 against BackgroundBrush), then add a line here
/// whose id is the file's name. That's all; the Appearance page lists it automatically
/// (unless it's marked isHidden — then never; it needs its own way in).
/// </summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<AppTheme> All { get; } =
    [
        new("Snes", "SNES", "Warm charcoal with Super Famicom lavender.", ThemeVariant.Dark),
        new("MegaDrive", "Mega Drive", "Near-black console plastic with Sega blue.", ThemeVariant.Dark),
        new("GameBoy", "Game Boy", "Pale DMG screen green, dark-green ink.", ThemeVariant.Light),
        // Only reachable by typing "noclip" into the Match page's search box (MainViewModel.OnNoclip).
        new("Level0", "Level 0", "You noclipped out of reality.", ThemeVariant.Light, isHidden: true),
    ];

    public const string NoclipThemeId = "Level0";

    /// <summary>The one App.axaml loads at startup.</summary>
    public static AppTheme Default => All[0];

    /// <summary>Unknown/missing ids (a removed theme, a fresh install) fall back to Default.</summary>
    public static AppTheme Find(string? id) => TryFind(id) ?? Default;

    public static AppTheme? TryFind(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
