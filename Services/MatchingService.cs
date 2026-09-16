using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public async Task<MatchScanResult> FindMatchesAsync(
        MatchSettings settings,
        IProgress<MatchProgress>? progress,
        IProgress<RomMatchResult>? romMatched,
        CancellationToken cancellationToken)
    {
        var (results, missing, imageUseCount) = await Task.Run(() =>
        {
            // TEMPORARY: per-phase timing to inform how the progress bar should weight
            // each phase (see MatchViewModel.IndexingPhaseWeight, currently a guess) —
            // remove once that decision is made from real numbers across a few
            // differently-sized ROM sets.
            var totalStopwatch = Stopwatch.StartNew();

            progress?.Report(new MatchProgress(MatchPhase.Listing, 0, 0, "ROMs"));
            var romsStopwatch = Stopwatch.StartNew();
            var roms = ListRoms(settings, progress, cancellationToken);
            Console.WriteLine($"[Timing] ROMs listing (incl. existing-art check): {romsStopwatch.ElapsedMilliseconds}ms, {roms.Count} ROMs");

            progress?.Report(new MatchProgress(MatchPhase.Listing, 0, 0, "images"));
            var imagesStopwatch = Stopwatch.StartNew();
            var images = ListFiles(settings.ImagesPath, settings.ImagesExtensions, settings.ImagesIncludeSubfolders, progress, "images", cancellationToken);
            Console.WriteLine($"[Timing] Images listing: {imagesStopwatch.ElapsedMilliseconds}ms, {images.Count} images");

            var indexStopwatch = Stopwatch.StartNew();
            var (index, entries) = BuildImageIndex(images, settings, progress, cancellationToken);
            Console.WriteLine($"[Timing] Indexing: {indexStopwatch.ElapsedMilliseconds}ms");

            var results = new List<MatchCandidate>();
            // A ROM that scores zero candidates above the threshold — tracked as a side
            // effect of this same pass rather than via a separate FindMissingAsync scan,
            // so the Report window's Missing tab (see MatchViewModel.MissingRoms) can
            // just read this instead of re-scanning from scratch.
            var missing = new List<ReportEntry>();
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

            var matchingStopwatch = Stopwatch.StartNew();
            for (var i = 0; i < roms.Count; i++)
            {
                var rom = roms[i];
                cancellationToken.ThrowIfCancellationRequested();

                if (i % reportInterval == 0 || i == roms.Count - 1)
                    progress?.Report(new MatchProgress(MatchPhase.Matching, i + 1, roms.Count, Path.GetFileName(rom)));

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

                if (candidates.Count == 0)
                {
                    missing.Add(new ReportEntry(romFileName, rom, null, settings.RomsPath));
                    continue;
                }

                var romResults = new List<MatchCandidate>(candidates.Count);
                foreach (var c in candidates.OrderByDescending(x => x.DisplayScore))
                {
                    var candidate = new MatchCandidate
                    {
                        RomFileName = romFileName,
                        RomFullPath = rom,
                        ImageFileName = Path.GetFileName(c.Entry.Path),
                        ImageFullPath = c.Entry.Path,
                        ScorePercent = c.DisplayScore,
                        IsExactMatch = c.IsExactMatch,
                        ContentHash = c.ContentHash,
                    };
                    romResults.Add(candidate);
                    imageUseCount[c.Entry.Path] = imageUseCount.GetValueOrDefault(c.Entry.Path) + 1;
                }
                results.AddRange(romResults);
                romMatched?.Report(new RomMatchResult(romFileName, romResults));
            }
            Console.WriteLine($"[Timing] Matching: {matchingStopwatch.ElapsedMilliseconds}ms, {roms.Count} ROMs scored, {results.Count} candidates found");
            Console.WriteLine($"[Timing] TOTAL: {totalStopwatch.ElapsedMilliseconds}ms");

            return (results, missing, imageUseCount);
        }, cancellationToken);

        // Cross-ROM: an image only "counts" as reused once every ROM that might claim
        // it has been scanned, so this can't run until the whole loop above is done —
        // deliberately kept OUTSIDE the Task.Run background-thread lambda, since by now
        // early-arriving candidates are likely already sitting inside ObservableCollections
        // bound to a live TreeView (see RomMatchResult's doc comment). Mutating IsDuplicate
        // from a background thread would raise PropertyChanged off the UI thread for
        // objects already exposed to Avalonia's binding system; running it here instead,
        // after the await, resumes on the caller's captured SynchronizationContext (the UI
        // thread, since MatchViewModel.StartAsync is the caller) with no manual Dispatcher
        // code needed in this UI-framework-agnostic service.
        foreach (var candidate in results)
            candidate.IsDuplicate = imageUseCount[candidate.ImageFullPath] > 1;

        return new MatchScanResult(results, missing);
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
    /// MatchCandidate.ContentHash), deliberately not a perceptual/similarity hash.
    /// Broad catch deliberately, same reasoning as MatchViewModel.LoadPreviewAsync: the
    /// image was enumerated at the start of the scan, but content-hashing only happens
    /// later, for whichever candidates actually pass the threshold — a real gap in which
    /// an external move/delete/permissions change can race the scan. Rather than crash
    /// the whole scan over one file, fall back to a sentinel that's unique per path (so
    /// it never falsely clusters as "identical" with anything else) and never collides
    /// with a real hash (SHA-256 hex is always exactly 64 lowercase hex chars).</summary>
    private static string ComputeContentHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"unreadable:{path}";
        }
    }

    /// <summary>MiSTer Console Mode convention: box art lives in a "media" subfolder next
    /// to the ROMs, named after the ROM (any extension).</summary>
    private static readonly string[] MisterArtExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>ROMs listing that, in Console Mode, always excludes the "media" subfolder —
    /// otherwise a recursive scan would treat existing art files as if they were ROMs.</summary>
    private static List<string> ListRoms(MatchSettings settings, IProgress<MatchProgress>? progress, CancellationToken cancellationToken)
    {
        // Built BEFORE listing ROMs (not filtered out afterward) so a ROM that already
        // has matching art never gets added to the "N ROMs" count in the first place —
        // the displayed count just climbs straight to its final, already-filtered value
        // instead of counting up past it and then visibly (or silently) dropping back
        // down. One enumeration of the media folder, not up to 3 File.Exists syscalls PER
        // ROM (checking .png/.jpg/.jpeg) like this used to do — for a large library,
        // especially on slower/mounted storage, that was easily the single slowest step
        // in the whole scan. A HashSet lookup per ROM afterward is effectively free by
        // comparison. StringComparer.OrdinalIgnoreCase on both the set and the extension
        // check makes matching uniformly case-insensitive everywhere — the old
        // File.Exists approach was actually case-insensitive on Windows but
        // case-sensitive on Linux (this app targets both), so this is a deliberate small
        // consistency improvement, not a behavior regression.
        // TEMPORARY timing breakdown — see FindMatchesAsync's totalStopwatch comment.
        var mediaScanStopwatch = Stopwatch.StartNew();

        HashSet<string>? existingArtBaseNames = null;
        if (settings.IsConsoleMode && settings.SkipExistingArt)
        {
            var mediaDir = Path.Combine(settings.RomsPath, "media");

            // Left null (not just an empty set) when there's no media folder at all —
            // nothing to skip, so ListFiles below gets no skip predicate rather than one
            // that always returns false.
            if (Directory.Exists(mediaDir))
            {
                existingArtBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var mediaFilesSeen = 0;
                foreach (var file in Directory.EnumerateFiles(mediaDir))
                {
                    if (++mediaFilesSeen % 250 == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    if (MisterArtExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        existingArtBaseNames.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            Console.WriteLine($"[Timing]   existing-art media scan: {mediaScanStopwatch.ElapsedMilliseconds}ms, {existingArtBaseNames?.Count ?? 0} existing-art basenames");
        }

        var romWalkStopwatch = Stopwatch.StartNew();
        var files = ListFiles(settings.RomsPath, settings.RomsExtensions, settings.RomsIncludeSubfolders, progress, "ROMs", cancellationToken,
            existingArtBaseNames is null ? null : rom => existingArtBaseNames.Contains(Path.GetFileNameWithoutExtension(rom) ?? ""));
        Console.WriteLine($"[Timing]   ROMs directory walk: {romWalkStopwatch.ElapsedMilliseconds}ms, {files.Count} files kept");

        var roms = !settings.IsConsoleMode || !settings.RomsIncludeSubfolders
            ? files
            : files.Where(f => !f.StartsWith(
                Path.Combine(settings.RomsPath, "media") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)).ToList();

        // Applied last, so an ignored ROM never resurfaces regardless of which RomsPath
        // it's currently found under.
        if (settings.IgnoredRomPaths.Count > 0)
            roms = roms.Where(r => !settings.IgnoredRomPaths.Contains(r)).ToList();

        if (settings.IgnoredRomFolders.Count > 0)
            roms = roms.Where(r => !settings.IgnoredRomFolders.Any(folder => FolderAncestry.IsUnderFolder(r, folder))).ToList();

        // Sorted once, here, so the Matching loop below scores ROMs in the same order
        // the UI wants to display them in — MatchViewModel used to re-sort the whole
        // batch result downstream after the fact; now that results stream in live as
        // each ROM is scored, they need to already arrive in display order so the UI can
        // just append instead of needing a sorted-insert. By filename (not full path),
        // matching the old downstream GroupBy/OrderBy key, so same-named ROMs in
        // different subfolders still sort adjacently.
        return roms.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reports a running found/examined count as it goes (see MatchPhase.
    /// Listing) rather than only at the end — Directory.EnumerateFiles streams results
    /// lazily (unlike Directory.GetFiles, which blocks until it has the whole list), so
    /// a plain foreach over it already gives incremental results for free, with no need
    /// for a hand-rolled recursive walker. shouldSkip (see ListRoms' existing-art check)
    /// excludes a file from the result — and from the reported count — during this same
    /// walk, rather than the caller filtering the returned list afterward; a skipped file
    /// still counts toward "examined" (see the throttling comment below), just not
    /// toward the "N ROMs" figure shown to the user.</summary>
    private static List<string> ListFiles(
        string folder, string extensionSpec, bool includeSubfolders,
        IProgress<MatchProgress>? progress, string listingLabel, CancellationToken cancellationToken,
        Func<string, bool>? shouldSkip = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var extensions = extensionSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Null (not an empty HashSet) signals "match everything" — same one-walk
        // structure below handles both cases identically rather than duplicating the
        // foreach for the "*"/no-filter case.
        HashSet<string>? allowedExtensions = extensions.Length == 0 || extensions.Contains("*")
            ? null
            // One walk of the tree regardless of how many extensions are configured —
            // this used to call Directory.EnumerateFiles once PER extension, each one a
            // full, separate recursive re-walk of the entire tree from scratch. For a
            // large multi-system library (especially on slower/network storage), listing
            // 3-4 extensions meant re-scanning everything 3-4 times over for no reason.
            // A HashSet lookup per file, in one single pass, is effectively free by
            // comparison.
            : new HashSet<string>(extensions.Select(ext => "." + ext.TrimStart('.')), StringComparer.OrdinalIgnoreCase);

        var result = new List<string>();
        var examined = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();
            examined++;

            if ((allowedExtensions is null || allowedExtensions.Contains(Path.GetExtension(file)))
                && (shouldSkip is null || !shouldSkip(file)))
                result.Add(file);

            // Throttled to every 250 files EXAMINED, not every 250 matched — a long
            // stretch of non-matching files (other extensions, save states, whatever
            // else lives under this root) would otherwise report nothing at all for
            // however long that stretch takes, which reads as a freeze even though the
            // walk is still moving. Reporting on examined count keeps this visibly
            // ticking regardless of how sparse the matches are.
            if (examined % 250 == 0)
                progress?.Report(new MatchProgress(MatchPhase.Listing, result.Count, examined, listingLabel));
        }

        // Final report so the displayed count reflects the true total even when the
        // walk ends between throttled intervals (e.g. exactly 16,000 examined with no
        // remainder would otherwise show a stale count from 250 files back).
        progress?.Report(new MatchProgress(MatchPhase.Listing, result.Count, examined, listingLabel));

        return result;
    }
}
