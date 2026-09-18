using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameArtMatch.Services;

/// <summary>
/// First-pass, heuristic triage over docs/catalog's raw tag vocabulary (see its
/// README): buckets each tag into a coarse category so a human can skim the small
/// NeedsReview pile instead of all ~4,000 raw strings by hand. Labels only — this
/// doesn't change any matching/scoring behavior, and isn't itself the normalization/
/// categorization decision docs/adr/0004 and 0005 still need to make; it's the triage
/// step that tells you where to spend that attention.
///
/// Many real tag strings are compound (e.g. "NA, Rev 1", "EU - 4-in-1 Hack Patch by
/// Meduza Team, Rev 1.5") because ROM/art naming conventions cram everything into one
/// "(...)" group separated by commas or " - ". Categorize splits on both before
/// classifying each clause, so a tag only lands in NeedsReview when it has at least
/// one clause nothing here recognizes — not just because it happens to combine two
/// already-understood concepts.
/// </summary>
public static class TagCategorizer
{
    public enum TagCategory
    {
        Region,
        Language,
        Revision,
        Disc,
        Date,

        /// <summary>Pre-release or promotional — not the final retail build (Beta,
        /// Demo, Proto/Prototype, Sample, Promo, Kiosk demo units). A different axis
        /// from Unofficial: this is about WHEN in the release cycle, not about
        /// legitimacy of origin — plenty of Preview tags belong to perfectly official
        /// releases (a publisher's own demo disc, a licensed kiosk cart).</summary>
        Preview,

        /// <summary>Unlicensed/altered distribution (Unl, Pirate, Aftermarket,
        /// Reproduction, Repro). Deliberately doesn't include "Alt" — real examples
        /// show it on fully legitimate licensed releases just as often as on pirated
        /// ones (e.g. "Vid Grid (USA) (Alt)"); it's a No-Intro/Redump "alternate dump
        /// of the same release" disambiguator, not a legitimacy flag, so it's grouped
        /// with Revision instead. Also deliberately doesn't include "Program" (spans
        /// both official educational software and unofficial test carts — about
        /// content TYPE, not legitimacy) or "Unknown" (means "origin/date not
        /// established," a metadata-completeness flag, not itself a legitimacy
        /// claim) — both left in NeedsReview rather than force-fit here.</summary>
        Unofficial,

        /// <summary>Hardware, storefronts, distribution/rental services, and
        /// mini-console/arcade-collection reissues — "what this runs on or was
        /// distributed through." Deliberately doesn't cover hardware-compatibility
        /// flags ("SGB Enhanced", "GBA Enhanced" — "also works on X") or cart form
        /// factor ("72 pin cart" — which physical connector) — those are a different
        /// axis of information and stay in NeedsReview until there's enough real
        /// evidence to justify their own category.</summary>
        Platform,

        /// <summary>Publisher/reissue brand names ("Zeppelin Games", "Limited Run
        /// Games") — who repackaged/re-released this, not what it runs on.</summary>
        Label,

        TranslationCredit,
        HackOrPatchCredit,

        /// <summary>At least one clause didn't match anything below — the actual
        /// "needs a human to look at it" pile.</summary>
        NeedsReview,
    }

    // Non-exhaustive by design, same "extend as real data turns up more" spirit as
    // RegionCatalog/TagWordCatalog/NameNormalizer.KnownRomanLookalikes.
    private static readonly HashSet<string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "En", "Ja", "Fr", "De", "Es", "It", "Nl", "Sv", "No", "Da", "Fi", "Pl", "Ru", "Zh", "Ko", "Ar", "Ca", "Cs", "Yi", "Pt",
    };

    // Literal, exact-clause matches only — same "extend as evidence appears, don't
    // guess ahead of it" discipline as RegionCatalog: e.g. "GameCube Edition" is its
    // own entry rather than a "starts with a known platform" pattern, since that kind
    // of pattern would just as happily swallow something that isn't a platform tag.
    private static readonly HashSet<string> KnownPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Distribution services / storefronts
        "Virtual Console", "Wii Virtual Console", "Wii U Virtual Console", "3DS Virtual Console",
        "Wii and Wii U Virtual Console", "USA Wii Virtual Console",
        "Switch", "Switch Online", "Classic Mini",
        "Evercade", "Sega Channel", "LodgeNet", "EasyFlash",
        "Steam", "GOG", "itch.io", "e-Reader", "e-Reader Edition",
        "Sega Game Toshokan", "Nintendo Power mail-order",

        // Consoles / handhelds / mini-console reissues
        "GameCube", "GameCube Edition", "GameCube Preview", "Wii U", "Dreamcast Version",
        "Famicom 3D System", "PS Vita",
        "Mega Drive Mini", "Genesis Mini", "Mega Drive Mini 2", "Genesis Mini 2",
        "PC Engine Mini", "Master System Evolution",
        "Intellivision", "Intellivision Lives!", "Atari Lynx Collection 1",
        "3DO Action Pak",

        // Arcade
        "Arcade", "Arcade CD-ROM", "Arcade Disc", "Arcade Mode", "Arcade Audio",
        "Toaplan Arcade Garage",

        // PlayStation reissue sub-labels — distribution-related (a repackaged disc
        // release), not a third-party publisher brand, so grouped with Platform
        // rather than Label.
        "PlayStation the Best", "PlayStation the Best for Family",
    };

    private static readonly Regex PreviewPattern = new(
        @"^(Beta|Demo|Proto(type)?|Sample|Promo|Kiosk)(\s+\S+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnofficialPattern = new(
        @"^(Unl|Pirate|Aftermarket|Reproduction|Repro)(\s+\S+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Alt" folded in here, not Unofficial — see Unofficial's doc comment.
    private static readonly Regex RevisionPattern = new(
        @"^(Rev\.?\s*\S+|v\d[\w\.]*|Alt)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DiscPattern = new(
        @"^(Disc|Disk|Side|Tape|Game Disc)\s*\S*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static readonly Regex TranslationPattern = new("Translat", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HackOrPatchPattern = new(
        @"\b(Hack|Patch|Trainer|Cheat)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClauseSplitPattern = new(@",|\s-\s", RegexOptions.Compiled);

    /// <summary>Returns a single TagCategory name for a clean tag, or, for a compound
    /// one where every clause resolved to a known category but not all to the SAME
    /// one (e.g. "NA, Rev 2" = Region + Revision), the actual combination joined with
    /// "+" in the order its clauses appeared ("Region+Revision") rather than a single
    /// opaque "Compound" label — the whole point of splitting into clauses is to know
    /// exactly what's being combined, not just that something was.</summary>
    public static string Categorize(string tag)
    {
        // Checked against the WHOLE tag, before splitting into clauses: a translation
        // or hack/patch credit is one semantic unit even when it also drags along a
        // region/revision clause ("EU - 4-in-1 Hack Patch by Meduza Team, Rev 1.5") —
        // splitting first would just strand "by Meduza Team" as an unrecognized clause
        // and lose the more useful, specific label.
        if (TranslationPattern.IsMatch(tag)) return nameof(TagCategory.TranslationCredit);
        if (HackOrPatchPattern.IsMatch(tag)) return nameof(TagCategory.HackOrPatchCredit);

        var clauses = ClauseSplitPattern.Split(tag)
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToList();

        if (clauses.Count == 0)
            return nameof(TagCategory.NeedsReview);

        // Region and Language overlap: several real language codes (Ja, Fr, De, Es,
        // It, Ca) are also, case-insensitively, a RegionCatalog synonym (Japan,
        // France, Germany, Spain, Italy, Canada respectively). A single-clause tag
        // like "Ja" alone really does mean the Japan region in this corpus's own
        // convention (see RegionCatalog's own history) — but the SAME word inside a
        // multi-clause list ("En,Ja,Fr,De,Es,It") is unambiguously a language list,
        // not five different regions. So which reading wins depends on whether this
        // tag has other clauses alongside it, not on the word alone.
        var preferLanguage = clauses.Count > 1;
        var classified = clauses.Select(c => ClassifyClause(c, preferLanguage)).ToList();
        if (classified.Any(c => c is null))
            return nameof(TagCategory.NeedsReview);

        // Sorted into a fixed order (enum declaration order) before joining, so
        // "NA - Disc 1" and "Disk 1 - JP" both come out as "Region+Disc" regardless
        // of which clause happened to come first in the source string — otherwise
        // the same real combination would silently split across two category names.
        var distinct = classified.Cast<TagCategory>().Distinct().OrderBy(c => (int)c).ToList();
        return distinct.Count == 1 ? distinct[0].ToString() : string.Join("+", distinct);
    }

    private static TagCategory? ClassifyClause(string clause, bool preferLanguage)
    {
        if (preferLanguage)
        {
            if (LanguageCodes.Contains(clause)) return TagCategory.Language;
            if (RegionCatalog.TryCanonicalize(clause) is not null) return TagCategory.Region;
        }
        else
        {
            if (RegionCatalog.TryCanonicalize(clause) is not null) return TagCategory.Region;
            if (LanguageCodes.Contains(clause)) return TagCategory.Language;
        }
        if (PreviewPattern.IsMatch(clause)) return TagCategory.Preview;
        if (UnofficialPattern.IsMatch(clause)) return TagCategory.Unofficial;
        if (RevisionPattern.IsMatch(clause)) return TagCategory.Revision;
        if (DiscPattern.IsMatch(clause)) return TagCategory.Disc;
        if (DatePattern.IsMatch(clause)) return TagCategory.Date;
        if (KnownPlatforms.Contains(clause)) return TagCategory.Platform;
        if (clause.EndsWith(" Games", StringComparison.OrdinalIgnoreCase)) return TagCategory.Label;
        return null;
    }
}
