namespace GameArtMatch.Models;

/// <summary>One row in the Report tab's Missing or Matched list.</summary>
public sealed record ReportEntry(string RomFileName, string? MatchedImageFileName);
