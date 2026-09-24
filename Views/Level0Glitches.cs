using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

/// <summary>
/// Level 0's deliberate glitches — rare, brief, self-healing, and never touching data
/// (filenames, scores and paths are what a rename decision rests on, so only interface
/// text is ever affected). Every 45–150 s, if Level 0 is active and nothing's in
/// progress, one of:
///   - a vanishing letter: one letter of a "glitchable" label (sidebar headings and field
///     labels, toggle labels, column headers, the app name) fades out over 20 s and stays
///     gone until the pointer touches that label or a few minutes pass;
///   - ROM → ROOM: for 5 s the results summary and search placeholder read "ROOM".
/// The third glitch, the carousel jolt, lives in MatchView (it's synced to the
/// backdrop's light flicker). "Level 0 is active" is the theme's own
/// ShowBackroomsBackdrop flag, so no theme id is hard-coded here. Owned by MainWindow;
/// its timers stop with the window.
/// </summary>
public sealed class Level0Glitches
{
    private const int MaxVanishedLetters = 3;
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Dictionary<TextBlock, Vanished> _vanished = new();

    private sealed record Vanished(string Original, DispatcherTimer Fade, DispatcherTimer Return);

    public Level0Glitches(Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
        _timer.Tick += (_, _) => Tick();
        // A theme switch mid-glitch puts everything back at once.
        vm.OptionsVm.PropertyChanged += OnOptionsChanged;
    }

    public void Start() => ScheduleNext();

    public void Stop()
    {
        _timer.Stop();
        RestoreAll();
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptionsViewModel.SelectedTheme))
            RestoreAll();
    }

    private void ScheduleNext()
    {
        _timer.Interval = TimeSpan.FromSeconds(45 + _random.NextDouble() * 105);
        _timer.Start();
    }

    private bool Active() =>
        _window.IsActive
        && _window.TryFindResource("ShowBackroomsBackdrop", out var on) && on is true
        && !_vm.MatchVm.IsBusy
        && !_vm.IsOptionsOpen;

    private void Tick()
    {
        _timer.Stop();
        if (Active())
        {
            if (_random.Next(2) == 0 && _vm.IsMatchPageActive)
                RoomPun();
            else
                VanishLetter();
        }
        ScheduleNext();
    }

    private void RoomPun()
    {
        _vm.MatchVm.RoomPun = true;
        DispatcherTimer.RunOnce(() => _vm.MatchVm.RoomPun = false, TimeSpan.FromSeconds(5));
    }

    private void VanishLetter()
    {
        if (_vanished.Count >= MaxVanishedLetters)
            return;

        var candidates = _window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("glitchable") && t.IsEffectivelyVisible
                        && !_vanished.ContainsKey(t) && t.Text?.Any(char.IsLetter) == true)
            .ToList();
        if (candidates.Count == 0)
            return;

        var target = candidates[_random.Next(candidates.Count)];
        var text = target.Text!;
        var letters = Enumerable.Range(0, text.Length).Where(i => char.IsLetter(text[i])).ToList();
        var index = letters[_random.Next(letters.Count)];

        // The letter becomes its own run with a brush we fade; the rest stays as-is. Its
        // text is still there, just transparent, so screen readers read the label intact.
        var colour = (target.Foreground as ISolidColorBrush)?.Color ?? Colors.Gray;
        var brush = new SolidColorBrush(colour);
        // Text first, then runs: adding a run to a TextBlock that still has Text moves
        // that text into a run of its own, and the label would read twice.
        target.Text = null;
        target.Inlines!.Clear();
        target.Inlines.Add(new Run(text[..index]));
        target.Inlines.Add(new Run(text[index].ToString()) { Foreground = brush });
        target.Inlines.Add(new Run(text[(index + 1)..]));

        var fade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        fade.Tick += (_, _) =>
        {
            brush.Opacity = Math.Max(0, brush.Opacity - 0.05); // gone after ~20 s
            if (brush.Opacity <= 0)
                fade.Stop();
        };
        var back = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3 + _random.NextDouble() * 3) };
        back.Tick += (_, _) => Restore(target);

        _vanished[target] = new Vanished(text, fade, back);
        target.PointerEntered += OnVanishedTouched;
        fade.Start();
        back.Start();
    }

    private void OnVanishedTouched(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is TextBlock target)
            Restore(target);
    }

    private void Restore(TextBlock target)
    {
        if (!_vanished.Remove(target, out var v))
            return;

        v.Fade.Stop();
        v.Return.Stop();
        target.PointerEntered -= OnVanishedTouched;
        target.Inlines?.Clear();
        target.Text = null; // force a real change so the plain text is laid out afresh
        target.Text = v.Original;
    }

    private void RestoreAll()
    {
        foreach (var target in _vanished.Keys.ToList())
            Restore(target);
        _vm.MatchVm.RoomPun = false;
    }
}
