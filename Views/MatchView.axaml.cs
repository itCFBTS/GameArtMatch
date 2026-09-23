using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameArtMatch.Models;
using GameArtMatch.Services;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class MatchView : UserControl
{
    private static readonly string[] ActivityDotsFrames = [".", "..", "..."];
    private readonly DispatcherTimer _activityDotsTimer;
    private int _activityDotsIndex;

    /// <summary>Half of the carousel's DoubleTransition Duration (0.35s, see MatchView.axaml's
    /// ContentPresenter style) — two slides swapping sizes with the same symmetric easing
    /// are exactly equal at the temporal midpoint, so that's when the z-order swap is
    /// invisible (avalonia-patterns skill, "crossover timing"). Owned here, not in the
    /// ViewModel, so it stops with the view (skill: timer ownership).</summary>
    private static readonly TimeSpan ZIndexSwapDelay = TimeSpan.FromMilliseconds(175);
    private readonly DispatcherTimer _zIndexTimer = new() { Interval = ZIndexSwapDelay };
    private PreviewCarouselViewModel? _subscribedCarousel;

    public MatchView()
    {
        InitializeComponent();

        // Tunnel (not bubble) so this runs before any native TreeView/TreeViewItem
        // Space-key handling gets a chance to consume the event first — that native
        // handling is what was swallowing Space when a row was reached via arrow keys
        // rather than a click (see OnResultsTreeKeyDown for the rest of the story).
        ResultsTree.AddHandler(InputElement.KeyDownEvent, OnResultsTreeKeyDown, RoutingStrategies.Tunnel);

        // Avalonia's Style.Animations has no built-in animator for string properties
        // (confirmed empirically: a Text-targeting KeyFrame throws "No animator
        // registered for the property Text" the moment the style tries to attach), so
        // the "." -> ".." -> "..." activity cue is driven by a plain timer instead,
        // started/stopped as MatchViewModel.IsIndexing changes.
        _activityDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _activityDotsTimer.Tick += (_, _) =>
        {
            _activityDotsIndex = (_activityDotsIndex + 1) % ActivityDotsFrames.Length;
            ActivityDotsText.Text = ActivityDotsFrames[_activityDotsIndex];
        };

        _zIndexTimer.Tick += (_, _) =>
        {
            _zIndexTimer.Stop();
            _subscribedCarousel?.ApplyZIndices();
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MatchViewModel vm)
            {
                vm.PropertyChanged += OnMatchViewModelPropertyChanged;
                SetPreviewPaneVisible(vm.IsPreviewVisible);
                SubscribeCarousel(vm.Carousel);
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _zIndexTimer.Stop();
            _activityDotsTimer.Stop();
        };
    }

    private void SubscribeCarousel(PreviewCarouselViewModel? carousel)
    {
        if (_subscribedCarousel is not null)
            _subscribedCarousel.PropertyChanged -= OnCarouselPropertyChanged;

        _subscribedCarousel = carousel;

        if (_subscribedCarousel is not null)
            _subscribedCarousel.PropertyChanged += OnCarouselPropertyChanged;
    }

    private void OnCarouselPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreviewCarouselViewModel.SelectedIndex))
            return;

        // Restart (not just start) so rapid successive steps each get a full half-
        // transition before their swap, instead of an early tick from the previous step.
        _zIndexTimer.Stop();
        _zIndexTimer.Start();
    }

    // A container's first position must land WITHOUT animating — otherwise every newly
    // shown image zooms in from the pane's corner (its Width/Height/Canvas.Left/Top all
    // start at their defaults). So the Transitions aren't in the container's base style;
    // they're in a ".animated" class (see MatchView.axaml's ItemsControl.Styles) that's
    // added here, posted at Background priority so it runs only after the layout and
    // render passes that place the container have already happened. Adding a class
    // changes no property values, so nothing animates at that moment — only from the
    // next real move onward. Deferring the slide build until the viewport is known (see
    // PreviewCarouselViewModel._pending) turned out not to be enough on its own.
    private static void OnCarouselContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var container = e.Container;
        Dispatcher.UIThread.Post(() => container.Classes.Add("animated"), DispatcherPriority.Background);
    }

    private void OnCarouselViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MatchViewModel vm)
            vm.Carousel.SetViewport(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnCarouselPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MatchViewModel vm)
            return;

        // Wheel down = next (further down the tree), wheel up = previous — matches the
        // vertical layout, where the next image is the one peeking in from below.
        if (e.Delta.Y < 0 && vm.Carousel.NextCommand.CanExecute(null))
            vm.Carousel.NextCommand.Execute(null);
        else if (e.Delta.Y > 0 && vm.Carousel.PreviousCommand.CanExecute(null))
            vm.Carousel.PreviousCommand.Execute(null);
        e.Handled = true;
    }

    // Reads sender's DataContext rather than routing a command — same reasoning as the
    // context-menu handlers below: a Border has no Command, and the slide is right there.
    private void OnSlidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: PreviewSlide slide } && DataContext is MatchViewModel vm)
        {
            vm.Carousel.SelectSlide(slide);
            e.Handled = true;
        }
    }

    private void OnMatchViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MatchViewModel vm)
            return;

        switch (e.PropertyName)
        {
            case nameof(MatchViewModel.IsPreviewVisible):
                SetPreviewPaneVisible(vm.IsPreviewVisible);
                break;

            case nameof(MatchViewModel.IsIndexing):
                if (vm.IsIndexing)
                {
                    _activityDotsIndex = 0;
                    ActivityDotsText.Text = ActivityDotsFrames[0];
                    _activityDotsTimer.Start();
                }
                else
                {
                    _activityDotsTimer.Stop();
                }
                break;
        }
    }

    // The preview Border's own IsVisible is bound in XAML, but a star-sized Grid column
    // keeps its share of the width even when its only child is collapsed — so the gap
    // and pane columns are zeroed here too, letting the results tree take the full
    // width until a ROM or candidate row is actually highlighted.
    private void SetPreviewPaneVisible(bool visible)
    {
        var columns = ResultsLayout.ColumnDefinitions;
        columns[1].Width = visible ? new GridLength(8) : new GridLength(0);
        columns[2].Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void OnResultsTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            // Scoped to vm.Groups — the already-filtered collection — not every ROM the
            // last scan found, so Ctrl+A only ever selects what's actually visible under
            // the current File type/Region/Exact-score filters. Leaf (MatchCandidate) rows
            // are deliberately left out of "select all"; this is specifically for the
            // batch-ignore workflow, which only ever acts on ROM rows.
            if (DataContext is MatchViewModel vm)
            {
                ResultsTree.SelectedItems.Clear();
                foreach (var group in vm.Groups)
                    ResultsTree.SelectedItems.Add(group);
            }
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space)
            return;

        // Prefer whatever row visually has keyboard focus right now (walking up from the
        // event's original source); fall back to the bound selection in case focus turns
        // out to still be sitting on the TreeView itself rather than the row's own
        // container (arrow-key navigation and click-to-focus don't necessarily behave
        // identically here).
        var candidate = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as MatchCandidate
                        ?? (DataContext as MatchViewModel)?.SelectedCandidate;

        if (candidate is not null)
        {
            candidate.IsSelected = !candidate.IsSelected;
            e.Handled = true;
        }
    }

    private void OnIgnoreRomClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: RomMatchGroup rightClicked } || DataContext is not MatchViewModel vm)
            return;

        // If the right-clicked row is part of the current multi-selection, act on the
        // whole selection; otherwise (right-clicking a row that isn't selected) act on
        // just that one row, matching ordinary desktop context-menu conventions.
        var selected = ResultsTree.SelectedItems.OfType<RomMatchGroup>().ToList();
        var targets = selected.Contains(rightClicked) ? selected : new List<RomMatchGroup> { rightClicked };

        vm.IgnoreRomsCommand.Execute(targets);
    }

    // Always acts on just the right-clicked row, not the broader multi-selection —
    // unlike OnIgnoreRomClick above, opening a file manager window per selected row
    // wouldn't make sense (see FileExplorerService's doc comment).

    private void OnOpenRomFilePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RomMatchGroup group })
            FileExplorerService.Reveal(group.RomFullPath);
    }

    // e.Source (not sender) deliberately — this handler is attached to the parent
    // "Ignore Folder" MenuItem, and Click bubbles up from whichever auto-generated
    // child (one per IgnorableAncestorFolders entry) was actually clicked; sender here
    // would always be the parent itself (DataContext = RomMatchGroup, not a folder
    // path), while e.Source is the specific child that raised the event.
    private void OnIgnoreFolderClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: string folderPath } && DataContext is MatchViewModel vm)
            vm.IgnoreFolderCommand.Execute(folderPath);
    }

    private void OnOpenImageFilePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MatchCandidate candidate })
            FileExplorerService.Reveal(candidate.ImageFullPath);
    }
}
