using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace GameArtMatch.Services;

/// <summary>
/// Tells an X11 window manager how much of the window is app-drawn shadow, via the
/// _GTK_FRAME_EXTENTS property GTK apps set for their own client-side shadows (honored
/// by KWin — including for XWayland windows under Plasma Wayland — Mutter, and Xfwm).
///
/// Why: with WindowDecorations="BorderOnly", Avalonia draws the window's border and
/// shadow itself (see MainWindow.axaml), the shadow being a transparent margin inside
/// the X window. Avalonia 12's X11 backend doesn't report that margin (its
/// IWindowImpl.SetShadowExtents is the default no-op), so the WM treats the invisible
/// shadow edge as the window edge: edge snapping and quick tiling leave a visible gap
/// the width of the shadow. Declaring the extents makes the WM snap/tile/maximize
/// against the visible border instead, the same as any GTK client-side-decorated app.
///
/// A no-op everywhere else: Windows uses its native frame (no drawn shadow), and a
/// failure to reach X (no libX11, no display) just leaves the old behavior.
/// </summary>
public static class X11FrameExtents
{
    private const string LibX11 = "libX11.so.6";
    private const int PropModeReplace = 0;

    private static readonly Lazy<IntPtr> Display = new(OpenDisplay);

    /// <summary>Sets _GTK_FRAME_EXTENTS on <paramref name="window"/> to
    /// <paramref name="shadow"/> (in DIPs, scaled here to device pixels) — pass a zero
    /// Thickness while maximized/fullscreen, when Avalonia drops the shadow.</summary>
    public static void Apply(Window window, Thickness shadow)
    {
        if (!OperatingSystem.IsLinux()
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
            return;

        try
        {
            var display = Display.Value;
            if (display == IntPtr.Zero)
                return;

            var scale = window.RenderScaling;
            // left, right, top, bottom — _GTK_FRAME_EXTENTS' order (not Thickness's).
            // Format-32 X properties are arrays of C long, i.e. nint on 64-bit Linux.
            nint[] extents =
            [
                (nint)Math.Round(shadow.Left * scale),
                (nint)Math.Round(shadow.Right * scale),
                (nint)Math.Round(shadow.Top * scale),
                (nint)Math.Round(shadow.Bottom * scale),
            ];

            var property = XInternAtom(display, "_GTK_FRAME_EXTENTS", false);
            var cardinal = XInternAtom(display, "CARDINAL", false);
            XChangeProperty(display, handle.Handle, property, cardinal, 32, PropModeReplace, extents, extents.Length);
            XFlush(display);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an X11 system after all — nothing to tell.
        }
    }

    // A connection of our own rather than Avalonia's (not exposed publicly). Properties
    // live on the X server, so which client sets them doesn't matter. Opened once and
    // kept for the process's lifetime.
    private static IntPtr OpenDisplay()
    {
        try
        {
            return XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(LibX11)]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type,
        int format, int mode, nint[] data, int elementCount);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);
}
