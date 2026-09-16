using System;
using System.Collections.Generic;

namespace GameArtMatch.Services;

/// <summary>
/// Canonical spellings for non-region tag words that should compare as identical during
/// NameNormalizer's tag-inclusive (ForceInclude) scoring — the same "same concept,
/// different spelling" problem RegionCatalog solves for region words, kept as a separate
/// catalog since these aren't regions and don't need RegionFilter's dropdown-facing
/// CanonicalRegions/SynonymsFor surface. Non-exhaustive by design, same "extend as
/// needed" spirit as RegionCatalog and NameNormalizer.KnownRomanLookalikes.
/// </summary>
public static class TagWordCatalog
{
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Proto"] = "Proto",
        ["Prototype"] = "Proto",
    };

    /// <summary>Looks up a single already-split word (e.g. "Prototype") and returns its
    /// canonical spelling, or null if it isn't a recognized synonym at all.</summary>
    public static string? TryCanonicalize(string word) =>
        Synonyms.TryGetValue(word, out var canonical) ? canonical : null;
}
