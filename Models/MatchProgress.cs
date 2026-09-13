namespace GameArtMatch.Models;

/// <summary>Progress through a FindMatchesAsync scan — Current/Total drive a real
/// determinate progress bar rather than an indeterminate spinner, since the ROM count
/// is known upfront.</summary>
public readonly record struct MatchProgress(int Current, int Total, string CurrentName)
{
    public double PercentComplete => Total == 0 ? 0 : (double)Current / Total * 100;
}
