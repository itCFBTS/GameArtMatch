using System.Collections.Generic;

namespace GameArtMatch.Models;

/// <summary>One row in the Report window's Missing list. RomFullPath (not just
/// RomFileName) is needed to support right-click "Ignore" — see MatchCandidate.
/// RomFullPath for the same rationale. RomsPath is the scan's RomsPath at the time this
/// entry was produced, needed only to compute IgnorableAncestorFolders below.</summary>
public sealed record ReportEntry(string RomFileName, string RomFullPath, string? MatchedImageFileName, string RomsPath)
{
    /// <summary>Every folder between this ROM and RomsPath, nearest first — backs the
    /// Report window's "Ignore Folder" submenu the same way RomMatchGroup.
    /// IgnorableAncestorFolders backs MatchView's. See FolderAncestry for why RomsPath
    /// itself is never included.</summary>
    public IReadOnlyList<string> IgnorableAncestorFolders => FolderAncestry.ComputeIgnorableAncestorFolders(RomFullPath, RomsPath);

    public bool HasIgnorableAncestorFolders => IgnorableAncestorFolders.Count > 0;
}
