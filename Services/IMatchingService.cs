using System;
using System.Threading;
using System.Threading.Tasks;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

/// <summary>
/// The fuzzy-matching engine — the actual "brain" ported from FatMatch.exe's real
/// compiled logic (tag stripping, Roman numerals, common words, standalone letters),
/// reverse-engineered from its IL. See MatchingService, NameNormalizer, and
/// SimilarityScorer for the real implementation.
/// </summary>
public interface IMatchingService
{
    Task<MatchScanResult> FindMatchesAsync(
        MatchSettings settings,
        IProgress<MatchProgress>? progress,
        IProgress<RomMatchResult>? romMatched,
        CancellationToken cancellationToken);
}
