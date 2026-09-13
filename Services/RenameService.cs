using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

/// <summary>
/// Performs the actual file operation behind "Rename Selected". Two distinct modes,
/// matching the art pack's own ReadMe:
///   - DeleteOriginalFiles = false (default, safe): copy the image into
///     "&lt;ImagesPath&gt;/renamed/&lt;RomBaseName&gt;.&lt;ext&gt;" — originals untouched.
///   - DeleteOriginalFiles = true: a true in-place rename (File.Move) — the old
///     filename stops existing. This is the destructive path the ReadMe warns about.
/// Never overwrites an existing destination — a name collision is skipped, not
/// clobbered, and reported back via RenameSummary.
/// </summary>
public sealed class RenameService : IRenameService
{
    public async Task<RenameSummary> RenameAsync(
        IReadOnlyList<MatchCandidate> selectedCandidates,
        MatchSettings settings,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var renamed = 0;
            var skipped = 0;
            var failed = 0;
            var copyScriptLines = new List<string>();
            var undoScriptLines = new List<string>();

            string? renamedDir = null;
            if (!settings.DeleteOriginalFiles)
            {
                renamedDir = Path.Combine(settings.ImagesPath, "renamed");
                Directory.CreateDirectory(renamedDir);
            }

            foreach (var candidate in selectedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(candidate.ImageFullPath);
                var newFileName = Path.GetFileNameWithoutExtension(candidate.RomFileName) + ext;

                try
                {
                    if (settings.DeleteOriginalFiles)
                    {
                        var sourceDir = Path.GetDirectoryName(candidate.ImageFullPath)!;
                        var destPath = Path.Combine(sourceDir, newFileName);

                        if (PathsEqual(destPath, candidate.ImageFullPath))
                        {
                            renamed++; // already correctly named — nothing to do
                            continue;
                        }

                        if (File.Exists(destPath))
                        {
                            skipped++;
                            continue;
                        }

                        File.Move(candidate.ImageFullPath, destPath);
                        copyScriptLines.Add(MoveCommand(candidate.ImageFullPath, destPath));
                        undoScriptLines.Add(MoveCommand(destPath, candidate.ImageFullPath));
                        renamed++;
                    }
                    else
                    {
                        var destPath = Path.Combine(renamedDir!, newFileName);

                        if (File.Exists(destPath))
                        {
                            skipped++;
                            continue;
                        }

                        File.Copy(candidate.ImageFullPath, destPath);
                        copyScriptLines.Add(CopyCommand(candidate.ImageFullPath, destPath));
                        undoScriptLines.Add(DeleteCommand(destPath));
                        renamed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            if (settings.ExportCopyScript)
                WriteScript(settings.ImagesPath, "copy_script", copyScriptLines);
            if (settings.ExportBackupScript)
                WriteScript(settings.ImagesPath, "undo_script", undoScriptLines);

            return new RenameSummary(renamed, skipped, failed);
        }, cancellationToken);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string CopyCommand(string src, string dest) =>
        OperatingSystem.IsWindows() ? $"copy \"{src}\" \"{dest}\"" : $"cp \"{src}\" \"{dest}\"";

    private static string MoveCommand(string src, string dest) =>
        OperatingSystem.IsWindows() ? $"move \"{src}\" \"{dest}\"" : $"mv \"{src}\" \"{dest}\"";

    private static string DeleteCommand(string path) =>
        OperatingSystem.IsWindows() ? $"del \"{path}\"" : $"rm \"{path}\"";

    private static void WriteScript(string folder, string baseName, List<string> lines)
    {
        if (lines.Count == 0)
            return;

        var ext = OperatingSystem.IsWindows() ? ".bat" : ".sh";
        var path = Path.Combine(folder, baseName + ext);
        var content = OperatingSystem.IsWindows()
            ? "@echo off\r\n" + string.Join("\r\n", lines) + "\r\n"
            : "#!/usr/bin/env bash\nset -e\n" + string.Join("\n", lines) + "\n";

        File.WriteAllText(path, content);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                // Filesystem doesn't support Unix permission bits — script still works,
                // just needs `bash script.sh` instead of `./script.sh`.
            }
        }
    }
}
