using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace GameArtMatch.Views;

/// <summary>
/// The Backrooms scene with its one dead ceiling panel. The panel sits dark and, every
/// so often — a random 8–30 s — sputters: a burst of lit/dim/dead frames with random
/// short timings, sometimes catching for a moment, always ending dead. Random rather
/// than a fixed loop so it never settles into a rhythm you can predict.
/// Timer is owned here and runs only while attached (avalonia-patterns: timer
/// ownership), and bursts are skipped while the control isn't actually on screen.
/// </summary>
public partial class BackroomsView : UserControl
{
    private static readonly Lazy<Bitmap> Dead = new(() => Load("dead"));
    private static readonly Lazy<Bitmap> Dim = new(() => Load("dim"));
    private static readonly Lazy<Bitmap> Lit = new(() => Load("lit"));

    private readonly DispatcherTimer _timer = new();
    private readonly Random _random = new();
    private readonly Queue<(Bitmap Frame, int Ms)> _burst = new();

    /// <summary>Raised as each flicker burst begins — MatchView jolts the preview carousel
    /// in sync with it (a Level 0 glitch).</summary>
    public event EventHandler? BurstStarted;

    public BackroomsView()
    {
        InitializeComponent();
        Scene.Source = Dead.Value;
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => ScheduleNextBurst();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    // Pure decoration: never ask the layout for space. The Image fills with
    // Stretch="UniformToFill", which by design measures LARGER than the space offered in
    // one direction (then relies on clipping), and that inflated size leaked into
    // whatever panel hosted the backdrop — after a resize, MatchView's carousel laid
    // itself out for a taller area than was visible. Measuring the image but reporting
    // zero keeps it out of layout; the host still arranges it at full size (Stretch
    // alignment), and ClipToBounds crops the overflow as before.
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return default;
    }

    private static Bitmap Load(string state) =>
        new(AssetLoader.Open(new Uri($"avares://GameArtMatch/Assets/Backrooms/level0_{state}.png")));

    private void ScheduleNextBurst()
    {
        _burst.Clear();
        Scene.Source = Dead.Value;
        _timer.Interval = TimeSpan.FromSeconds(8 + _random.NextDouble() * 22);
        _timer.Start();
    }

    private void Step()
    {
        if (_burst.Count == 0)
        {
            if (!IsEffectivelyVisible)
            {
                ScheduleNextBurst();
                return;
            }
            PlanBurst();
            BurstStarted?.Invoke(this, EventArgs.Empty);
        }

        var (frame, ms) = _burst.Dequeue();
        Scene.Source = frame;
        if (_burst.Count == 0)
        {
            ScheduleNextBurst();
            return;
        }
        _timer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    // A tube struggling to strike: 2–5 quick blips (lit or dim, 30–120 ms, with
    // 50–160 ms of dark between), and one time in three a brief catch (250–700 ms lit)
    // before it gives out again.
    private void PlanBurst()
    {
        var blips = _random.Next(2, 6);
        for (var i = 0; i < blips; i++)
        {
            _burst.Enqueue((_random.Next(3) == 0 ? Dim.Value : Lit.Value, _random.Next(30, 121)));
            _burst.Enqueue((Dead.Value, _random.Next(50, 161)));
        }

        if (_random.Next(3) == 0)
        {
            _burst.Enqueue((Lit.Value, _random.Next(250, 701)));
            _burst.Enqueue((Dim.Value, _random.Next(40, 90)));
        }

        _burst.Enqueue((Dead.Value, 0));
    }
}
