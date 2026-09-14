using System;
using System.Linq;

namespace GameArtMatch.Services;

/// <summary>
/// The Match tab's region filter — a post-scan display filter (doesn't affect scoring,
/// only which already-computed rows are shown). Region words are matched via the shared
/// RegionCatalog (canonical region + synonyms), as a whole word inside a "(...)"/"[...]"
/// group, split on any non-alphanumeric separator — e.g. "(USA)", "(USA, Europe)", and
/// "(NA - Disc 1)" all correctly find "USA"/"NA" as their own word regardless of what
/// else shares the tag.
///   English Translated = a loose substring match for the phrase anywhere in the name,
///   e.g. "Name (Some Translation Group English Translated v1.2).chd" — and uniquely,
///   this one only ever restricts ROMs; images are never filtered by it (see Matches).
/// </summary>
public static class RegionFilter
{
    public const string All = "All";
    public const string EnglishTranslated = "English Translated";

    public static readonly string[] Options = [All, .. RegionCatalog.CanonicalRegions, EnglishTranslated];

    /// <param name="isImage">True when checking an image filename rather than a ROM
    /// filename — English Translated exempts images from filtering entirely.</param>
    /// <remarks>region is nullable because the bound ComboBox's SelectedItem transiently
    /// goes null whenever MatchViewModel clears AvailableRegions to rebuild it for a new
    /// scan (the previously-selected item briefly isn't in the list) — null is treated
    /// the same as All (match everything) rather than throwing.</remarks>
    public static bool Matches(string fileName, string? region, bool isImage)
    {
        if (region is null || region == All)
            return true;

        if (region == EnglishTranslated)
            return isImage || fileName.Contains(EnglishTranslated, StringComparison.OrdinalIgnoreCase);

        var synonyms = RegionCatalog.SynonymsFor(region);
        if (synonyms.Count == 0)
            return true;

        foreach (var tagGroup in RegionCatalog.FindTagGroups(fileName))
        {
            foreach (var word in RegionCatalog.SplitWords(tagGroup.Groups[1].Value))
            {
                if (synonyms.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }
}
