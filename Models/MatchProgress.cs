namespace GameArtMatch.Models;

/// <summary>Which stage of FindMatchesAsync a MatchProgress report describes. The UI
/// maps each phase into its own slice of one overall progress bar (see
/// MatchViewModel.IndexingPhaseWeight) rather than showing two separate bars.</summary>
public enum MatchPhase
{
    /// <summary>Tokenizing every image up front to build the inverted index — see
    /// MatchingService.BuildImageIndex. Silent before this was added, since it happens
    /// entirely before the first ROM is scored.</summary>
    Indexing,
    Scanning,
}

/// <summary>Progress through a FindMatchesAsync scan — Current/Total drive a real
/// determinate progress bar rather than an indeterminate spinner, since both the image
/// count (Indexing phase) and ROM count (Scanning phase) are known upfront.</summary>
public readonly record struct MatchProgress(MatchPhase Phase, int Current, int Total, string CurrentName)
{
    public double PercentComplete => Total == 0 ? 0 : (double)Current / Total * 100;
}
