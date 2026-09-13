using System;
using System.Collections.Generic;
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
    Task<IReadOnlyList<MatchCandidate>> FindMatchesAsync(
        MatchSettings settings,
        IProgress<MatchProgress>? progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportEntry>> FindMissingAsync(
        MatchSettings settings,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportEntry>> FindMatchedAsync(
        MatchSettings settings,
        CancellationToken cancellationToken);
}
