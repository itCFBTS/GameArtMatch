using System.Collections.Generic;

namespace GameArtMatch.Models;

/// <summary>Result of one full MatchingService.FindMatchesAsync pass — every scored
/// candidate, plus every ROM that scored zero candidates above the accuracy threshold
/// (computed as a side effect of the same scan, at no extra cost). MatchViewModel caches
/// Missing on itself so the Report page's Missing list can read it directly instead of
/// running its own separate, redundant re-scan just to reproduce the same list.</summary>
public sealed record MatchScanResult(IReadOnlyList<MatchCandidate> Candidates, IReadOnlyList<ReportEntry> Missing);
