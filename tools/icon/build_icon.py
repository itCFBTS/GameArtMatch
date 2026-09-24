"""Builds the app icon from the pixel-art designs and writes it into the app:

    Assets/Icon/gameartmatch.ico      16, 24, 32, 48, 64, 128, 256
    Assets/Icon/png/gameartmatch-<size>.png  every size separately — the app sends
        these to the Linux window manager itself (X11WindowHints.SetIcons), since
        Avalonia only passes it one resampled size from the .ico

Three hand-made designs, each used where there's room for its level of detail:
  16  small.MINI_CART       hand-placed mini cartridge (no wing notches)
  24  small24.grid()        rule-built, same family as the 16
  32  n64 + vapor           full design (wing notches, plastic shading, grid, road)
48 is the 32 enlarged 1.5x (slightly uneven pixels — Explorer's medium view is its
main use); 64/128/256 are exact 2x/4x/8x enlargements of the 32, so they stay crisp.

    python tools/icon/build_icon.py

Also renders the one-colour glyph (glyph.svg) as the window/taskbar icon — see
build_glyph(). The full-colour .ico stays the Windows .exe icon (Start menu,
Explorer, shortcuts).
"""
import subprocess
from pathlib import Path

from PIL import Image

import n64
import small
import small24
import vapor

OUT = Path(__file__).resolve().parents[2] / "Assets" / "Icon"


def build():
    i16 = small.to_image(small.MINI_CART)
    i24 = small24.to_image(small24.grid())
    vapor.SHOW_CAR = False
    n64.picture_colour = vapor.vapor_scene
    i32 = n64.render()

    frames = [i16, i24, i32] + [i32.resize((s, s), Image.NEAREST) for s in (48, 64, 128, 256)]
    OUT.mkdir(parents=True, exist_ok=True)
    # Pillow writes each requested size from the matching append_images frame (not a
    # resample of the largest), so the hand-made small sizes survive intact.
    frames[-1].save(OUT / "gameartmatch.ico", format="ICO",
                    sizes=[f.size for f in frames], append_images=frames[:-1])
    (OUT / "png").mkdir(exist_ok=True)
    for f in frames:
        f.save(OUT / "png" / f"gameartmatch-{f.width}.png")
    return frames


GLYPH_SVG = Path(__file__).resolve().parent / "glyph.svg"
GLYPH_SIZES = (16, 24, 32, 48, 64, 128, 256)


def build_glyph():
    """The one-colour glyph as the window/taskbar icon: rendered from glyph.svg at every
    size (it's vector, so each size is drawn fresh — no upscaling) into
    Assets/Icon/glyph/ and Assets/Icon/gameartmatch-glyph.ico. Needs rsvg-convert
    (librsvg)."""
    out = OUT / "glyph"
    out.mkdir(parents=True, exist_ok=True)
    frames = []
    for size in GLYPH_SIZES:
        png = out / f"gameartmatch-glyph-{size}.png"
        subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), str(GLYPH_SVG), "-o", str(png)], check=True)
        frames.append(Image.open(png).convert("RGBA"))
    frames[-1].save(OUT / "gameartmatch-glyph.ico", format="ICO",
                    sizes=[f.size for f in frames], append_images=frames[:-1])
    return frames


if __name__ == "__main__":
    frames = build()
    build_glyph()
    ico = Image.open(OUT / "gameartmatch.ico")
    for f in frames:
        stored = ico.ico.getimage(f.size).convert("RGBA")
        assert stored.tobytes() == f.tobytes(), f"{f.size} frame was altered"
    print(OUT / "gameartmatch.ico", sorted(ico.info["sizes"]))
