using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GameArtMatch.Services;

/// <summary>
/// Window properties Avalonia 12's X11 backend doesn't set (or sets poorly), written
/// straight to the X server. Both are read by KWin — including for XWayland windows
/// under Plasma Wayland — Mutter and Xfwm. A no-op on Windows (which has native
/// equivalents) and wherever libX11 or a display isn't available.
///
/// _GTK_FRAME_EXTENTS (SetFrameExtents): with WindowDecorations="BorderOnly", Avalonia
/// draws the window's shadow itself as a transparent margin inside the X window, but
/// never reports it (its IWindowImpl.SetShadowExtents is the default no-op), so the WM
/// snaps and tiles against the invisible shadow edge — a visible gap. Declaring the
/// extents makes it use the visible border, like any GTK client-side-decorated app.
///
/// _NET_WM_ICON (SetIcons): Avalonia decodes only the largest image of the window's
/// .ico and redraws it smoothly at 128x128, so the WM gets one size and shrinks it —
/// blurry at in-between sizes, and the hand-drawn 16/24 px designs never used. The
/// property can carry several sizes; the WM picks the closest. We send every size, as
/// exact pixels.
/// </summary>
public static class X11WindowHints
{
    private const string LibX11 = "libX11.so.6";
    private const int PropModeReplace = 0;

    private static readonly Lazy<IntPtr> Display = new(OpenDisplay);

    /// <summary>Sets _GTK_FRAME_EXTENTS to <paramref name="shadow"/> (in DIPs, scaled here
    /// to device pixels) — pass a zero Thickness while maximized/fullscreen, when
    /// Avalonia drops the shadow.</summary>
    public static void SetFrameExtents(Window window, Thickness shadow)
    {
        var scale = window.RenderScaling;
        // left, right, top, bottom — _GTK_FRAME_EXTENTS' order (not Thickness's).
        SetCardinals(window, "_GTK_FRAME_EXTENTS",
        [
            (nint)Math.Round(shadow.Left * scale),
            (nint)Math.Round(shadow.Right * scale),
            (nint)Math.Round(shadow.Top * scale),
            (nint)Math.Round(shadow.Bottom * scale),
        ]);
    }

    /// <summary>Sets _NET_WM_ICON to every image in <paramref name="iconUris"/> (avares://
    /// PNGs, one per size). Format: for each image, width, height, then width*height
    /// ARGB pixels, each in a C long.</summary>
    public static void SetIcons(Window window, IEnumerable<Uri> iconUris)
    {
        if (!IsX11(window, out _))
            return;

        var data = new List<nint>();
        foreach (var uri in iconUris)
        {
            using var bitmap = new Bitmap(AssetLoader.Open(uri));
            var (w, h) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            // Bgra8888 read as little-endian uint is 0xAARRGGBB — _NET_WM_ICON's layout.
            // The icons are fully opaque or fully transparent, so premultiplied and
            // straight alpha are the same here.
            var pixels = new uint[w * h];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length * 4, w * 4);
            }
            finally
            {
                handle.Free();
            }

            data.Add(w);
            data.Add(h);
            foreach (var p in pixels)
                data.Add((nint)p);
        }

        SetCardinals(window, "_NET_WM_ICON", data.ToArray());
    }

    private static bool IsX11(Window window, out IntPtr xid)
    {
        xid = IntPtr.Zero;
        if (!OperatingSystem.IsLinux() || window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
            return false;
        xid = handle.Handle;
        return true;
    }

    private static void SetCardinals(Window window, string propertyName, nint[] data)
    {
        if (!IsX11(window, out var xid))
            return;

        try
        {
            var display = Display.Value;
            if (display == IntPtr.Zero)
                return;

            var property = XInternAtom(display, propertyName, false);
            var cardinal = XInternAtom(display, "CARDINAL", false);
            // Format-32 X properties are arrays of C long, i.e. nint on 64-bit Linux.
            XChangeProperty(display, xid, property, cardinal, 32, PropModeReplace, data, data.Length);
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
