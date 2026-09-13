using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameArtMatch.Services;

/// <summary>
/// The Match tab's region filter — a post-scan display filter (doesn't affect scoring,
/// only which already-computed rows are shown). Deliberately scoped to exactly the
/// variants asked for, not every real-world region tag convention:
///   Japan = "Japan" or "JP", Europe = "Europe" or "EU", USA = "NA" or "USA" — matched
///   as a whole word inside a parenthetical group, split on any non-alphanumeric
///   separator, e.g. "(USA)", "(USA, Europe)", and "(NA - Disc 1)" all correctly find
///   "USA"/"NA" as their own word regardless of what else shares the tag.
///   English Translated = a loose substring match for the phrase anywhere in the name,
///   e.g. "Name (Some Translation Group English Translated v1.2).chd" — and uniquely,
///   this one only ever restricts ROMs; images are never filtered by it (see Matches).
/// </summary>
public static partial class RegionFilter
{
    public const string All = "All";
    public const string EnglishTranslated = "English Translated";

    public static readonly string[] Options = [All, "Japan", "Europe", "USA", EnglishTranslated];

    private static readonly Dictionary<string, string[]> TagWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Japan"] = ["Japan", "JP"],
        ["Europe"] = ["Europe", "EU"],
        ["USA"] = ["USA", "NA"],
    };

    /// <param name="isImage">True when checking an image filename rather than a ROM
    /// filename — English Translated exempts images from filtering entirely.</param>
    public static bool Matches(string fileName, string region, bool isImage)
    {
        if (region == All)
            return true;

        if (region == EnglishTranslated)
            return isImage || fileName.Contains(EnglishTranslated, StringComparison.OrdinalIgnoreCase);

        if (!TagWords.TryGetValue(region, out var words))
            return true;

        foreach (Match tagGroup in TagGroupPattern().Matches(fileName))
        {
            // Split on any non-alphanumeric run, not just commas — a tag like
            // "(NA - Disc 1)" or "(USA-Rev1)" needs "NA"/"USA" pulled out as their own
            // word just as much as the comma-separated "(USA, Europe)" case does.
            foreach (var word in WordSplitPattern().Split(tagGroup.Groups[1].Value))
            {
                if (word.Length > 0 && words.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex TagGroupPattern();

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex WordSplitPattern();
}
