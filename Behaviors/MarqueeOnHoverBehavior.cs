using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace GameArtMatch.Behaviors;

/// <summary>
/// Attached behavior for a ROM/image filename that's too long for the space it's given.
/// REQUIRES a specific structure: the TextBlock's Parent must be a Canvas, and the
/// Canvas's Parent must be a clipping Border (ClipToBounds="True") — see every call
/// site's XAML for the exact shape.
///
/// Two earlier attempts at this got the measurement wrong:
///  - TextBlock directly inside a Border (HorizontalAlignment="Left" and all): still
///    measured against the Border's small column width, since a parent hands its
///    *available* size down at Measure time regardless of the child's own alignment —
///    alignment only affects how an already-computed size gets arranged afterward.
///  - TextBlock inside a ScrollViewer: worked in theory (ScrollViewer measures Content
///    unconstrained to compute Extent) but wasn't actually confirmed correct in this app.
/// Canvas is the standard Avalonia/WPF idiom for this specific problem: its
/// MeasureOverride always measures every child with (Infinity, Infinity) and arranges
/// each one at its own natural DesiredSize (verified directly against Avalonia's own
/// Canvas.cs), so the TextBlock's Bounds always reflects its true, unclipped width
/// regardless of how narrow the visible column is. Canvas.MeasureOverride always
/// reports (0,0) itself, though — it never sizes to fit its children — so a row with no
/// other sibling cell to establish a height needs the wrapping Border to declare one
/// explicitly (see each call site's Height), or the row would collapse to nothing.
///
/// While the pointer stays over it: scrolls left until the clipped tail is visible,
/// holds 2s, snaps back to the start, holds 2s, then repeats. Leaving the pointer at any
/// point during any phase cancels the loop and snaps straight back to the start.
///
/// Driven by TopLevel.RequestAnimationFrame (real elapsed time between frames) rather
/// than a fixed Task.Delay-per-frame loop, so scroll speed doesn't drift under frame
/// drops/system load the way a "just wait 16ms and hope" loop would.
/// </summary>
public static class MarqueeOnHoverBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsEnabled", typeof(MarqueeOnHoverBehavior));

    private static readonly AttachedProperty<CancellationTokenSource?> LoopCtsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, CancellationTokenSource?>("LoopCts", typeof(MarqueeOnHoverBehavior));

    private const double PixelsPerSecond = 60;
    private static readonly TimeSpan PauseDuration = TimeSpan.FromSeconds(2);

    public static bool GetIsEnabled(TextBlock element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(TextBlock element, bool value) => element.SetValue(IsEnabledProperty, value);

    static MarqueeOnHoverBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBlock>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        textBlock.PointerEntered += OnPointerEntered;
        textBlock.PointerExited += OnPointerExited;

        // Canvas positions children at (0,0) by default (it doesn't honor
        // VerticalAlignment at all) — without this, the filename would render
        // top-aligned within its row at rest, only becoming centered the first time
        // it's hovered. LayoutUpdated re-centers on every layout pass (initial render,
        // window resize, row height changes), not just while scrolling.
        textBlock.LayoutUpdated += (_, _) => CenterVertically(textBlock);
    }

    private static void CenterVertically(TextBlock textBlock)
    {
        if (textBlock.Parent is Canvas && textBlock.Parent.Parent is Control viewport)
            Canvas.SetTop(textBlock, (viewport.Bounds.Height - textBlock.Bounds.Height) / 2);
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBlock textBlock)
            return;

        var cts = new CancellationTokenSource();
        textBlock.SetValue(LoopCtsProperty, cts);
        _ = RunLoopAsync(textBlock, cts.Token);
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBlock textBlock)
            return;

        ResetAndStop(textBlock);
    }

    private static void ResetAndStop(TextBlock textBlock)
    {
        textBlock.GetValue(LoopCtsProperty)?.Cancel();
        textBlock.SetValue(LoopCtsProperty, null);
        Canvas.SetLeft(textBlock, 0);
    }

    private static async Task RunLoopAsync(TextBlock textBlock, CancellationToken token)
    {
        if (textBlock.Parent is not Canvas || textBlock.Parent?.Parent is not Control viewport)
            return;

        // Vertical centering is handled continuously by CenterVertically (via
        // LayoutUpdated), not here — this only needs the horizontal overflow.
        // textBlock.Bounds.Width is now its TRUE natural width (see class doc comment on
        // why Canvas makes that true here) — comparing it to the viewport's real width
        // tells us how much is actually hidden.
        var overflow = textBlock.Bounds.Width - viewport.Bounds.Width;

        // Not actually truncated (short filename, or a column wide enough to fit it) —
        // nothing to scroll, leave it alone.
        if (overflow <= 0)
            return;

        var topLevel = TopLevel.GetTopLevel(textBlock);
        if (topLevel is null)
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await ScrollToAsync(topLevel, textBlock, overflow, token);
                if (token.IsCancellationRequested)
                    break;

                await Task.Delay(PauseDuration, token); // hold with the tail visible
                Canvas.SetLeft(textBlock, 0);
                await Task.Delay(PauseDuration, token); // hold at the start before looping
            }
        }
        catch (OperationCanceledException)
        {
            // Expected the moment the pointer leaves — OnPointerExited already reset
            // Canvas.Left to 0 itself, so there's nothing left to clean up here.
        }
    }

    /// <summary>Animates Canvas.Left from 0 to -overflow over overflow/PixelsPerSecond,
    /// driven by real elapsed wall-clock time between animation frames rather than an
    /// assumed fixed frame interval.</summary>
    private static Task ScrollToAsync(TopLevel topLevel, TextBlock textBlock, double overflow, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        var duration = TimeSpan.FromSeconds(overflow / PixelsPerSecond);
        TimeSpan? lastTimestamp = null;
        var elapsed = TimeSpan.Zero;

        void OnFrame(TimeSpan timestamp)
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetResult();
                return;
            }

            if (lastTimestamp is { } last)
                elapsed += timestamp - last;
            lastTimestamp = timestamp;

            var progress = Math.Min(elapsed.TotalSeconds / duration.TotalSeconds, 1.0);
            Canvas.SetLeft(textBlock, -overflow * progress);

            if (progress >= 1.0)
            {
                tcs.TrySetResult();
                return;
            }

            topLevel.RequestAnimationFrame(OnFrame);
        }

        topLevel.RequestAnimationFrame(OnFrame);
        return tcs.Task;
    }
}
