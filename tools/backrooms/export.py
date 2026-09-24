"""Writes the Level 0 easter-egg frames the app ships (Assets/Backrooms/*.png) at the
renderer's native 320x180 — the app scales them up nearest-neighbour
(RenderOptions.BitmapInterpolationMode="None"), so they stay crisp pixel art at any
window size. One PNG per state of the dead ceiling panel; BackroomsView flickers
between them at random.

    python tools/backrooms/export.py
"""
from pathlib import Path

from PIL import Image

import scene
from flicker import PANEL, YAW

OUT = Path(__file__).resolve().parents[2] / "Assets" / "Backrooms"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for state in ("dead", "dim", "lit"):
        frame = Image.fromarray(scene.render(YAW, PANEL, state))
        # Palette mode: the frame only ever uses the 22 Backrooms-22 colours, so this is
        # lossless and a fraction of the RGB size.
        frame.quantize(colors=32, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE) \
             .save(OUT / f"level0_{state}.png", optimize=True)
        print(OUT / f"level0_{state}.png")


if __name__ == "__main__":
    main()
