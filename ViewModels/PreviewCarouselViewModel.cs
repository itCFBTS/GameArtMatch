using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>
/// The Match tab's right-hand art pane: a vertical coverflow-style carousel of every
/// DISTINCT candidate image for one ROM (see PreviewSlide for what "distinct" means).
/// The current slide sits large in the middle; its neighbours peek above and below at
/// reduced size and opacity; everything further out is parked just off-screen so it
/// slides into view (rather than popping) when it becomes a neighbour.
///
/// Kept in sync with the results tree by MatchViewModel in both directions:
///  - tree -> carousel: MatchViewModel.SyncCarousel calls Show(...) with the selected
///    ROM's visible candidates and the highlighted candidate's ContentHash. Nothing here
///    ever fires RepresentativeChosen for that path, which is what stops the two from
///    ping-ponging.
///  - carousel -> tree: Next/Previous/SelectSlide (the only user-driven entry points)
///    raise RepresentativeChosen, and MatchViewModel highlights that candidate in the
///    tree. That comes straight back in as a Show(...) call, which resolves to the
///    index already selected — a no-op.
///
/// Layout is computed here in viewport pixels (the View reports its size via
/// SetViewport) so the View is nothing but bindings plus Transitions — see the
/// avalonia-patterns skill's ItemsControl+Canvas and Transitions notes for why the
/// targets live on the ContentPresenter and why they're computed rather than animated
/// by hand.
/// </summary>
public partial class PreviewCarouselViewModel : ObservableObject
{
    /// <summary>Slides within this many positions of the current one keep a decoded
    /// bitmap. 2 rather than 1 so the slide parked just off-screen already has its image
    /// when it animates in as the next neighbour, instead of arriving blank.</summary>
    private const int LoadWindow = 2;

    private const double NeighbourScale = 0.55;
    private const double NeighbourOpacity = 0.55;

    /// <summary>Between the current image's visible edge and a neighbour's — measured
    /// against the images' real (aspect-correct) boxes, not their square slots.</summary>
    private const double Gap = 4;

    /// <summary>Height fraction the current slide's box may take before width becomes
    /// the limit — leaves room for both neighbours to actually show.</summary>
    private const double CenterHeightFraction = 0.5;

    public ObservableCollection<PreviewSlide> Slides { get; } = [];

    /// <summary>-1 while there are no slides.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSlide))]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyPropertyChangedFor(nameof(ScrollPosition))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    public partial int SelectedIndex { get; private set; } = -1;

    // --- Scrollbar adapter: the pane's vertical ScrollBar (see MatchView.axaml) is a
    // plain standalone control, not a ScrollViewer's — the carousel doesn't scroll, it
    // steps. So the bar's range is "one notch per distinct image": Maximum is the last
    // index, ViewportSize is 1 (the thumb spans one slide's share of the track), and a
    // drag/track-click lands on the nearest whole slide. ---

    /// <summary>Last slide index, or 0 while there's nothing to scroll through.</summary>
    public double ScrollMaximum => Math.Max(0, Slides.Count - 1);

    /// <summary>Only worth showing a bar once there's a second image to reach.</summary>
    public bool CanScroll => Slides.Count > 1;

    /// <summary>Two-way with the ScrollBar's Value. Reads as the current index; a write
    /// (thumb drag, track click, arrow button) is user navigation, so it goes through
    /// Choose like Next/Previous do and the tree's highlight follows it.</summary>
    public double ScrollPosition
    {
        get => Math.Max(0, SelectedIndex);
        set
        {
            var index = (int)Math.Round(value);
            if (index != SelectedIndex)
                Choose(index);
        }
    }

    public PreviewSlide? SelectedSlide =>
        SelectedIndex >= 0 && SelectedIndex < Slides.Count ? Slides[SelectedIndex] : null;

    /// <summary>"3 / 7" — position among the ROM's distinct images.</summary>
    public string PositionText => Slides.Count == 0 ? "" : $"{SelectedIndex + 1} / {Slides.Count}";

    /// <summary>Raised only for user-driven navigation (arrows, wheel, clicking a
    /// neighbour) — never for a Show(...) resync from the tree. Carries the chosen
    /// slide's leader candidate so the tree can highlight the matching row.</summary>
    public event Action<MatchCandidate>? RepresentativeChosen;

    // Last real (non-zero) viewport size. Zero sizes are ignored rather than stored: the
    // pane's grid column collapses to 0 width whenever it's hidden (see
    // MatchView.SetPreviewPaneVisible), and layout computed against that would just be
    // garbage the next reopen animates away from.
    private double _viewportWidth;
    private double _viewportHeight;

    private bool HasViewport => _viewportWidth > 0 && _viewportHeight > 0;

    /// <summary>A Show(...) that arrived before the View had ever reported a size, held
    /// until SetViewport can run it. Building the slides any earlier would create their
    /// containers at (0,0), and the first real layout would then be a property CHANGE
    /// that the View's Transitions animate — the "slides in from the top-left corner"
    /// effect on first open. Built only once positions can be computed, the containers
    /// are created already in place and nothing moves.</summary>
    private (IReadOnlyList<MatchCandidate> Candidates, string? FocusHash)? _pending;

    /// <summary>Rebuilds the slide set from one ROM's VISIBLE candidates — one slide per
    /// distinct ContentHash, in first-appearance order (which RomMatchGroup already
    /// clusters best-match-first) — and lands on the slide holding focusHash.
    ///
    /// Slides whose hash survives are reused (same object, same decoded bitmap, same
    /// ContentPresenter if the sequence is unchanged), so a Hide Same Images toggle —
    /// which by definition removes only NON-leader cluster members and therefore leaves
    /// the distinct-hash sequence identical — changes nothing visible here at all.
    ///
    /// focusHash null (a ROM header row is highlighted, not a candidate) keeps the current
    /// slide when the set is unchanged, and starts at the best match otherwise.</summary>
    public void Show(IReadOnlyList<MatchCandidate> visibleCandidates, string? focusHash)
    {
        if (!HasViewport)
        {
            _pending = (visibleCandidates, focusHash);
            return;
        }
        _pending = null;

        var clusters = visibleCandidates
            .GroupBy(c => c.ContentHash)
            .Select(g => (Hash: g.Key, Members: (IReadOnlyList<MatchCandidate>)g.ToList()))
            .ToList();

        var existing = Slides.ToDictionary(s => s.ContentHash);
        var next = new List<PreviewSlide>(clusters.Count);
        foreach (var (hash, members) in clusters)
        {
            if (existing.Remove(hash, out var kept))
            {
                kept.SetMembers(members);
                next.Add(kept);
            }
            else
            {
                var created = new PreviewSlide(hash, members);
                created.PropertyChanged += OnSlidePropertyChanged;
                next.Add(created);
            }
        }

        foreach (var dropped in existing.Values)
        {
            dropped.PropertyChanged -= OnSlidePropertyChanged;
            dropped.Dispose();
        }

        var sequenceChanged = next.Count != Slides.Count || next.Where((s, i) => !ReferenceEquals(s, Slides[i])).Any();
        if (sequenceChanged)
        {
            Slides.Clear();
            foreach (var slide in next)
                Slides.Add(slide);
            NotifyScrollRangeChanged();
        }

        int index;
        if (focusHash is not null && next.FindIndex(s => s.ContentHash == focusHash) is var found and >= 0)
            index = found;
        else if (!sequenceChanged && SelectedIndex >= 0 && SelectedIndex < next.Count)
            index = SelectedIndex;
        else
            index = next.Count > 0 ? 0 : -1;

        var indexChanged = index != SelectedIndex;
        SelectedIndex = index; // OnSelectedIndexChanged applies the selection only if it actually changed...
        if (!indexChanged)
        {
            // ...so an unchanged index over a changed slide set (e.g. best match -> best
            // match across two ROMs) has to apply it explicitly.
            OnPropertyChanged(nameof(SelectedSlide));
            OnPropertyChanged(nameof(PositionText));
            ApplySelection();
        }

        // Freshly-built containers have no outgoing slide to cross over, so there's
        // nothing to delay for (see ApplyZIndices) — stack them correctly right away.
        if (sequenceChanged)
            ApplyZIndices();
    }

    public void Clear()
    {
        _pending = null;
        foreach (var slide in Slides)
        {
            slide.PropertyChanged -= OnSlidePropertyChanged;
            slide.Dispose();
        }
        Slides.Clear();
        SelectedIndex = -1;
        NotifyScrollRangeChanged();
    }

    private void NotifyScrollRangeChanged()
    {
        OnPropertyChanged(nameof(ScrollMaximum));
        OnPropertyChanged(nameof(CanScroll));
    }

    /// <summary>A slide's box only takes its real proportions once the bitmap has been
    /// decoded (see PreviewSlide.AspectRatio), so an image finishing its load is a layout
    /// change — the box settles from the square placeholder to the image's shape and the
    /// neighbours close up (or spread out) to match.</summary>
    private void OnSlidePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewSlide.Image))
            Relayout();
    }

    /// <summary>Called by the View whenever the carousel's host panel changes size.</summary>
    public void SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;
        if (Math.Abs(width - _viewportWidth) < 0.5 && Math.Abs(height - _viewportHeight) < 0.5)
            return;

        _viewportWidth = width;
        _viewportHeight = height;

        if (_pending is { } pending)
            Show(pending.Candidates, pending.FocusHash); // first real size — build the deferred slide set in place
        else
            Relayout();
    }

    private bool CanGoPrevious() => SelectedIndex > 0;
    private bool CanGoNext() => SelectedIndex >= 0 && SelectedIndex < Slides.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Choose(SelectedIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Choose(SelectedIndex + 1);

    /// <summary>A click on a slide (typically a peeking neighbour) — brings it to the middle.</summary>
    public void SelectSlide(PreviewSlide slide) => Choose(Slides.IndexOf(slide));

    private void Choose(int index)
    {
        if (index < 0 || index >= Slides.Count)
            return;

        SelectedIndex = index;
        RepresentativeChosen?.Invoke(Slides[index].Representative);
    }

    partial void OnSelectedIndexChanged(int value) => ApplySelection();

    private void ApplySelection()
    {
        Relayout();
        UpdateLoadedWindow();
    }

    /// <summary>Stacks the current slide on top, neighbours beneath it, and so on outward.
    /// Deliberately NOT called from OnSelectedIndexChanged: the outgoing and incoming
    /// slides swap sizes over the transition, and swapping z-order the instant the move
    /// starts makes the still-small incoming slide pop in front of the still-large
    /// outgoing one. MatchView.axaml.cs calls this half a transition-duration after each
    /// SelectedIndex change instead — the point at which two symmetric-eased transitions
    /// between the same endpoints are exactly equal (avalonia-patterns skill, "crossover
    /// timing").</summary>
    public void ApplyZIndices()
    {
        for (var i = 0; i < Slides.Count; i++)
            Slides[i].ZIndex = 100 - Math.Abs(i - SelectedIndex);
    }

    private void Relayout()
    {
        if (!HasViewport || Slides.Count == 0)
            return;

        var w = _viewportWidth;
        var h = _viewportHeight;

        // Slot sizes: the largest square each role's image may occupy. The current
        // image gets as much as the pane allows while still leaving vertical room for
        // the neighbours; each neighbour is a fixed fraction of that.
        var centerSlot = Math.Max(0, Math.Min(w, h * CenterHeightFraction));

        // Each image's real box is its slot shrunk to the image's own proportions
        // (Stretch="Uniform" in the View renders exactly this rectangle), so positions
        // below are relative to visible pixels, not to empty slot corners.
        var current = FitIn(Slides[SelectedIndex].AspectRatio, centerSlot);
        var currentTop = h / 2 - current.Height / 2;
        var currentBottom = h / 2 + current.Height / 2;

        // Neighbours may never be taller than the room actually left above/below the
        // current image, so they're not half-clipped on a short pane — and a wide
        // current image leaves more room, which they're allowed to use.
        var roomPerSide = Math.Max(0, (h - current.Height) / 2 - Gap);
        var neighbourSlot = Math.Min(centerSlot * NeighbourScale, roomPerSide);

        for (var i = 0; i < Slides.Count; i++)
        {
            var slide = Slides[i];
            var distance = i - SelectedIndex;
            var direction = Math.Sign(distance);

            double width, height, top, opacity;
            switch (Math.Abs(distance))
            {
                case 0:
                    (width, height) = current;
                    top = currentTop;
                    opacity = 1;
                    break;
                case 1:
                    (width, height) = FitIn(slide.AspectRatio, neighbourSlot);
                    // Tucked right up against the current image's visible edge.
                    top = direction < 0 ? currentTop - Gap - height : currentBottom + Gap;
                    opacity = NeighbourOpacity;
                    break;
                default:
                    // Parked entirely beyond the top/bottom edge (the host panel clips),
                    // fully transparent, already at neighbour size so becoming a
                    // neighbour is a pure slide-and-fade rather than a resize as well.
                    (width, height) = FitIn(slide.AspectRatio, neighbourSlot);
                    top = direction < 0 ? -height : h;
                    opacity = 0;
                    break;
            }

            slide.Width = width;
            slide.Height = height;
            slide.Left = (w - width) / 2;
            slide.Top = top;
            slide.Opacity = opacity;
        }
    }

    /// <summary>The largest box of the given height/width ratio that fits in a square
    /// slot — what Stretch="Uniform" will actually paint.</summary>
    private static (double Width, double Height) FitIn(double aspectRatio, double slot) =>
        aspectRatio <= 1 ? (slot, slot * aspectRatio) : (slot / aspectRatio, slot);

    private void UpdateLoadedWindow()
    {
        for (var i = 0; i < Slides.Count; i++)
        {
            if (Math.Abs(i - SelectedIndex) <= LoadWindow)
                Slides[i].EnsureLoaded();
            else
                Slides[i].Unload();
        }
    }
}
