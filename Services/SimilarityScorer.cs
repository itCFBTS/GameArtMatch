using System;
using System.Collections.Generic;
using System.Linq;

namespace GameArtMatch.Services;

/// <summary>
/// Scores two token sets, 0-100. Rooted in FatMatch.exe's actual formula (reverse-
/// engineered from TryMatching's IL) — average the two directional containment
/// ratios: how much of A is found in B, and how much of B is found in A — computed
/// correctly via real set intersection instead of the original's substring/word-
/// boundary string scanning.
///
/// tokenWeight (see docs/adr/0003-corpus-frequency-weighted-similarity-scoring.md)
/// lets a caller weight each token by how rare it is across the scan's corpus
/// instead of counting every shared token equally — a bare sequel number and a
/// franchise name used to count the same. Both the intersection AND each set's own
/// total get weighted (not just the intersection) — that's what preserves the
/// property below for ANY weighting scheme, not just the unweighted default:
/// identical token sets always score exactly 100, since numerator and denominator
/// become the same sum when a == b, regardless of what the individual weights are.
/// Omitting tokenWeight (or passing null) falls back to every token being worth 1 —
/// today's original, unweighted behavior.
/// </summary>
public static class SimilarityScorer
{
    public static double ScorePercent(HashSet<string> a, HashSet<string> b, Func<string, double>? tokenWeight = null)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;

        var weight = tokenWeight ?? (static _ => 1.0);

        var intersectWeight = 0.0;
        foreach (var token in a)
            if (b.Contains(token))
                intersectWeight += weight(token);

        if (intersectWeight <= 0)
            return 0;

        var totalA = a.Sum(weight);
        var totalB = b.Sum(weight);

        // A set made entirely of maximally-common (weight-0) tokens has no
        // informative content to compare with — same "nothing to score" outcome as
        // an empty set, rather than dividing by zero.
        if (totalA <= 0 || totalB <= 0)
            return 0;

        var scoreFromA = intersectWeight / totalA;
        var scoreFromB = intersectWeight / totalB;
        return (scoreFromA + scoreFromB) / 2 * 100;
    }
}
