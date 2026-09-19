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

    private static string? GetArgValue(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
