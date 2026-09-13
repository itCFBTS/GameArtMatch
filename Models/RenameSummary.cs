namespace GameArtMatch.Models;

/// <summary>Result of a rename/copy pass — see IRenameService.</summary>
public sealed record RenameSummary(int Renamed, int Skipped, int Failed)
{
    public string ToStatusText() =>
        Skipped == 0 && Failed == 0
            ? $"Renamed {Renamed} file(s)."
            : $"Renamed {Renamed} file(s). {Skipped} skipped (destination already existed). {Failed} failed.";
}
