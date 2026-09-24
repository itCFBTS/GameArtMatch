using System;
using System.Collections.Generic;
using System.IO;

namespace GameArtMatch.Models;

/// <summary>Shared folder-path helpers backing "Ignore Folder" everywhere it appears
/// (MatchView's ROM rows, the Report page's Missing list) and the scan-time filtering
/// that actually excludes an ignored folder's contents (MatchingService.ListRoms).</summary>
public static class FolderAncestry
{
    /// <summary>Every folder between fullPath and romsPath, nearest first — e.g. for a
    /// ROM at ".../Saturn/Extras/Palettes/file.pal" with romsPath ".../Saturn", this is
    /// [".../Saturn/Extras/Palettes", ".../Saturn/Extras"]. romsPath itself is
    /// deliberately never included — ignoring the folder explicitly configured as the
    /// scan root would silently zero out an entire system with no obvious explanation
    /// why, so "Ignore Folder" never offers it as an option.</summary>
    public static List<string> ComputeIgnorableAncestorFolders(string fullPath, string romsPath)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(romsPath))
            return result;

        var root = romsPath.TrimEnd('/', '\\');
        var folder = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(folder)
               && !string.Equals(folder.TrimEnd('/', '\\'), root, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(folder);
            folder = Path.GetDirectoryName(folder);
        }

        return result;
    }

    /// <summary>True when path is folder itself or lives underneath it. A plain
    /// StartsWith would also match "/roms/Sat" against "/roms/Saturn2/game.rom" — this
    /// requires the next character after the prefix to be a real path separator (or
    /// nothing at all, for an exact match), so a folder name that's merely a textual
    /// prefix of a sibling folder's name never gets swept in by accident.</summary>
    public static bool IsUnderFolder(string path, string folder)
    {
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Length == folder.Length || path[folder.Length] is '/' or '\\';
    }
}
