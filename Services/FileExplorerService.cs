using System;
using System.Diagnostics;
using System.IO;

namespace GameArtMatch.Services;

/// <summary>
/// Backs every "Open File Path" context-menu item (MatchView's ROM/image rows,
/// ReportView's Missing tab, Options' Ignored ROMs tab) — reveals a single file or
/// folder in the OS's file manager. Always acts on exactly one item regardless of the
/// row's broader multi-selection (unlike "Ignore Selected ROM(s)"), since spawning a
/// file-manager window per selected row wouldn't make sense.
/// </summary>
public static class FileExplorerService
{
    /// <summary>For a file — opens its CONTAINING folder, with the file itself
    /// pre-selected where the OS supports that.</summary>
    public static void Reveal(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            // Two separate arguments (not one "/select,\"path\"" string) — this is
            // the well-known working form; Explorer parses "/select," as a prefix
            // token and the next argument as the path to highlight.
            RunProcess("explorer.exe", "/select,", fullPath);
        }
        else
        {
            // No cross-desktop-environment equivalent of Windows' "/select," on
            // Linux (Nautilus/Dolphin/Thunar/etc. each have their own, if any) —
            // xdg-open on the containing folder is the one thing that reliably
            // opens *some* file manager window in the right place, just without the
            // file itself pre-selected.
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                RunProcess("xdg-open", directory);
        }
    }

    /// <summary>For a folder (e.g. an entry from IgnoredRomsViewModel.IgnoredFolders) —
    /// opens the folder ITSELF, not its parent with the folder selected. Reveal above
    /// would open the folder's parent instead, which is right for a file but wrong here.</summary>
    public static void RevealFolder(string folderPath)
    {
        if (OperatingSystem.IsWindows())
            RunProcess("explorer.exe", folderPath);
        else
            RunProcess("xdg-open", folderPath);
    }

    /// <summary>Broad catch deliberately, same reasoning as MatchViewModel.LoadPreviewAsync
    /// and MatchingService.ComputeContentHash: this shells out to an external program
    /// (explorer.exe, xdg-open) whose presence/behavior this app doesn't control — a
    /// missing binary or an unusual desktop setup shouldn't crash the app over what's
    /// ultimately just a convenience action.</summary>
    private static void RunProcess(string fileName, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);
            Process.Start(info);
        }
        catch
        {
            // Swallowed — see doc comment above.
        }
    }
}
