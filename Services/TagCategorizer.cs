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
/// "(...)" group separated by commas or " - ". ClassifyClauses splits on both before
/// classifying each clause (folding a credit's trailing "by X"/"Rev N" clauses into
/// the credit), so a tag only lands in NeedsReview when it has at least one clause
/// nothing here recognizes — not just because it happens to combine two
/// already-understood concepts.
/// </summary>
public static class TagCategorizer
{
    /// <summary>
    /// Declaration order is the category's RANK — how much a difference in a tag of
    /// this category tends to mean "different box art," most decisive first. Decided
    /// 2026-09-23. The criterion: a wrong disc is a wrong file outright; a wrong
    /// region is still the right game but almost always a different box; Unl/Pirate
    /// carts are different products; a Beta/Proto is a different build; Rev/Alt is
    /// the same box nearly every time; a translation or hack credit never gets its
    /// own art at all. Categorize's compound labels sort by this order, so they read
    /// most-significant-first ("Region+Revision", never "Revision+Region"). Nothing
    /// consumes the rank as a number yet — if a scoring weight ever does, that's the
    /// point to move the rank into an explicit table instead of relying on (int)c.
    /// </summary>
    public enum TagCategory
    {
        Disc,
        Region,

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

        /// <summary>Pre-release or promotional — not the final retail build (Beta,
        /// Demo, Proto/Prototype, Sample, Promo, Kiosk demo units). A different axis
        /// from Unofficial: this is about WHEN in the release cycle, not about
        /// legitimacy of origin — plenty of Preview tags belong to perfectly official
        /// releases (a publisher's own demo disc, a licensed kiosk cart).</summary>
        Preview,
        Revision,

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
        Language,
        Date,
        TranslationCredit,
        HackOrPatchCredit,

        /// <summary>At least one clause didn't match anything below — the actual
        /// "needs a human to look at it" pile.</summary>
        NeedsReview,
    }

    /// <summary>
    /// Coarser grouping over TagCategory's rank: how much a difference in a tag of
    /// this category matters to whether two names refer to the same box art. Decided
    /// 2026-09-23. Each level is a contiguous run of the rank order, so rank and
    /// significance never disagree about which of two categories matters more; the
    /// rank still orders categories within a level. Nothing consumes this yet — it's
    /// the vocabulary a future scoring rule (ADR-0004's "drop credits entirely", or
    /// a per-level penalty in the tag-inclusive display score) would be written in.
    /// </summary>
    public enum TagSignificance
    {
        /// <summary>A mismatch is a wrong file outright, or the wrong box nearly
        /// every time: Disc, Region.</summary>
        Decisive,

        /// <summary>A mismatch means a different product, build, or reissue —
        /// Unl/Pirate cart, Beta/Proto, Rev/Alt, Virtual Console/e-Reader — whose
        /// art usually still resembles the original's: Unofficial, Preview,
        /// Revision, Platform. Platform started in Descriptive; ADR-0007's NES
        /// validation moved it here because dropping it made "Foo (Europe)
        /// (Virtual Console)" tie "Foo (Europe)" and win on filename order, and
        /// in one case let a short e-Reader name overtake the right box.</summary>
        Distinguishing,

        /// <summary>Describes the release without usually changing which box it
        /// is: Label, Language, Date.</summary>
        Descriptive,

        /// <summary>Carries no art signal at all — a patch is scored against the
        /// original's box: TranslationCredit, HackOrPatchCredit.</summary>
        Irrelevant,
    }

    public static TagSignificance SignificanceOf(TagCategory category) => category switch
    {
        TagCategory.Disc or TagCategory.Region => TagSignificance.Decisive,
        TagCategory.Unofficial or TagCategory.Preview or TagCategory.Revision or TagCategory.Platform => TagSignificance.Distinguishing,
        TagCategory.Label or TagCategory.Language or TagCategory.Date => TagSignificance.Descriptive,
        TagCategory.TranslationCredit or TagCategory.HackOrPatchCredit => TagSignificance.Irrelevant,
        TagCategory.NeedsReview => throw new ArgumentOutOfRangeException(nameof(category),
            "NeedsReview is a triage bucket, not a category — it has no significance level."),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unmapped TagCategory — add it to SignificanceOf."),
    };

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
    // "Alt 1"/"Alt 2" (No-Intro's numbered alternate dumps) alongside bare "Alt".
    private static readonly Regex RevisionPattern = new(
        @"^(Rev\.?\s*\S+|v\d[\w\.]*|Alt(\s*\d+)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Requires a value after the medium word: "Disc 1", "Disk 2", "Disc-B", "Side A".
    // A bare "Disc" or "Game Disc" (Redump's label for the non-bonus disc of a set)
    // names a disc's ROLE, not its index, and is left unclassified — as a Disc token
    // it weighed 1.0 and made "(Disc 1) (Game Disc)" lose to "(Disc 2) (Omake Disc)".
    private static readonly Regex DiscPattern = new(
        @"^(?:Game Disc|Disc|Disk|Side|Tape)[\s-]+\S+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static readonly Regex TranslationPattern = new("Translat", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HackOrPatchPattern = new(
        @"\b(Hack|Patch|Trainer|Cheat)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns a single TagCategory name for a clean tag, or, for a compound
    /// one where every clause resolved to a known category but not all to the SAME
    /// one (e.g. "NA, Rev 2" = Region + Revision), the actual combination joined with
    /// "+" in the order its clauses appeared ("Region+Revision") rather than a single
    /// opaque "Compound" label — the whole point of splitting into clauses is to know
    /// exactly what's being combined, not just that something was.</summary>
    public static string Categorize(string tag)
    {
        var clauses = ClassifyClauses(tag);
        if (clauses.Count == 0 || clauses.Any(c => c.Category == TagCategory.NeedsReview))
            return nameof(TagCategory.NeedsReview);

        // Sorted into a fixed order (enum declaration order = rank, see TagCategory)
        // before joining, so "NA - Disc 1" and "Disk 1 - JP" both come out as
        // "Disc+Region" regardless of which clause happened to come first in the
        // source string — otherwise the same real combination would silently split
        // across two category names.
        var distinct = clauses.Select(c => c.Category).Distinct().OrderBy(c => (int)c).ToList();
        return distinct.Count == 1 ? distinct[0].ToString() : string.Join("+", distinct);
    }

    /// <summary>The per-clause view Categorize summarizes and TagTokenizer emits
    /// tokens from: each clause of the tag with the category it resolved to, in
    /// source order, NeedsReview for any clause nothing here recognizes.
    ///
    /// Splits on " - " first (the convention's major separator), then on commas
    /// within each segment. A translation or hack/patch credit usually spans a whole
    /// comma-separated segment — "English Translated, Rev A, by DvD Translations",
    /// "Reforged Patch by Mziab, FlamePurge, and Kevan33, Rev 1.01", "Tweaks,
    /// Localization, and Custom Art Patch by Acediez, Rev 2.6" — where the other
    /// parts are the credit's own words: author lists, the patch's revision, the
    /// front half of a patch name that happened to contain commas. So once a
    /// segment contains a credit part, every part after it and every UNRECOGNIZED
    /// part before it fold into the credit; a recognized part before it ("Rev 1" in
    /// "Rev 1, English Translated by X") is the game's and keeps its category. Parts
    /// in OTHER segments are never touched: in "NA - Disc 2 - Undub Patch by Etsuna,
    /// Rev 2" the region and disc are the game's and must survive (ADR-0007's PSX
    /// validation found 18 multi-disc ROMs landing on the wrong disc when an earlier
    /// version classified the WHOLE tag as a credit and dropped them).</summary>
    public static IReadOnlyList<(TagCategory Category, string Clause)> ClassifyClauses(string tag)
    {
        var segments = SegmentSplitPattern.Split(tag)
            .Select(seg => CommaSplitPattern.Split(seg).Select(c => c.Trim()).Where(c => c.Length > 0).ToList())
            .Where(seg => seg.Count > 0)
            .ToList();

        // Region and Language overlap: several real language codes (Ja, Fr, De, Es,
        // It, Ca) are also, case-insensitively, a RegionCatalog synonym (Japan,
        // France, Germany, Spain, Italy, Canada respectively). A single-clause tag
        // like "Ja" alone really does mean the Japan region in this corpus's own
        // convention (see RegionCatalog's own history) — but the SAME word inside a
        // multi-clause list ("En,Ja,Fr,De,Es,It") is unambiguously a language list,
        // not five different regions. So which reading wins depends on whether this
        // tag has other clauses alongside it, not on the word alone.
        var preferLanguage = segments.Sum(seg => seg.Count) > 1;

        var result = new List<(TagCategory, string)>();
        foreach (var parts in segments)
        {
            var creditIndex = parts.FindIndex(IsCreditClause);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                TagCategory category;
                if (creditIndex < 0)
                    category = ClassifyClause(part, preferLanguage) ?? TagCategory.NeedsReview;
                else if (i >= creditIndex)
                    category = CreditCategory(parts[creditIndex]);
                else
                    category = ClassifyClause(part, preferLanguage) ?? CreditCategory(parts[creditIndex]);
                result.Add((category, part));
            }
        }
        return result;
    }

    private static bool IsCreditClause(string clause) =>
        TranslationPattern.IsMatch(clause) || HackOrPatchPattern.IsMatch(clause);

    private static TagCategory CreditCategory(string creditClause) =>
        TranslationPattern.IsMatch(creditClause) ? TagCategory.TranslationCredit : TagCategory.HackOrPatchCredit;

    private static readonly Regex SegmentSplitPattern = new(@"\s-\s", RegexOptions.Compiled);
    private static readonly Regex CommaSplitPattern = new(@",", RegexOptions.Compiled);

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
