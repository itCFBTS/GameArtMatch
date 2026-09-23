using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static GameArtMatch.Services.TagCategorizer;

namespace GameArtMatch.Services;

/// <summary>
/// Turns one "(...)"/"[...]" tag group into the tokens NameNormalizer's tag-inclusive
/// (ForceInclude) display scoring compares, and weights them — docs/adr/0007. One
/// token per tag CLAUSE, never per word, of the form "&lt;category&gt;:&lt;canonical
/// clause&gt;" ("region:usa", "disc:1", "revision:rev 1"), so a differing revision
/// pays its full penalty as one token rather than half of one ("rev" shared, "1" vs
/// "2"), and the "1" in "Disc 1", "Rev 1", "Beta 1" and a title's own sequel number
/// can never collide as the same set member. Descriptive- and Irrelevant-significance
/// clauses (language lists, dates, platform/label names, translation and hack
/// credits) are omitted outright — not weighted 0 — so they neither help nor hurt.
/// </summary>
public static class TagTokenizer
{
    /// <summary>Weight of a Distinguishing-significance clause token (Unofficial,
    /// Preview, Revision) relative to a title word's 1.0 — see ADR-0007 §2.</summary>
    public const double DistinguishingWeight = 0.75;

    /// <summary>Weight of an unclassified (NeedsReview) clause — kept, not dropped,
    /// because that pile holds real both-sided signal ("Tengen", "Namcot Collection")
    /// alongside one-sided noise ("SGB Enhanced"); the catalog triage loop is how the
    /// noise gets promoted into a real category and then dropped by its level.</summary>
    public const double UnclassifiedWeight = 0.75;

    /// <summary>Prefix for an unclassified clause's token, so it's still marked as
    /// tag-derived rather than mistaken for a title word.</summary>
    private const string UnclassifiedPrefix = "tag";

    private static readonly Regex DiscPattern = new(
        @"^(?:Game\s+Disc|Disc|Disk|Side|Tape)[\s-]*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public static IEnumerable<string> Tokenize(string tagGroupInnerText)
    {
        foreach (var (category, clause) in TagCategorizer.ClassifyClauses(tagGroupInnerText))
        {
            if (category == TagCategory.NeedsReview)
            {
                yield return $"{UnclassifiedPrefix}:{Collapse(clause)}";
                continue;
            }

            var significance = TagCategorizer.SignificanceOf(category);
            if (significance is TagSignificance.Descriptive or TagSignificance.Irrelevant)
                continue;

            yield return $"{Prefix(category)}:{Canonicalize(category, clause)}";
        }
    }

    /// <summary>Per-token weight for the tag-inclusive display score. A title word
    /// (no recognized prefix) is 1.0; tag tokens weigh by their category's
    /// significance. Only tokens Tokenize can actually emit need a value here —
    /// Descriptive/Irrelevant never reach the set.</summary>
    public static double Weight(string token)
    {
        var colon = token.IndexOf(':');
        if (colon <= 0)
            return 1.0;

        var prefix = token[..colon];
        if (prefix == UnclassifiedPrefix)
            return UnclassifiedWeight;

        if (!Enum.TryParse<TagCategory>(prefix, ignoreCase: true, out var category) || category == TagCategory.NeedsReview)
            return 1.0; // a title word that happens to contain a colon

        return TagCategorizer.SignificanceOf(category) switch
        {
            TagSignificance.Decisive => 1.0,
            TagSignificance.Distinguishing => DistinguishingWeight,
            _ => 0.0, // never emitted; here only so the switch is total
        };
    }

    private static string Prefix(TagCategory category) => category.ToString().ToLowerInvariant();

    private static string Canonicalize(TagCategory category, string clause)
    {
        switch (category)
        {
            case TagCategory.Region:
                // ClassifyClause only labels a clause Region when this resolves.
                return (RegionCatalog.TryCanonicalize(clause) ?? clause).ToLowerInvariant();

            case TagCategory.Disc:
                // "Disc 1", "Disk 1", "Disc-B", "Game Disc" all reduce to the value
                // after the medium word — the medium word itself carries nothing.
                var m = DiscPattern.Match(clause);
                var value = m.Success ? m.Groups[1].Value.Trim() : clause;
                return Collapse(value.Length > 0 ? value : clause);

            case TagCategory.Preview:
                // "Prototype" -> "Proto" etc. via TagWordCatalog, word by word.
                return Collapse(string.Join(' ', RegionCatalog.SplitWords(clause)
                    .Select(w => TagWordCatalog.TryCanonicalize(w) ?? w)));

            default:
                return Collapse(clause);
        }
    }

    private static string Collapse(string s) => WhitespaceRun.Replace(s.Trim(), " ").ToLowerInvariant();
}
