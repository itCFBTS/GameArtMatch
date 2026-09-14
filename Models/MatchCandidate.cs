using CommunityToolkit.Mvvm.ComponentModel;

namespace GameArtMatch.Models;

/// <summary>One scored Rom &lt;-&gt; Image pairing shown in the Match results list.</summary>
public partial class MatchCandidate : ObservableObject
{
    [ObservableProperty] public partial bool IsSelected { get; set; }

    public required string RomFileName { get; init; }

    /// <summary>Full path to the ROM on disk — needed (not just RomFileName) because
    /// "Include subfolders" can match a file that isn't directly under RomsPath, and
    /// because ignoring a ROM (see MatchViewModel.IgnoreRomCommand) is keyed by full
    /// path so a same-named ROM in a different folder isn't accidentally also skipped.</summary>
    public required string RomFullPath { get; init; }

    public required string ImageFileName { get; init; }

    /// <summary>Full path to the image on disk — needed (not just ImageFileName) because
    /// "Include subfolders" can match a file that isn't directly under ImagesPath.
    /// Used for preview and will be used for the actual copy/rename operation.</summary>
    public required string ImageFullPath { get; init; }

    public required double ScorePercent { get; init; }

    /// <summary>True when the ROM and image basenames are literally identical (extension
    /// aside) — always case-insensitive, deliberately independent of settings.MatchCase
    /// (which controls fuzzy token comparison; this is a stricter, separate concept, so
    /// don't "fix" it to respect MatchCase later). A pure function of this one pair, so
    /// it's set at construction like ScorePercent rather than mutated later like
    /// IsDuplicate below.</summary>
    public required bool IsExactMatch { get; init; }

    /// <summary>SHA-256 (hex) of the image file's raw bytes — a strict "same file or not"
    /// check, deliberately not a perceptual/similarity hash: a genuinely different scan
    /// or crop of the same box art should stay distinct, only byte-identical copies
    /// should collapse together. Computed once per distinct image path per scan (see
    /// MatchingService), regardless of which/how many ROMs it's a candidate for.</summary>
    public required string ContentHash { get; init; }

    /// <summary>True when the immediately-preceding VISIBLE candidate in this ROM's list
    /// has the same ContentHash — i.e., this is a byte-identical copy of the row right
    /// above it, just under a different filename. Recomputed by MatchViewModel.ApplyFilters
    /// whenever the visible set changes, since which row counts as "above" depends on the
    /// current filters — so it's mutable, not set at construction.</summary>
    [ObservableProperty] public partial bool IsSameAsAbove { get; set; }

    /// <summary>Alternating row-background markers for a multi-member identical-content
    /// cluster (2+ visible candidates sharing ContentHash within this ROM) — at most one
    /// is ever true, and both are false for a singleton (unique-content) candidate, which
    /// gets no shading at all. Recomputed by MatchViewModel.ApplyFilters alongside
    /// IsSameAsAbove, for the same reason — depends on the current visible set.</summary>
    [ObservableProperty] public partial bool IsContentShadeA { get; set; }
    [ObservableProperty] public partial bool IsContentShadeB { get; set; }

    /// <summary>True when this image was also picked as a candidate for another ROM —
    /// an ambiguous match worth a second look before renaming. Computed after a full
    /// scan (see MatchingService), so it's mutable rather than set at construction.</summary>
    [ObservableProperty] public partial bool IsDuplicate { get; set; }
}
