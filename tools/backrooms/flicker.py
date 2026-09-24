"""Animated Level 0: scene.py's 28-degree view with one dead fluorescent panel that
sputters back to life only occasionally. Renders the three panel states once (only
that panel differs between them) and sequences them with per-frame durations into a
looping GIF, so a 7-second loop costs three renders, not seventy.

    python flicker.py [out.gif]
"""
import sys
from PIL import Image
import scene

PANEL = (3, 3)   # the nearest, most visible panel at yaw 28 (see scene.render's panel_ids)
YAW = 28

# (state, milliseconds). Mostly dead; one ragged burst near the end of the loop — a
# tube struggling to strike: two quick blips, a near-catch, a brief hold, then out.
TIMELINE = [
    ("dead", 5200),
    ("lit", 50), ("dead", 110),
    ("dim", 40), ("dead", 60),
    ("lit", 70), ("dim", 50), ("lit", 380),
    ("dead", 140), ("dim", 60),
    ("dead", 900),
]


def main(out):
    frames = {state: scene.render(YAW, PANEL, state) for state in ("dead", "dim", "lit")}
    size = (scene.W * scene.SCALE, scene.H * scene.SCALE)
    images = {k: Image.fromarray(v).resize(size, Image.NEAREST) for k, v in frames.items()}
    # One shared palette (the 22 Backrooms-22 colours) across frames, no re-dithering.
    pal_src = images["lit"].quantize(colors=32, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    seq = [images[s].quantize(palette=pal_src, dither=Image.Dither.NONE) for s, _ in TIMELINE]
    seq[0].save(out, save_all=True, append_images=seq[1:], duration=[ms for _, ms in TIMELINE],
                loop=0, optimize=False, disposal=1)
    images["dead"].save(out.replace(".gif", "_dead.png"))
    images["lit"].save(out.replace(".gif", "_lit.png"))
    print(out, f"{sum(ms for _, ms in TIMELINE) / 1000:.1f}s loop")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "level0_flicker.gif")
