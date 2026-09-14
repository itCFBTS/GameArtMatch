using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GameArtMatch.Models;

/// <summary>
/// Shared state for the "Paths" and "Options" tabs. One instance is owned by
/// MainViewModel and handed to both MatchViewModel and ReportViewModel, so a
/// folder or option chosen once applies to both the fuzzy-match wizard and the
/// missing/matched report — mirrors the original FatMatch.exe's settings.config,
/// but shared instead of duplicated between two separate windows.
/// </summary>
public partial class MatchSettings : ObservableObject
{
    // --- Roots (persisted between launches via ISettingsStore — see PersistedSettings) ---
    [ObservableProperty] public partial string RomsRootPath { get; set; } = "";
    [ObservableProperty] public partial string ImagesRootPath { get; set; } = "";

    /// <summary>True when the ROMs root follows MiSTer's Console Mode convention —
    /// each system has its own folder with a "media" subfolder holding box art
    /// (e.g. "MegaCD/media/Sonic CD (USA).png"). Persisted with the roots, since
    /// it describes the ROMs root's layout, not a per-session choice.</summary>
    [ObservableProperty] public partial bool IsConsoleMode { get; set; } = false;

    /// <summary>Console Mode has an obvious default for SkipExistingArt (on when Console
    /// Mode is on, off otherwise), but it's still a separate, independently-toggleable
    /// setting — this only sets the default on each Console Mode transition, it doesn't
    /// force/lock the two together.</summary>
    partial void OnIsConsoleModeChanged(bool value) => SkipExistingArt = value;

    // --- Paths (the current session's specific system folder; not persisted) ---
    [ObservableProperty] public partial string RomsPath { get; set; } = "";
    [ObservableProperty] public partial string RomsExtensions { get; set; } = "*";
    [ObservableProperty] public partial string ImagesPath { get; set; } = "";
    [ObservableProperty] public partial string ImagesExtensions { get; set; } = "*";

    // Default true: most art packs (this one included) organize covers into region
    // subfolders (Licensed USA, Licensed Japan, ...) rather than a flat folder, so off
    // silently finds zero matches for the common case. Persisted (see PersistedSettings)
    // so an explicit opt-out sticks, but this default stands on a fresh install.
    [ObservableProperty] public partial bool RomsIncludeSubfolders { get; set; } = true;
    [ObservableProperty] public partial bool ImagesIncludeSubfolders { get; set; } = true;

    /// <summary>Just the leaf folder name (e.g. "MegaCD" instead of the full path) — the
    /// main window shows these instead of the full RomsPath/ImagesPath to stay compact.</summary>
    public string RomsPathDisplayName => GetLeafFolderName(RomsPath);
    public string ImagesPathDisplayName => GetLeafFolderName(ImagesPath);

    partial void OnRomsPathChanged(string value) => OnPropertyChanged(nameof(RomsPathDisplayName));
    partial void OnImagesPathChanged(string value) => OnPropertyChanged(nameof(ImagesPathDisplayName));

    private static string GetLeafFolderName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(not set)";

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        // Falls back to the original (untrimmed) path for a drive root, e.g. "/" or "C:\" —
        // trimmed would itself be empty in that case ("/" trims to ""), not just name.
        return string.IsNullOrEmpty(name) ? path : name;
    }

    // --- Matching options (see original settings.config keys in parentheses) ---
    [ObservableProperty] public partial bool DisregardRomTags { get; set; } = true; // DISABLE_ROMTAGS
    [ObservableProperty] public partial bool MatchStandaloneLetters { get; set; } = true; // MATCH_LONELETTERS
    [ObservableProperty] public partial bool TryRomanNumerals { get; set; } = true; // TRY_ROMAN
    [ObservableProperty] public partial bool MatchCase { get; set; } = false;
    [ObservableProperty] public partial bool DisregardCommonWords { get; set; } = true; // DISABLE_COMWORDS
    [ObservableProperty] public partial string CommonWords { get; set; } = "the;and;of;in;to"; // COMWORDS
    [ObservableProperty] public partial int AccuracyThreshold { get; set; } = 65; // ACCURACY

    // --- Renaming options ---
    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; } = false; // DELETE_ORIGINAL
    [ObservableProperty] public partial bool ExportBackupScript { get; set; } = false;
    [ObservableProperty] public partial bool ExportCopyScript { get; set; } = false; // EXPORT_COPY / EXPORT_SCRIPT

    // --- Console Mode options (only meaningful when IsConsoleMode is true) ---
    /// <summary>When true, a ROM that already has an image in its core's "media" folder
    /// (e.g. "MegaCD/media/Sonic CD (USA).png") is dropped from the match candidates
    /// entirely — the goal becomes filling gaps, not reviewing/overwriting existing art.</summary>
    [ObservableProperty] public partial bool SkipExistingArt { get; set; } = false;

    /// <summary>Full paths of ROMs to always skip during scanning (see
    /// MatchingService.ListRoms) — global across every system/folder ever scanned, not
    /// scoped to the current RomsPath, since a ROM ignored once should stay ignored no
    /// matter which folder you point the app at later. Populated from PersistedSettings
    /// at startup and mutated directly (not reassigned) by MatchViewModel.IgnoreRom, so
    /// MainViewModel — the sole ISettingsStore owner — can persist the same instance
    /// after the fact rather than needing a round-trip.</summary>
    public HashSet<string> IgnoredRomPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
}
