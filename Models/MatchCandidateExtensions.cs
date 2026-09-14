using System.Collections.Generic;
using System.Linq;

namespace GameArtMatch.Models;

public static class MatchCandidateExtensions
{
    /// <summary>The ordering "best match first" logic shares everywhere a group needs to
    /// pick one candidate to auto-select/display: an exact filename match always wins,
    /// even in a hypothetical near-tie, rather than relying solely on ScorePercent to
    /// always rank it strictly highest.</summary>
    public static IOrderedEnumerable<MatchCandidate> OrderByBestMatch(this IEnumerable<MatchCandidate> candidates) =>
        candidates.OrderByDescending(c => c.IsExactMatch).ThenByDescending(c => c.ScorePercent);
}
