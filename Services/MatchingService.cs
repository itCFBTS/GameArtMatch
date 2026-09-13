using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

/// <summary>
/// Real implementation, replacing the earlier exact-basename stub. Scoring rules
/// (NameNormalizer/SimilarityScorer) are ported from FatMatch.exe's actual compiled
/// logic, reverse-engineered from its IL — see those two classes' doc comments for
/// what's faithfully kept vs. deliberately fixed.
///
/// Performance: FatMatch scored every ROM against every image (O(n*m) — ~113 million
/// pairs for the PS1 Redump set alone). This builds an inverted index over the images'
/// tokens once, then only scores a ROM against images sharing at least one token with
/// it — a real match can't exist without that overlap, so nothing is lost, just the
/// pairs that could never have cleared the threshold.
/// </summary>
public sealed class MatchingService : IMatchingService
{
    private readonly record struct ImageEntry(string Path, HashSet<string> Tokens);

    public async Task<IReadOnlyList<MatchCandidate>> FindMatchesAsync(
        MatchSettings settings,
        IProgress<MatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var roms = ListRoms(settings);
            var images = ListFiles(settings.ImagesPath, settings.ImagesExtensions, settings.ImagesIncludeSubfolders);
            var (index, entries) = BuildImageIndex(images, settings);

            var results = new List<MatchCandidate>();
            var imageUseCount = new Dictionary<string, int>();

            // Throttled to ~200 reports total regardless of set size — reporting every
            // single item on a 10,000+ ROM set would flood the UI thread with far more
            // dispatcher posts than a progress bar can even visually distinguish.
            var reportInterval = Math.Max(1, roms.Count / 200);

            for (var i = 0; i < roms.Count; i++)
            {
                var rom = roms[i];
                cancellationToken.ThrowIfCancellationRequested();

                if (i % reportInterval == 0 || i == roms.Count - 1)
                    progress?.Report(new MatchProgress(i + 1, roms.Count, Path.GetFileName(rom)));

                var romTokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(rom) ?? "", settings);
                var candidates = FindCandidates(romTokens, index, entries, settings.AccuracyThreshold);

                foreach (var (entry, score) in candidates.OrderByDescending(c => c.Score))
                {
                    results.Add(new MatchCandidate
                    {
                        RomFileName = Path.GetFileName(rom),
                        ImageFileName = Path.GetFileName(entry.Path),
                        ImageFullPath = entry.Path,
                        ScorePercent = Math.Round(score, 1),
                    });
                    imageUseCount[entry.Path] = imageUseCount.GetValueOrDefault(entry.Path) + 1;
                }
            }

            foreach (var candidate in results)
                candidate.IsDuplicate = imageUseCount[candidate.ImageFullPath] > 1;

            return (IReadOnlyList<MatchCandidate>)results;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportEntry>> FindMissingAsync(MatchSettings settings, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var roms = ListRoms(settings);
            var images = ListFiles(settings.ImagesPath, settings.ImagesExtensions, settings.ImagesIncludeSubfolders);
            var (index, entries) = BuildImageIndex(images, settings);

            var missing = new List<ReportEntry>();
            foreach (var rom in roms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var romTokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(rom) ?? "", settings);
                if (FindCandidates(romTokens, index, entries, settings.AccuracyThreshold).Count == 0)
                    missing.Add(new ReportEntry(Path.GetFileName(rom), null));
            }

            return (IReadOnlyList<ReportEntry>)missing;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportEntry>> FindMatchedAsync(MatchSettings settings, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var roms = ListRoms(settings);
            var images = ListFiles(settings.ImagesPath, settings.ImagesExtensions, settings.ImagesIncludeSubfolders);
            var (index, entries) = BuildImageIndex(images, settings);

            var matched = new List<ReportEntry>();
            foreach (var rom in roms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var romTokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(rom) ?? "", settings);
                var candidates = FindCandidates(romTokens, index, entries, settings.AccuracyThreshold);
                if (candidates.Count > 0)
                {
                    var best = candidates.OrderByDescending(c => c.Score).First();
                    matched.Add(new ReportEntry(Path.GetFileName(rom), Path.GetFileName(best.Entry.Path)));
                }
            }

            return (IReadOnlyList<ReportEntry>)matched;
        }, cancellationToken);
    }

    private static (Dictionary<string, List<int>> Index, List<ImageEntry> Entries) BuildImageIndex(
        List<string> imagePaths, MatchSettings settings)
    {
        var entries = new List<ImageEntry>(imagePaths.Count);
        var index = new Dictionary<string, List<int>>(
            settings.MatchCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < imagePaths.Count; i++)
        {
            var tokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(imagePaths[i]) ?? "", settings);
            entries.Add(new ImageEntry(imagePaths[i], tokens));

            foreach (var token in tokens)
            {
                if (!index.TryGetValue(token, out var list))
                    index[token] = list = [];
                list.Add(i);
            }
        }

        return (index, entries);
    }

    private static List<(ImageEntry Entry, double Score)> FindCandidates(
        HashSet<string> romTokens, Dictionary<string, List<int>> index, List<ImageEntry> entries, double thresholdPercent)
    {
        if (romTokens.Count == 0)
            return [];

        var candidateIndices = new HashSet<int>();
        foreach (var token in romTokens)
            if (index.TryGetValue(token, out var list))
                candidateIndices.UnionWith(list);

        var results = new List<(ImageEntry, double)>();
        foreach (var idx in candidateIndices)
        {
            var score = SimilarityScorer.ScorePercent(romTokens, entries[idx].Tokens);
            if (score >= thresholdPercent)
                results.Add((entries[idx], score));
        }

        return results;
    }

    private static readonly string[] MisterArtExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>MiSTer Console Mode convention: box art lives in a "media" subfolder next
    /// to the ROMs, named after the ROM (any extension). Used to skip ROMs that already
    /// have art in place, rather than re-matching/overwriting them.</summary>
    private static bool HasExistingArt(string romsPath, string? romBaseName)
    {
        if (string.IsNullOrWhiteSpace(romBaseName))
            return false;

        var mediaDir = Path.Combine(romsPath, "media");
        if (!Directory.Exists(mediaDir))
            return false;

        return MisterArtExtensions.Any(ext => File.Exists(Path.Combine(mediaDir, romBaseName + ext)));
    }

    /// <summary>ROMs listing that, in Console Mode, always excludes the "media" subfolder —
    /// otherwise a recursive scan would treat existing art files as if they were ROMs.</summary>
    private static List<string> ListRoms(MatchSettings settings)
    {
        var files = ListFiles(settings.RomsPath, settings.RomsExtensions, settings.RomsIncludeSubfolders);

        var roms = !settings.IsConsoleMode || !settings.RomsIncludeSubfolders
            ? files
            : files.Where(f => !f.StartsWith(
                Path.Combine(settings.RomsPath, "media") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)).ToList();

        if (settings.IsConsoleMode && settings.SkipExistingArt)
            roms = roms.Where(r => !HasExistingArt(settings.RomsPath, Path.GetFileNameWithoutExtension(r))).ToList();

        return roms;
    }

    private static List<string> ListFiles(string folder, string extensionSpec, bool includeSubfolders = false)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var extensions = extensionSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (extensions.Length == 0 || extensions.Contains("*"))
            return Directory.EnumerateFiles(folder, "*", searchOption).ToList();

        return extensions
            .SelectMany(ext => Directory.EnumerateFiles(folder, $"*.{ext.TrimStart('.')}", searchOption))
            .Distinct()
            .ToList();
    }
}
