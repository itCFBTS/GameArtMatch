using CommunityToolkit.Mvvm.ComponentModel;

namespace GameArtMatch.Models;

/// <summary>One scored Rom &lt;-&gt; Image pairing shown in the Match results list.</summary>
public partial class MatchCandidate : ObservableObject
{
    [ObservableProperty] public partial bool IsSelected { get; set; }

    public required string RomFileName { get; init; }
    public required string ImageFileName { get; init; }

    /// <summary>Full path to the image on disk — needed (not just ImageFileName) because
    /// "Include subfolders" can match a file that isn't directly under ImagesPath.
    /// Used for preview and will be used for the actual copy/rename operation.</summary>
    public required string ImageFullPath { get; init; }

    public required double ScorePercent { get; init; }

    /// <summary>True when this image was also picked as a candidate for another ROM —
    /// an ambiguous match worth a second look before renaming. Computed after a full
    /// scan (see MatchingService), so it's mutable rather than set at construction.</summary>
    [ObservableProperty] public partial bool IsDuplicate { get; set; }
}
