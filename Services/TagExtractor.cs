using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameArtMatch.Services;

/// <summary>
/// Empirical groundwork for docs/adr/0004 and 0005 (and any future tag-handling
/// decision): before guessing a normalization/categorization scheme from the handful
/// of examples we've stumbled into, gather the actual raw tag vocabulary in use across
/// the whole library. Deliberately does no normalization or categorization itself —
/// see docs/catalog/README.md for how its output is meant to be used.
///
/// Reuses RegionCatalog.FindTagGroups (the same "(...)"/"[...]" pattern NameNormalizer
/// scores against) rather than a second regex, so this catalogs exactly the tag groups
/// the matching pipeline actually sees.
/// </summary>
public static class TagExtractor
{
    public readonly record struct TagOccurrence(int TotalCount, int RomCount, int ImageCount);

    /// <summary>Case-sensitive (StringComparer.Ordinal) on purpose: spelling variance
    /// between otherwise-equivalent tags (e.g. "Ja" vs "Japan", "GAMP" vs "Gump") is
    /// itself part of what this catalog exists to surface, not noise to fold away.</summary>
    public static Dictionary<string, TagOccurrence> ExtractTags(
        IEnumerable<string> romPaths, IEnumerable<string> imagePaths)
    {
        var counts = new Dictionary<string, TagOccurrence>(StringComparer.Ordinal);

        void Scan(IEnumerable<string> paths, bool isRomSide)
        {
            foreach (var path in paths)
            {
                var name = Path.GetFileNameWithoutExtension(path) ?? "";
                foreach (var match in RegionCatalog.FindTagGroups(name))
                {
                    var tag = match.Groups[1].Value.Trim();
                    if (tag.Length == 0)
                        continue;

                    counts.TryGetValue(tag, out var existing);
                    counts[tag] = new TagOccurrence(
                        existing.TotalCount + 1,
                        existing.RomCount + (isRomSide ? 1 : 0),
                        existing.ImageCount + (isRomSide ? 0 : 1));
                }
            }
        }

        Scan(romPaths, isRomSide: true);
        Scan(imagePaths, isRomSide: false);

        return counts;
    }

    /// <summary>Every file under root, recursively, skipping any "media" folder
    /// (MiSTer Console Mode's existing-art convention — see MatchingService.ListRoms)
    /// so its contents aren't scanned twice under two different roles. No extension
    /// filtering: tag content lives in the basename regardless of what the file turns
    /// out to be, and this is an exploratory catalog, not the matching pipeline.</summary>
    public static List<string> EnumerateAllFiles(string root)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return result;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(file) ?? "";
            if (dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals("media", StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(file);
        }

        return result;
    }
}
