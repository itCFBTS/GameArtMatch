using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameArtMatch.Services;

/// <summary>
/// Canonical region names and their known literal synonyms — the single shared source
/// for "what counts as the same region," used both by RegionFilter (the Match tab's
/// post-scan Region: dropdown) and by NameNormalizer's tag-inclusive scoring (so "USA"
/// and "US" don't count as a mismatch the way a genuine region or version difference
/// should). Non-exhaustive by design, same "extend as needed" spirit as
/// NameNormalizer.KnownRomanLookalikes — covers common No-Intro/Redump/TOSEC region tags,
/// not every real-world convention.
/// </summary>
public static partial class RegionCatalog
{
    public static readonly IReadOnlyList<string> CanonicalRegions =
    [
        "USA", "Europe", "Japan", "World", "Asia", "Australia",
        "Korea", "China", "Brazil", "Canada", "Spain", "France",
        "Germany", "Italy", "Netherlands", "Sweden",
        // Added from docs/catalog's NeedsReview triage (see docs/adr/README) — real
        // region tags found in the actual library, not yet covered above. UK and
        // Scandinavia are genuinely distinct release regions (a UK-specific release
        // isn't the same as a generic Europe one, same reasoning USA stays separate
        // from World), not synonyms of an existing entry.
        "UK", "Scandinavia", "Taiwan", "Russia", "Denmark", "Norway", "Finland",
        "Argentina", "Hong Kong", "Portugal", "Greece", "Belgium", "Ireland",
        "Israel", "India", "Mexico", "Peru",
    ];

    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USA"] = ["USA", "US", "NA", "NTSC-U"],
        ["Europe"] = ["Europe", "EU", "PAL"],
        ["Japan"] = ["Japan", "JP", "JPN", "NTSC-J", "Ja"],
        ["World"] = ["World", "W"],
        ["Asia"] = ["Asia"],
        ["Australia"] = ["Australia", "AUS", "AU"],
        // "Korean" (adjective) appears in the real corpus as a stand-in for a Korea
        // release, the same "region word vs. language word" overlap "Ja" already has
        // for Japan above.
        ["Korea"] = ["Korea", "KOR", "KR", "Korean"],
        ["China"] = ["China", "CHN", "CN"],
        ["Brazil"] = ["Brazil", "BRA", "BR"],
        ["Canada"] = ["Canada", "CAN", "CA"],
        ["Spain"] = ["Spain", "SPA", "ES"],
        ["France"] = ["France", "FRA", "FR"],
        ["Germany"] = ["Germany", "GER", "DE"],
        ["Italy"] = ["Italy", "ITA", "IT"],
        ["Netherlands"] = ["Netherlands", "NL", "Holland"],
        ["Sweden"] = ["Sweden", "SW", "SE"],
        // Below: only the spelled-out name each one actually appears as in the real
        // library (see docs/catalog) — no invented abbreviations (e.g. "TWN", "RUS")
        // that haven't actually turned up, same "extend as evidence appears, don't
        // guess ahead of it" discipline as the rest of this catalog.
        ["UK"] = ["UK"],
        ["Scandinavia"] = ["Scandinavia"],
        ["Taiwan"] = ["Taiwan"],
        ["Russia"] = ["Russia"],
        ["Denmark"] = ["Denmark"],
        ["Norway"] = ["Norway"],
        ["Finland"] = ["Finland"],
        ["Argentina"] = ["Argentina"],
        ["Hong Kong"] = ["Hong Kong"],
        ["Portugal"] = ["Portugal"],
        ["Greece"] = ["Greece"],
        ["Belgium"] = ["Belgium"],
        ["Ireland"] = ["Ireland"],
        ["Israel"] = ["Israel"],
        ["India"] = ["India"],
        ["Mexico"] = ["Mexico"],
        ["Peru"] = ["Peru"],
    };

    /// <summary>Reverse of Synonyms, built via an explicit loop (not a collection
    /// initializer) so two regions accidentally claiming the same synonym word throws at
    /// startup instead of one silently shadowing the other.</summary>
    private static readonly Dictionary<string, string> WordToCanonical = BuildWordToCanonical();

    private static Dictionary<string, string> BuildWordToCanonical()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, words) in Synonyms)
        {
            foreach (var word in words)
            {
                if (map.TryGetValue(word, out var existing))
                    throw new InvalidOperationException(
                        $"RegionCatalog synonym '{word}' is claimed by both '{existing}' and '{canonical}'.");
                map[word] = canonical;
            }
        }
        return map;
    }

    /// <summary>Looks up a single already-split word (e.g. "US", "JP") and returns its
    /// canonical region name, or null if the word isn't a recognized region synonym at
    /// all — non-region tag words ("Unl", "v3.11.088") always return null here (see
    /// TagWordCatalog for other cross-spelling tag-word synonyms, e.g. "Proto"/
    /// "Prototype", handled separately since they aren't regions).</summary>
    public static string? TryCanonicalize(string word) =>
        WordToCanonical.TryGetValue(word, out var canonical) ? canonical : null;

    /// <summary>All synonym words for a canonical region name (used by RegionFilter).
    /// Empty for an unrecognized name.</summary>
    public static IReadOnlyList<string> SynonymsFor(string canonicalRegion) =>
        Synonyms.TryGetValue(canonicalRegion, out var words) ? words : [];

    /// <summary>Splits a tag-group's inner text ("USA, Australia", "NA - Disc 1") into
    /// words on any non-alphanumeric run — shared so RegionFilter and NameNormalizer
    /// can't drift into two different splitting rules.</summary>
    public static IEnumerable<string> SplitWords(string tagGroupInnerText) =>
        WordSplitPattern().Split(tagGroupInnerText).Where(w => w.Length > 0);

    /// <summary>Finds each "(...)" or "[...]" group in a filename. Doesn't special-case
    /// mismatched bracket types (e.g. "(Foo]") — that doesn't occur in real ROM naming
    /// conventions, so it's not worth the extra complexity to detect.</summary>
    public static IEnumerable<Match> FindTagGroups(string text) => TagGroupPattern().Matches(text).Cast<Match>();

    [GeneratedRegex(@"[\(\[]([^)\]]*)[\)\]]")]
    private static partial Regex TagGroupPattern();

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex WordSplitPattern();
}
