namespace GameArtMatch.Models;

/// <summary>One row in the Report window's Missing or Matched list. RomFullPath (not
/// just RomFileName) is needed to support right-click "Ignore" from these tabs — see
/// MatchCandidate.RomFullPath for the same rationale.</summary>
public sealed record ReportEntry(string RomFileName, string RomFullPath, string? MatchedImageFileName);
