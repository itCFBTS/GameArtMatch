using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using GameArtMatch.Services;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Stand-in for the old File > Exit — closing the main window ends the app.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Q, KeyModifiers.Control), Command = new RelayCommand(Close) });

        // Ctrl+F: jump to the Match page's search box (switching to Match if needed).
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(FocusSearch) });

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.NoclipTransition += async (_, e) => await PlayNoclipAsync(e);
        };

        Opened += (_, _) => UpdateFrameExtents();
        ScalingChanged += (_, _) => UpdateFrameExtents();
    }

    private void FocusSearch()
    {
        if (DataContext is MainViewModel vm)
            vm.IsReportPageActive = false;
        // Posted: if the Match page was hidden, its TextBox can't take focus until the
        // layout pass that shows it.
        Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<MatchView>().FirstOrDefault()?.FocusSearch(),
            DispatcherPriority.Loaded);
    }

    private bool _noclipPlaying;

    // Lights stutter out, the theme swaps while the screen is dark, the Backrooms hold
    // for a moment with a caption, then everything fades back in on the new theme.
    private async Task PlayNoclipAsync(NoclipEventArgs e)
    {
        if (_noclipPlaying)
            return;
        _noclipPlaying = true;
        try
        {
            NoclipCaption.Text = e.Entering ? "You've noclipped out of reality." : "You clipped back into reality.";
            NoclipOverlay.IsVisible = true;

            // Fluorescent stutter: the overlay blinks in over a few frames.
            foreach (var (opacity, ms) in new[] { (0.9, 60), (0.15, 70), (1.0, 50), (0.35, 90), (1.0, 140) })
            {
                NoclipOverlay.Opacity = opacity;
                await Task.Delay(ms);
            }

            e.ApplyTheme(); // hidden behind the fully opaque overlay
            await Task.Delay(e.Entering ? 2200 : 1300);

            for (var step = 1; step <= 20; step++)
            {
                NoclipOverlay.Opacity = 1 - step / 20.0;
                await Task.Delay(30);
            }
        }
        finally
        {
            NoclipOverlay.IsVisible = false;
            NoclipOverlay.Opacity = 0;
            _noclipPlaying = false;
        }
    }

    // Keeps the WM's idea of our shadow in step with what Avalonia draws: the shadow
    // exists only in the normal state (Avalonia drops it when maximized/fullscreen), so
    // report zero extents then, or a maximized window would be inset by the shadow width.
    // Posted so it runs after Avalonia has applied the state change to its decorations.
    private void UpdateFrameExtents() => Dispatcher.UIThread.Post(() =>
    {
        var shadow = WindowState is WindowState.Normal or WindowState.Minimized
                     && this.TryFindResource("WindowShadowThickness", out var value) && value is Thickness t
            ? t
            : default;
        X11FrameExtents.Apply(this, shadow);
    });

    private const string MaximizeGlyph = "M4,4H20V20H4V4M6,6V18H18V6H6Z";
    private const string RestoreGlyph = "M4,8H8V4H20V16H16V20H4V8M16,8V14H18V6H10V8H16M6,12V18H14V12H6Z";

    // The title bar replacement: plain BeginMoveDrag rather than marking the strip with
    // WindowDecorationProperties.ElementRole="TitleBar" — on X11 that role hands the
    // press straight to the window manager as a move, so a double-click never reaches
    // the app and can't toggle maximize. Buttons inside the strip mark their own
    // presses handled, so they never start a drag.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Swap the maximize glyph for "restore" while maximized — however that happened
    // (button, double-click, or the OS itself, e.g. a Super+Up shortcut).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || MaximizeIcon is null)
            return;

        UpdateFrameExtents();

        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = StreamGeometry.Parse(maximized ? RestoreGlyph : MaximizeGlyph);
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void OnOptionsBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CloseOptionsCommand.CanExecute(null))
            vm.CloseOptionsCommand.Execute(null);
        e.Handled = true;
    }

    // Per-system pickers: start from the persisted root, if one is set, so picking a
    // specific system's folder is a couple of clicks instead of navigating from scratch.
    private async void OnBrowseRomsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await PickFolderAsync("Choose your ROMs directory", vm.Settings.RomsRootPath);
        if (path is not null) vm.Settings.RomsPath = path;
    }

    private async void OnBrowseImagesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await PickFolderAsync("Choose your images directory", vm.Settings.ImagesRootPath);
        if (path is not null) vm.Settings.ImagesPath = path;
    }

    private async Task<string?> PickFolderAsync(string title, string? startInPath)
    {
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(startInPath))
        {
            try
            {
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(startInPath);
            }
            catch
            {
                // Root path no longer exists (moved drive, renamed folder, etc.) — just fall
                // back to the picker's own default location instead of failing the pick.
            }
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
