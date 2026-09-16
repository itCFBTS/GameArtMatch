using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

/// <summary>
/// Turns a ROM/image basename into a set of comparable tokens. Rules are ported from
/// FatMatch.exe's PrepareWord/MatchTheseTwo (reverse-engineered from its compiled IL),
/// but restructured around real token-set membership instead of FatMatch's ad-hoc
/// substring/word-boundary string scanning — see MatchingService's class doc for what
/// that changes. See MatchSettings for what each rule/checkbox controls.
/// </summary>
public static partial class NameNormalizer
{
    // Same fixed punctuation pass FatMatch always applied, tag-stripping aside.
    // Apostrophe is deleted outright (not spaced) — "Yoshi's" -> "Yoshis", matching
    // the original so "Yoshi's Island" and "Yoshis Island" tokenize identically.
    private static readonly (char Find, char? Replace)[] PunctuationRules =
    [
        ('-', ' '), (',', ' '), ('\'', null), ('"', ' '), ('!', ' '), ('.', ' '),
        ('`', ' '), (';', ' '), ('+', ' '), ('~', ' '), ('^', ' '), ('%', ' '),
        ('$', ' '), ('#', ' '), ('@', ' '), ('&', ' '),
    ];

    /// <summary>Controls whether ToTokens strips "(...)"/"[...]" tag content before
    /// tokenizing. StripPerSettings is the original, recall-oriented behavior used for
    /// candidacy/threshold gating — untouched by ForceInclude, which exists solely to
    /// compute a tag-aware display score for candidates that already passed that gate.</summary>
    public enum TagHandling
    {
        /// <summary>Today's exact behavior: strip tags when settings.DisregardRomTags is
        /// true, so title matching stays insensitive to version/region noise.</summary>
        StripPerSettings,

        /// <summary>Never strip tags — region words, plus a handful of other known
        /// cross-spelling tag words (e.g. "Prototype"/"Proto"), are first canonicalized
        /// (see CanonicalizeTagWords) so those spelling differences don't count against
        /// the resulting token set, but every other tag word (version numbers, "Unl",
        /// "Rev A") stays literal and does count.</summary>
        ForceInclude,
    }

    public static HashSet<string> ToTokens(string rawName, MatchSettings settings,
        TagHandling tagHandling = TagHandling.StripPerSettings)
    {
        var comparer = settings.MatchCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var name = rawName;

        if (tagHandling == TagHandling.StripPerSettings)
        {
            if (settings.DisregardRomTags)
            {
                name = RemoveBetween(name, '(', ')');
                name = RemoveBetween(name, '[', ']');
            }
        }
        else
        {
            name = CanonicalizeTagWords(name);
        }

        name = ApplyPunctuationRules(name);

        var commonWords = settings.DisregardCommonWords
            ? new HashSet<string>(
                settings.CommonWords.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase)
            : null;

        var tokens = new HashSet<string>(comparer);
        foreach (var raw in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Common-word removal happens per-token (not as a substring replace on the
            // whole string like the original) so "The" only drops the word "The" —
            // never mangles it out of the middle of "Theme" or "Gathering".
            if (commonWords is not null && commonWords.Contains(raw))
                continue;

            // A 1-letter token can never contribute a match unless the setting allows
            // it (FatMatch's "Match Standalone Letters") — dropped entirely, same as
            // the original: it stays in neither side's set, so it can't cause a false
            // miss either. 2+ letter tokens are always kept and compared as whole
            // tokens (see SimilarityScorer) — this also fixes a real bug in the
            // original, where 3+ letter tokens were compared via raw substring
            // containment (so "Man" would match inside "Mankind").
            if (raw.Length == 1 && !settings.MatchStandaloneLetters)
                continue;

            var token = settings.MatchCase ? raw : raw.ToLowerInvariant();
            tokens.Add(settings.TryRomanNumerals ? NormalizeNumeralToken(token) : token);

            AddYearAbbreviationVariant(tokens, token);
        }

        return tokens;
    }

    /// <summary>Replaces any region- or tag-word synonym found inside a "(...)"/"[...]"
    /// group with its canonical spelling (e.g. "US" -> "USA", "Prototype" -> "Proto"),
    /// leaving every other word — both unrecognized tag words and the actual title text
    /// outside any group — untouched. Run as a single pass over the raw string, before
    /// punctuation/splitting, because once tokens are split there's no way to tell which
    /// ones came from inside a tag group without re-plumbing that context through the
    /// rest of the pipeline.</summary>
    private static string CanonicalizeTagWords(string name)
    {
        var result = name;
        // Replace back-to-front so each earlier match's span/index stays valid as later
        // ones are rewritten in place.
        foreach (var match in RegionCatalog.FindTagGroups(name).Reverse())
        {
            var inner = match.Groups[1].Value;
            var rewritten = string.Join(' ', RegionCatalog.SplitWords(inner)
                .Select(word => RegionCatalog.TryCanonicalize(word) ?? TagWordCatalog.TryCanonicalize(word) ?? word));

            var openChar = result[match.Index];
            var closeChar = openChar == '(' ? ')' : ']';
            result = result[..match.Index] + openChar + rewritten + closeChar + result[(match.Index + match.Length)..];
        }
        return result;
    }

    /// <summary>Strips everything between (and including) paired open/close markers —
    /// e.g. "(USA)", "(Rev 1)", "[!]". Handles multiple/nested groups in one linear
    /// pass (the original counted occurrences and repeated a Remove call per pair).</summary>
    private static string RemoveBetween(string s, char open, char close)
    {
        var sb = new StringBuilder(s.Length);
        var depth = 0;
        foreach (var c in s)
        {
            if (c == open) { depth++; continue; }
            if (c == close) { if (depth > 0) depth--; continue; }
            if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ApplyPunctuationRules(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var rule = Array.Find(PunctuationRules, r => r.Find == c);
            if (rule.Find == c)
            {
                if (rule.Replace is char replacement)
                    sb.Append(replacement);
                // else: delete (apostrophe) — append nothing
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Returns the single canonical form to store for this word: a Roman
    /// numeral gets converted to its arabic-digit equivalent; an arabic digit is
    /// already canonical and passes through unchanged; anything else (not a numeral at
    /// all) also passes through unchanged. So "Final Fantasy II" and "Final Fantasy 2"
    /// tokenize to the identical set regardless of which style either name used.
    ///
    /// Deliberately canonicalizes rather than adding a second alias token alongside the
    /// original the way this used to work — see ADR-0002 (docs/adr). Aliasing meant a
    /// numeral occupied two slots in its own token set ("2" AND "ii"), so two titles
    /// sharing only a sequel number could double-count that one shared concept against
    /// SimilarityScorer's flat per-token intersection count, outweighing what a single
    /// shared, genuinely distinctive word would contribute. Canonicalizing keeps the
    /// cross-notation matching without that duplication: a numeral occupies exactly one
    /// slot either way.
    ///
    /// Single Roman letters (I, V, X, L, C, D, M) are deliberately never auto-converted
    /// — too ambiguous with real standalone letters like "Mega Man X" — consistent with
    /// the 1-letter-token rule.</summary>
    private static string NormalizeNumeralToken(string token)
    {
        if (int.TryParse(token, out var n) && n is > 0 and <= 3999)
            return token; // already the canonical arabic form

        if (token.Length >= 2 && !KnownRomanLookalikes.Contains(token) && RomanNumeralPattern().IsMatch(token))
        {
            var arabic = RomanToArabic(token);
            if (arabic is > 0)
                return arabic.Value.ToString();
        }

        return token; // not a numeral at all
    }

    /// <summary>Real words that are also syntactically valid Roman numerals — most
    /// notably "CD" (400), which collides constantly with Sega CD/Mega CD/Turbo CD/
    /// Neo-Geo CD in ROM titles. Not exhaustive (e.g. "MIX" -> 1009 has the same
    /// problem); this covers the collision that's actually common in this domain.
    /// Extend if another false positive turns up in practice.</summary>
    private static readonly HashSet<string> KnownRomanLookalikes =
        new(StringComparer.OrdinalIgnoreCase) { "CD", "DC" };

    /// <summary>A 4-digit year in 1900-2009 also gets its 2-digit short form added
    /// ("1994" -> "94"), so "NBA Jam '94" and "NBA Jam 1994" cross-match. Always on,
    /// matching the original (it wasn't behind any checkbox there either).</summary>
    private static void AddYearAbbreviationVariant(HashSet<string> tokens, string token)
    {
        if (token.Length == 4 && int.TryParse(token, out var year) && year is > 1899 and < 2010)
            tokens.Add(token[2..]);
    }

    private static readonly Dictionary<char, int> RomanDigitValues = new()
    {
        ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000,
    };

    private static int? RomanToArabic(string token)
    {
        var s = token.ToUpperInvariant();
        var total = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var cur = RomanDigitValues[s[i]];
            var next = i + 1 < s.Length ? RomanDigitValues[s[i + 1]] : 0;
            total += cur < next ? -cur : cur;
        }
        return total;
    }

    [GeneratedRegex("^M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex RomanNumeralPattern();
}
