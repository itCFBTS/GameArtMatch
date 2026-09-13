namespace GameArtMatch.Models;

/// <summary>
/// The subset of settings that survive between launches (unlike MatchSettings,
/// which holds the current session's specific Roms/Images folder + match options).
/// Serialized as plain JSON via ISettingsStore.
/// </summary>
public sealed class PersistedSettings
{
    /// <summary>Base folder to start browsing from when picking a system's ROMs folder,
    /// e.g. the top of your games library.</summary>
    public string? RomsRootPath { get; set; }

    /// <summary>Base folder to start browsing from when picking a system's images folder,
    /// e.g. the art pack's Covers folder.</summary>
    public string? ImagesRootPath { get; set; }

    /// <summary>Whether the ROMs root follows MiSTer's Console Mode convention
    /// (each system folder has its own "media" subfolder for box art).</summary>
    public bool IsConsoleMode { get; set; }

    /// <summary>Whether to recurse into subfolders when scanning the ROMs/Images
    /// directory. Persisted because leaving these off by default silently produces
    /// zero matches for any art pack organized into region subfolders (Licensed USA,
    /// Licensed Japan, etc.) — exactly the layout most of these packs actually use —
    /// and re-discovering that after every relaunch is more surprising than helpful.
    /// Nullable so "never saved yet" (use MatchSettings' own default) is distinct from
    /// "the user explicitly turned this off" (respect it) — a plain bool defaulting to
    /// false would silently override MatchSettings' true default on every fresh install.</summary>
    public bool? RomsIncludeSubfolders { get; set; }
    public bool? ImagesIncludeSubfolders { get; set; }
}
