using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameArtMatch.Services;

namespace GameArtMatch;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--extract-tags"))
        {
            RunExtractTags(args);
            return;
        }

        if (args.Contains("--scan"))
        {
            RunScan(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            // Bundled font (see Assets/Fonts, Tokens.axaml's FontFamilyBase) as the app's
            // implicit default, same role Avalonia.Fonts.Inter's WithInterFont() played before.
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://GameArtMatch/Assets/Fonts#Share Tech Mono"
            })
            .LogToTrace();

    /// <summary>Dev CLI mode backing docs/catalog (see its README): scans the entire
    /// ROMs/Images library (every system, via the persisted roots — not just whichever
    /// single system folder was last selected in the GUI) and writes out the raw,
    /// deduplicated "(...)"/"[...]" tag vocabulary in use, as empirical groundwork for
    /// tag normalization/categorization decisions (docs/adr/0004, 0005). Defaults come
    /// from the same settings.json the GUI reads (see SettingsStore), so this can be
    /// re-run standalone without re-entering paths; --roms-root/--images-root/--out
    /// override them when needed (e.g. pointing at a different art pack).</summary>
    private static void RunExtractTags(string[] args)
    {
        var persisted = new SettingsStore().Load();

        var romsRoot = GetArgValue(args, "--roms-root") ?? persisted.RomsRootPath ?? "";
        var imagesRoot = GetArgValue(args, "--images-root") ?? persisted.ImagesRootPath ?? "";
        var outDir = GetArgValue(args, "--out") ?? Path.Combine("docs", "catalog");

        if (string.IsNullOrWhiteSpace(romsRoot) || !Directory.Exists(romsRoot))
            Console.WriteLine($"Warning: ROMs root not found ({romsRoot}) - scanning images only.");
        if (string.IsNullOrWhiteSpace(imagesRoot) || !Directory.Exists(imagesRoot))
            Console.WriteLine($"Warning: Images root not found ({imagesRoot}) - scanning ROMs only.");

        Console.WriteLine($"Scanning ROMs root: {romsRoot}");
        var romFiles = TagExtractor.EnumerateAllFiles(romsRoot);
        Console.WriteLine($"  {romFiles.Count} files found");

        Console.WriteLine($"Scanning images root: {imagesRoot}");
        var imageFiles = TagExtractor.EnumerateAllFiles(imagesRoot);
        Console.WriteLine($"  {imageFiles.Count} files found");

        var tags = TagExtractor.ExtractTags(romFiles, imageFiles);
        Console.WriteLine($"{tags.Count} unique tag strings found");

        Directory.CreateDirectory(outDir);

        var sortedTags = tags.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList();
        File.WriteAllLines(Path.Combine(outDir, "raw-tags.txt"), sortedTags);

        var categorized = tags.ToDictionary(kv => kv.Key, kv => TagCategorizer.Categorize(kv.Key));

        var countsLines = new List<string> { "tag\tcategory\ttotal\troms\timages" };
        countsLines.AddRange(tags
            .OrderByDescending(kv => kv.Value.TotalCount)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}\t{categorized[kv.Key]}\t{kv.Value.TotalCount}\t{kv.Value.RomCount}\t{kv.Value.ImageCount}"));
        File.WriteAllLines(Path.Combine(outDir, "raw-tags-counts.tsv"), countsLines);

        // One TSV per category, split out of raw-tags-counts.tsv, so each bucket can
        // be skimmed on its own instead of grepping the combined file. The whole
        // directory is rebuilt from scratch each run so a category renamed/removed
        // from TagCategorizer can't leave a stale file behind.
        var categoriesDir = Path.Combine(outDir, "categories");
        if (Directory.Exists(categoriesDir))
            Directory.Delete(categoriesDir, recursive: true);
        Directory.CreateDirectory(categoriesDir);

        var header = "tag\ttotal\troms\timages";
        foreach (var group in categorized.GroupBy(kv => kv.Value))
        {
            var lines = new List<string> { header };
            lines.AddRange(group
                .OrderByDescending(kv => tags[kv.Key].TotalCount)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}\t{tags[kv.Key].TotalCount}\t{tags[kv.Key].RomCount}\t{tags[kv.Key].ImageCount}"));
            File.WriteAllLines(Path.Combine(categoriesDir, $"{group.Key}.tsv"), lines);
        }

        Console.WriteLine("By category (unique tags / total occurrences):");
        foreach (var group in categorized
            .GroupBy(kv => kv.Value)
            .Select(g => new
            {
                Category = g.Key,
                UniqueCount = g.Count(),
                TotalOccurrences = g.Sum(kv => tags[kv.Key].TotalCount),
            })
            .OrderByDescending(g => g.TotalOccurrences))
        {
            Console.WriteLine($"  {group.Category,-18} {group.UniqueCount,5} unique / {group.TotalOccurrences,6} occurrences");
        }

        File.WriteAllLines(Path.Combine(outDir, "scan-info.txt"),
        [
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} (--extract-tags)",
            $"ROMs root: {romsRoot} ({romFiles.Count} files)",
            $"Images root: {imagesRoot} ({imageFiles.Count} files)",
            $"Unique tag strings: {tags.Count}",
        ]);

        Console.WriteLine($"Wrote {outDir}/raw-tags.txt, raw-tags-counts.tsv, categories/*.tsv, scan-info.txt");
    }

    /// <summary>Dev CLI mode for before/after validation of matcher changes (the diff
    /// discipline ADR-0003 established and ADR-0006/0007 call for): runs the exact same
    /// FindMatchesAsync the GUI runs — same ListRoms filtering (Console Mode existing-art
    /// skip, ignore list, ignored folders), same compiled MatchSettings defaults for the
    /// matching options — and writes one TSV row per candidate, in display order, plus a
    /// row per ROM with no candidates. ROMs/Images default to the GUI's last-used folders
    /// (LastRomsPath/LastImagesPath in settings.json); --roms/--images/--threshold/--out
    /// override. ADR-0003's run used a temporary version of this that was reverted; it's
    /// kept this time because every scoring ADR needs it.</summary>
    private static void RunScan(string[] args)
    {
        var persisted = new SettingsStore().Load();

        var settings = new Models.MatchSettings
        {
            RomsRootPath = persisted.RomsRootPath ?? "",
            ImagesRootPath = persisted.ImagesRootPath ?? "",
            IsConsoleMode = persisted.IsConsoleMode, // also sets SkipExistingArt, as in the GUI
            RomsPath = GetArgValue(args, "--roms") ?? persisted.LastRomsPath ?? "",
            ImagesPath = GetArgValue(args, "--images") ?? persisted.LastImagesPath ?? "",
        };
        if (persisted.RomsIncludeSubfolders.HasValue) settings.RomsIncludeSubfolders = persisted.RomsIncludeSubfolders.Value;
        if (persisted.ImagesIncludeSubfolders.HasValue) settings.ImagesIncludeSubfolders = persisted.ImagesIncludeSubfolders.Value;
        foreach (var path in persisted.IgnoredRomPaths ?? []) settings.IgnoredRomPaths.Add(path);
        foreach (var folder in persisted.IgnoredRomFolders ?? []) settings.IgnoredRomFolders.Add(folder);
        if (GetArgValue(args, "--threshold") is { } t && int.TryParse(t, out var threshold))
            settings.AccuracyThreshold = threshold;

        var outPath = GetArgValue(args, "--out") ?? "scan.tsv";

        Console.WriteLine($"ROMs:   {settings.RomsPath}");
        Console.WriteLine($"Images: {settings.ImagesPath}");
        Console.WriteLine($"Console Mode: {settings.IsConsoleMode}, Skip existing art: {settings.SkipExistingArt}, Threshold: {settings.AccuracyThreshold}");

        var result = new MatchingService().FindMatchesAsync(settings, null, null, System.Threading.CancellationToken.None)
            .GetAwaiter().GetResult();

        var lines = new List<string> { "rom	image	score	exact" };
        foreach (var group in result.Candidates.GroupBy(c => c.RomFileName))
            foreach (var c in group)
                lines.Add($"{c.RomFileName}	{c.ImageFileName}	{c.ScorePercent:F1}	{(c.IsExactMatch ? 1 : 0)}");
        foreach (var missing in result.Missing)
            lines.Add($"{missing.RomFileName}			");

        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"Wrote {outPath}: {result.Candidates.Count} candidates, {result.Missing.Count} ROMs with none");
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
