using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;
using GameArtMatch.Services;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the Report window (see ReportWindow) — Missing/Matched/Ignored, all
/// populated together by one command rather than three separate manual triggers.</summary>
public partial class ReportViewModel : ViewModelBase
{
    private readonly MatchSettings _settings;
    private readonly IMatchingService _matchingService;

    public ObservableCollection<ReportEntry> Missing { get; } = [];
    public ObservableCollection<ReportEntry> Matched { get; } = [];

    /// <summary>Full paths from MatchSettings.IgnoredRomPaths that still exist on disk —
    /// the ignore list is global across every system/folder ever scanned, not scoped to
    /// the currently-configured RomsPath, so this intentionally isn't restricted to it.</summary>
    public ObservableCollection<string> Ignored { get; } = [];

    /// <summary>Drives the Ignored tab's visibility — only shown "if any ignored files
    /// exist" (per the request), not unconditionally alongside Missing/Matched.</summary>
    public bool HasIgnoredEntries => Ignored.Count > 0;

    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ReportViewModel(MatchSettings settings, IMatchingService matchingService)
    {
        _settings = settings;
        _matchingService = matchingService;
    }

    /// <summary>Design-time only (XAML previewer's Design.DataContext).</summary>
    public ReportViewModel() : this(new MatchSettings(), new MatchingService())
    {
    }

    /// <summary>Populates all three tabs in one pass — run automatically as soon as the
    /// Report window opens (see ReportWindow.axaml.cs), and re-runnable via the window's
    /// own Refresh button afterward, e.g. after ignoring more ROMs from the Match tab
    /// while the Report window stays open.</summary>
    [RelayCommand]
    private async Task GenerateAllAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Missing.Clear();
        Matched.Clear();
        Ignored.Clear();
        try
        {
            var missing = await _matchingService.FindMissingAsync(_settings, cancellationToken);
            foreach (var entry in missing.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
                Missing.Add(entry);

            var matched = await _matchingService.FindMatchedAsync(_settings, cancellationToken);
            foreach (var entry in matched.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
                Matched.Add(entry);

            foreach (var path in _settings.IgnoredRomPaths
                         .Where(File.Exists)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                Ignored.Add(path);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasIgnoredEntries));
        }
    }

    /// <summary>Plain-text rendering of everything currently loaded, for the Export
    /// button (see ReportView.axaml.cs) — assembled from whatever GenerateAllAsync last
    /// populated, not re-queried, so Export always reflects exactly what's on screen.</summary>
    public string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GameArtMatch Report — generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        sb.AppendLine();
        sb.AppendLine($"=== Missing ({Missing.Count}) ===");
        foreach (var entry in Missing)
            sb.AppendLine(entry.RomFileName);

        sb.AppendLine();
        sb.AppendLine($"=== Matched ({Matched.Count}) ===");
        foreach (var entry in Matched)
            sb.AppendLine($"{entry.RomFileName} -> {entry.MatchedImageFileName}");

        if (Ignored.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"=== Ignored ({Ignored.Count}) ===");
            foreach (var path in Ignored)
                sb.AppendLine(path);
        }

        return sb.ToString();
    }
}
