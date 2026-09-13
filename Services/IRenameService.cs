using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

public interface IRenameService
{
    Task<RenameSummary> RenameAsync(
        IReadOnlyList<MatchCandidate> selectedCandidates,
        MatchSettings settings,
        CancellationToken cancellationToken);
}
