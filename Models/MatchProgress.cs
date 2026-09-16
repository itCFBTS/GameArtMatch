namespace GameArtMatch.Models;

/// <summary>Which stage of FindMatchesAsync a MatchProgress report describes. The UI
/// maps each phase into its own slice of one overall progress bar (see
/// MatchViewModel.IndexingPhaseWeight) rather than showing two separate bars.</summary>
public enum MatchPhase
{
    /// <summary>Enumerating ROM/image files on disk (MatchingService.ListRoms/
    /// ListFiles) — no notion of a total up front (the directory walk streams files
    /// lazily rather than knowing the count in advance). Current is how many files have
    /// matched the configured extension(s) so far (and, for ROMs with Console Mode +
    /// "Skip ROMs that already have matching art" both on, aren't already excluded by
    /// that check either — see MatchingService.ListRoms, which builds the existing-art
    /// lookup before this walk starts so an excluded ROM is never counted here in the
    /// first place, rather than being counted then filtered back out afterward);
    /// CurrentName says which listing this is ("ROMs" or "images") — see MatchViewModel,
    /// which keeps the ROM figure on screen once that listing finishes rather than
    /// replacing it when the image count starts appearing alongside it. Total is a
    /// running "examined so far" tally (matching or not) — not shown to the user, but
    /// still what throttles how often this reports: a folder tree can contain a long
    /// stretch of non-matching files between one matched file and the next, and
    /// throttling on the matched count alone would go silent for that whole stretch,
    /// looking exactly like a freeze even though the walk is still actively
    /// progressing.</summary>
    Listing,

    /// <summary>Tokenizing every image up front to build the inverted index — see
    /// MatchingService.BuildImageIndex.</summary>
    Indexing,

    /// <summary>Scoring each ROM against the image index — the actual ROM-to-image
    /// matching pass (see MatchingService.FindCandidates).</summary>
    Matching,
}

/// <summary>Progress through a FindMatchesAsync scan — Current/Total drive a real
/// determinate progress bar rather than an indeterminate spinner, since both the image
/// count (Indexing phase) and ROM count (Matching phase) are known upfront (Listing is
/// the exception — see MatchPhase.Listing).</summary>
public readonly record struct MatchProgress(MatchPhase Phase, int Current, int Total, string CurrentName)
{
    public double PercentComplete => Total == 0 ? 0 : (double)Current / Total * 100;
}
