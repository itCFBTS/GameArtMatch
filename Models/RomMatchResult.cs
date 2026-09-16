using System.Collections.Generic;

namespace GameArtMatch.Models;

/// <summary>One ROM's full, freshly-scored candidate list, reported live as
/// MatchingService.FindMatchesAsync computes it — well before the whole scan finishes.
/// IsDuplicate is always false on every candidate here: it's the one field that's
/// genuinely cross-ROM (an image only "counts" as reused once every ROM that might
/// claim it has been scanned), so it can't be known yet at this point — MatchingService
/// corrects it on these same candidate instances in one pass after the whole scan
/// completes, which raises PropertyChanged normally since it's already bindable.</summary>
public sealed record RomMatchResult(string RomFileName, IReadOnlyList<MatchCandidate> Candidates);
