using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;
using GameArtMatch.Services;

namespace GameArtMatch.ViewModels;

/// <summary>Backs the "Report" tab — the old, separate "List Missing" window's two sub-tabs.</summary>
public partial class ReportViewModel : ViewModelBase
{
    private readonly MatchSettings _settings;
    private readonly IMatchingService _matchingService;

    public ObservableCollection<ReportEntry> Missing { get; } = [];
    public ObservableCollection<ReportEntry> Matched { get; } = [];

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

    [RelayCommand]
    private async Task ListMissingAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Missing.Clear();
        try
        {
            var entries = await _matchingService.FindMissingAsync(_settings, cancellationToken);
            foreach (var entry in entries.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
                Missing.Add(entry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ListMatchedAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Matched.Clear();
        try
        {
            var entries = await _matchingService.FindMatchedAsync(_settings, cancellationToken);
            foreach (var entry in entries.OrderBy(e => e.RomFileName, StringComparer.OrdinalIgnoreCase))
                Matched.Add(entry);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
