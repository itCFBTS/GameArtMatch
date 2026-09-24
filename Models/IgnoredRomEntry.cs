using System.IO;

namespace GameArtMatch.Models;

/// <summary>One leaf row under a folder group in the Options pane's Ignored ROMs page
/// tree — FullPath is what Un-ignore/Open File Path act on, FileName is what's shown.</summary>
public sealed record IgnoredRomEntry(string FullPath)
{
    public string FileName => Path.GetFileName(FullPath);
}
