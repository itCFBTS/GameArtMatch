using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>
/// One distinct image in the Match tab's preview carousel (see PreviewCarouselViewModel).
/// Keyed by MatchCandidate.ContentHash rather than by candidate: a ROM whose visible
/// candidate list holds three byte-identical files under different names gets ONE slide
/// for them, not three — so walking down the tree through a "Same as above" run never
/// flips the carousel to a visually identical image. Representative is the first VISIBLE
/// member of that cluster (the best-scoring one, since RomMatchGroup clusters in
/// best-match order), and is what the carousel hands back to the tree when the user
/// navigates here from the carousel side instead of the tree side.
///
/// Left/Top/Width/Height/Opacity/ZIndex are the carousel's computed layout targets for this
/// slide's ContentPresenter (see MatchView.axaml's ContentPresenter style) — the
/// ViewModel recomputes them whenever the selected index or viewport changes, and the
/// View's Transitions animate to the new values (avalonia-patterns skill: "compute the
/// targets in the ViewModel, let Transitions handle the motion").
/// </summary>
public partial class PreviewSlide : ObservableObject, IDisposable
{
    public string ContentHash { get; }

    /// <summary>First visible candidate carrying this ContentHash — the row the tree
    /// labels as the cluster's leader (never "Same as above").</summary>
    public MatchCandidate Representative { get; private set; }

    /// <summary>Every VISIBLE candidate sharing this ContentHash, leader first. With
    /// HideSameImages on this is always exactly one — the tree and the carousel agree by
    /// construction, since both derive from the same RomMatchGroup.VisibleCandidates.</summary>
    public IReadOnlyList<MatchCandidate> Members { get; private set; }

    /// <summary>Decoded only while this slide sits within the carousel's load window
    /// (see PreviewCarouselViewModel.LoadWindow); null otherwise, so a ROM with dozens of
    /// distinct candidates never holds dozens of bitmaps in memory at once.</summary>
    [ObservableProperty] public partial Bitmap? Image { get; private set; }

    [ObservableProperty] public partial string? Error { get; private set; }

    [ObservableProperty] public partial double Left { get; set; }
    [ObservableProperty] public partial double Top { get; set; }
    [ObservableProperty] public partial double Width { get; set; }
    [ObservableProperty] public partial double Height { get; set; }

    /// <summary>Height / width of the decoded image, or 1 (a square placeholder) until
    /// it's loaded — the carousel sizes each slide's box to the image's real proportions
    /// so a wide banner's box is short and its neighbours can sit right under the
    /// visible pixels, rather than under an invisible square slot.</summary>
    public double AspectRatio =>
        Image is { PixelSize: { Width: > 0 } px } ? (double)px.Height / px.Width : 1;
    [ObservableProperty] public partial double Opacity { get; set; }
    [ObservableProperty] public partial int ZIndex { get; set; }

    /// <summary>Bumped on every Unload so a decode still in flight from before can tell
    /// its result is no longer wanted and dispose it instead of publishing it — same
    /// request-id idea the old single-image preview used, just per slide.</summary>
    private int _loadVersion;
    private bool _loading;

    public PreviewSlide(string contentHash, IReadOnlyList<MatchCandidate> members)
    {
        ContentHash = contentHash;
        Members = members;
        Representative = members[0];
    }

    /// <summary>Refreshes membership after a filter change without touching the decoded
    /// bitmap — the image bytes are the same regardless of which filenames currently
    /// survive the filters, so toggling Hide Same Images never causes a reload.</summary>
    public void SetMembers(IReadOnlyList<MatchCandidate> members)
    {
        Members = members;
        Representative = members[0];
    }

    public void EnsureLoaded()
    {
        if (Image is not null || _loading)
            return;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var version = _loadVersion;
        _loading = true;
        Error = null;

        try
        {
            var path = Representative.ImageFullPath;
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            });

            if (version != _loadVersion)
            {
                bitmap.Dispose(); // unloaded (or disposed) while decoding — nobody wants this anymore
                return;
            }

            Image = bitmap;
        }
        catch (Exception ex)
        {
            // Broad catch deliberately: a missing file, an unreadable/corrupt image, or a
            // codec failure should all just show a friendly message on this one slide,
            // never crash or silently leave it blank with no explanation.
            if (version == _loadVersion)
                Error = $"Couldn't load image: {ex.Message}";
        }
        finally
        {
            if (version == _loadVersion)
                _loading = false;
        }
    }

    public void Unload()
    {
        _loadVersion++;
        _loading = false;
        Image?.Dispose();
        Image = null;
        Error = null;
    }

    public void Dispose() => Unload();
}
