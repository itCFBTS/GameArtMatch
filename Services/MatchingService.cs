using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            var (index, entries) = BuildImageIndex(images, settings, progress, cancellationToken);

            var results = new List<MatchCandidate>();
            var imageUseCount = new Dictionary<string, int>();

            // Populated lazily inside FindCandidates, keyed by index into entries — an
            // image can legitimately be a candidate for multiple ROMs, so its full
            // (tag-inclusive) tokens are computed at most once per scan, only for images
            // that actually clear the accuracy threshold for at least one ROM.
            var fullTokenCache = new Dictionary<int, HashSet<string>?>();

            // Same lazy-per-image-index caching rationale as fullTokenCache — an image's
            // content hash is a pure function of its own bytes, computed at most once per
            // scan regardless of how many ROMs it's a candidate for.
            var contentHashCache = new Dictionary<int, string>();

            // Throttled to ~200 reports total regardless of set size — reporting every
            // single item on a 10,000+ ROM set would flood the UI thread with far more
            // dispatcher posts than a progress bar can even visually distinguish.
            var reportInterval = Math.Max(1, roms.Count / 200);

            for (var i = 0; i < roms.Count; i++)
            {
                var rom = roms[i];
                cancellationToken.ThrowIfCancellationRequested();

                if (i % reportInterval == 0 || i == roms.Count - 1)
                    progress?.Report(new MatchProgress(MatchPhase.Scanning, i + 1, roms.Count, Path.GetFileName(rom)));

                var romFileName = Path.GetFileName(rom);
                var romBaseName = Path.GetFileNameWithoutExtension(rom) ?? "";
                var romTokens = NameNormalizer.ToTokens(romBaseName, settings);

                // When DisregardRomTags is off, StripPerSettings already includes tags,
                // so a second "full" tokenization would just recompute the same set —
                // null here is the signal FindCandidates uses to skip that redundant work.
                var romFullTokens = settings.DisregardRomTags
                    ? NameNormalizer.ToTokens(romBaseName, settings, NameNormalizer.TagHandling.ForceInclude)
                    : null;

                var candidates = FindCandidates(romFileName, romTokens, romFullTokens, index, entries, fullTokenCache, contentHashCache, settings.AccuracyThreshold, settings);

                foreach (var c in candidates.OrderByDescending(x => x.DisplayScore))
                {
                    results.Add(new MatchCandidate
                    {
                        RomFileName = romFileName,
                        RomFullPath = rom,
                        ImageFileName = Path.GetFileName(c.Entry.Path),
                        ImageFullPath = c.Entry.Path,
                        ScorePercent = c.DisplayScore,
                        IsExactMatch = c.IsExactMatch,
                        ContentHash = c.ContentHash,
                    });
                    imageUseCount[c.Entry.Path] = imageUseCount.GetValueOrDefault(c.Entry.Path) + 1;
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
            var (index, entries) = BuildImageIndex(images, settings, progress: null, cancellationToken);

            // romFullTokens/contentHashCache deliberately null — the Report tab never
            // surfaces scores or a content-identical marker (ReportEntry has neither
            // field), so there's no reason to pay for the tag-inclusive rescore or the
            // file-hashing pass here. Deliberate opt-outs for cost, not oversights —
            // don't "fix" these to match the Match tab's behavior.
            var unusedTokenCache = new Dictionary<int, HashSet<string>?>();

            var missing = new List<ReportEntry>();
            foreach (var rom in roms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var romTokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(rom) ?? "", settings);
                if (FindCandidates(Path.GetFileName(rom), romTokens, null, index, entries, unusedTokenCache, null, settings.AccuracyThreshold, settings).Count == 0)
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
            var (index, entries) = BuildImageIndex(images, settings, progress: null, cancellationToken);

            // See FindMissingAsync above — romFullTokens/contentHashCache deliberately null here too.
            var unusedTokenCache = new Dictionary<int, HashSet<string>?>();

            var matched = new List<ReportEntry>();
            foreach (var rom in roms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var romTokens = NameNormalizer.ToTokens(Path.GetFileNameWithoutExtension(rom) ?? "", settings);
                var candidates = FindCandidates(Path.GetFileName(rom), romTokens, null, index, entries, unusedTokenCache, null, settings.AccuracyThreshold, settings);
                if (candidates.Count > 0)
                {
                    var best = candidates.OrderByDescending(c => c.DisplayScore).First();
                    matched.Add(new ReportEntry(Path.GetFileName(rom), Path.GetFileName(best.Entry.Path)));
                }
            }

            return (IReadOnlyList<ReportEntry>)matched;
        }, cancellationToken);
    }

    private static (Dictionary<string, List<int>> Index, List<ImageEntry> Entries) BuildImageIndex(
        List<string> imagePaths, MatchSettings settings,
        IProgress<MatchProgress>? progress, CancellationToken cancellationToken)
    {
        var entries = new List<ImageEntry>(imagePaths.Count);
        var index = new Dictionary<string, List<int>>(
            settings.MatchCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        // Same throttling rationale as the ROM scan loop below, plus this is otherwise a
        // silent, potentially slow pass (tokenizing every image) with no feedback at all —
        // the whole reason a separate Indexing phase exists in MatchProgress.
        var reportInterval = Math.Max(1, imagePaths.Count / 200);

        for (var i = 0; i < imagePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i % reportInterval == 0 || i == imagePaths.Count - 1)
                progress?.Report(new MatchProgress(MatchPhase.Indexing, i + 1, imagePaths.Count, Path.GetFileName(imagePaths[i])));

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

    private readonly record struct CandidateResult(ImageEntry Entry, double DisplayScore, bool IsExactMatch, string ContentHash);

    /// <summary>Finds every image scoring at or above thresholdPercent against romTokens
    /// (the tag-stripped title score — unchanged, this is what gates candidacy/recall).
    /// For each survivor, also computes the DISPLAYED score: when romFullTokens is
    /// non-null (DisregardRomTags is on), that's a second, tag-inclusive comparison
    /// against the image's own full tokens (computed lazily and cached in
    /// fullTokenCache, since the same image can be a candidate for multiple ROMs) — so
    /// identical filenames still score 100 while differently-tagged siblings score
    /// lower. When romFullTokens is null, the tag-stripped score IS the full score
    /// (nothing was stripped to begin with), so it's reused with no extra work.
    /// contentHashCache works the same lazy-per-image way for the file's content hash;
    /// pass null to skip that work entirely for callers that don't need it (see the
    /// Report-tab call sites).</summary>
    private static List<CandidateResult> FindCandidates(
        string romFileName, HashSet<string> romTokens, HashSet<string>? romFullTokens,
        Dictionary<string, List<int>> index, List<ImageEntry> entries,
        Dictionary<int, HashSet<string>?> fullTokenCache, Dictionary<int, string>? contentHashCache,
        double thresholdPercent, MatchSettings settings)
    {
        if (romTokens.Count == 0)
            return [];

        var candidateIndices = new HashSet<int>();
        foreach (var token in romTokens)
            if (index.TryGetValue(token, out var list))
                candidateIndices.UnionWith(list);

        var romBaseName = Path.GetFileNameWithoutExtension(romFileName);

        var results = new List<CandidateResult>();
        foreach (var idx in candidateIndices)
        {
            var entry = entries[idx];
            var score = SimilarityScorer.ScorePercent(romTokens, entry.Tokens);
            if (score < thresholdPercent)
                continue;

            var displayScore = score;
            if (romFullTokens is not null)
            {
                if (!fullTokenCache.TryGetValue(idx, out var imageFullTokens))
                {
                    imageFullTokens = NameNormalizer.ToTokens(
                        Path.GetFileNameWithoutExtension(entry.Path) ?? "", settings, NameNormalizer.TagHandling.ForceInclude);
                    fullTokenCache[idx] = imageFullTokens;
                }

                displayScore = SimilarityScorer.ScorePercent(romFullTokens, imageFullTokens!);
            }

            var contentHash = "";
            if (contentHashCache is not null)
            {
                if (!contentHashCache.TryGetValue(idx, out var hash))
                {
                    hash = ComputeContentHash(entry.Path);
                    contentHashCache[idx] = hash;
                }
                contentHash = hash;
            }

            var isExactMatch = string.Equals(romBaseName, Path.GetFileNameWithoutExtension(entry.Path), StringComparison.OrdinalIgnoreCase);
            results.Add(new CandidateResult(entry, Math.Round(displayScore, 1), isExactMatch, contentHash));
        }

        return results;
    }

    /// <summary>SHA-256 of the file's raw bytes — a strict "same file or not" check (see
    /// MatchCandidate.ContentHash), deliberately not a perceptual/similarity hash.</summary>
    private static string ComputeContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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

        // Applied last and to every caller (FindMatchesAsync/FindMissingAsync/
        // FindMatchedAsync all funnel through here) so an ignored ROM never resurfaces
        // in any of the three, regardless of which RomsPath it's currently found under.
        if (settings.IgnoredRomPaths.Count > 0)
            roms = roms.Where(r => !settings.IgnoredRomPaths.Contains(r)).ToList();

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
