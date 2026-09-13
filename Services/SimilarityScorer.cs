using System.Collections.Generic;
using System.Linq;

namespace GameArtMatch.Services;

/// <summary>
/// Scores two token sets, 0-100. This is FatMatch.exe's actual formula (reverse-
/// engineered from TryMatching's IL) — average the two directional containment
/// ratios: how much of A is found in B, and how much of B is found in A — just
/// computed correctly via real set intersection instead of the original's
/// substring/word-boundary string scanning. Two side effects of that fix, both
/// intentional: identical token sets naturally score exactly 100 (no need for the
/// original's hardcoded 98%/95% caps), and there's no separate "exact string" case
/// to special-case — the formula already gives the right answer.
/// </summary>
public static class SimilarityScorer
{
    public static double ScorePercent(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;

        var intersectCount = a.Count(b.Contains);
        if (intersectCount == 0)
            return 0;

        var scoreFromA = (double)intersectCount / a.Count;
        var scoreFromB = (double)intersectCount / b.Count;
        return (scoreFromA + scoreFromB) / 2 * 100;
    }
}
