using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using GameArtMatch.Themes;

namespace GameArtMatch.Services;

/// <summary>Switches the app's colour theme live. Three steps, all needed:
/// 1. Swap the theme's ResourceDictionary into Application.Resources in place of the
///    current one — every {DynamicResource} colour in the app re-resolves.
/// 2. Set Application.RequestedThemeVariant to the theme's base (Dark/Light), which
///    drives Fluent's own control chrome.
/// 3. Set Fluent's palette Accent to the theme's AccentBrush colour. Fluent derives its
///    accent shades (button fill, selection, progress, toggles) from that; left unset,
///    it follows the OS accent colour instead (e.g. KDE's blue), off-palette.</summary>
public static class ThemeService
{
    private const string ThemeMarkerKey = "AccentBrush";

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        // The active theme is whichever merged dictionary defines the colour keys —
        // App.axaml's startup ResourceInclude first, a catalog dictionary after a switch.
        // Tokens.axaml sits alongside it but defines no colours.
        var merged = app.Resources.MergedDictionaries;
        var slot = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].TryGetResource(ThemeMarkerKey, null, out _))
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
            merged.Add(theme.Resources);
        else if (!ReferenceEquals(merged[slot], theme.Resources))
            merged[slot] = theme.Resources;

        app.RequestedThemeVariant = theme.BaseVariant;

        if (theme.Accent is ISolidColorBrush accent
            && app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            foreach (var palette in fluent.Palettes.Values)
                palette.Accent = accent.Color;
        }
    }
}
