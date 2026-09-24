"""Level 0 'OG photo' composition: camera in a doorway, turned ~28 deg, looking into
an open room. Same raycaster + palette quantiser as render.py, but a hand-laid map,
taller walls, off-centre eye height with a slight downward tilt, long fluorescent
tubes, patterned wallpaper (chevron / pegboard / pinstripe per wall), and a bright
cream haze instead of dark murk.

    python scene.py [yaw_degrees] [out.png]
"""
import math, sys
import numpy as np
from PIL import Image
from render import PALETTE, quantise  # same 22-colour palette + Bayer dither

W, H, SCALE = 320, 180, 4
FOV = math.radians(72)
WALL_H = 1.5      # wall height in tile units — offices are taller than they are wide here
EYE = 0.62        # eye height above the floor
HORIZON = int(H * 0.40)  # above centre = camera tilted slightly down, like the photo

WALL = np.array([226, 224, 168], np.float32)
WALL_INK = np.array([176, 170, 116], np.float32)
BASEBOARD = np.array([150, 140, 98], np.float32)
CARPET = np.array([204, 192, 140], np.float32)
CEILING = np.array([230, 228, 184], np.float32)
TUBE = np.array([250, 250, 232], np.float32)
DEAD_TUBE = np.array([176, 172, 150], np.float32)
HAZE = np.array([168, 160, 108], np.float32)

def build_grid():
    """Laid out to match the photo's framing from the camera below: a wall ending
    almost dead ahead on the left, a wall corner at ~70% across on the right, and the
    open room seen diagonally through the gap between them."""
    g = np.zeros((24, 24), np.uint8)
    g[0, :] = g[-1, :] = g[:, 0] = g[:, -1] = 1
    g[17, 1:9] = 1        # left wall — its east end is the photo's left "pillar" edge
    g[17:23, 11] = 1      # right wall, running back past the camera
    g[17, 11:23] = 1
    g[9:12, 5:7] = 1      # the yellow wall block seen left of centre
    g[10, 13:17] = 1      # free-standing pinstripe partition, centre
    g[5, 9:22] = 1        # back wall
    g[5:9, 21] = 1
    return g


GRID = build_grid()
POS = (8.95, 19.2)


def texture_kind(mx, my):
    # Like the photo: pegboard on the near-left wall, chevrons on the right wall,
    # pinstripe partition, everything else plain-ish chevron.
    if my == 17 and mx <= 8:
        return 1
    if (my == 10 and 13 <= mx <= 16) or my == 5:
        return 2
    return 0


def cast(angle):
    px, py = POS
    dx, dy = math.cos(angle), math.sin(angle)
    mx, my = int(px), int(py)
    ddx = abs(1 / dx) if dx else 1e30
    ddy = abs(1 / dy) if dy else 1e30
    stx, sdx = (1, (mx + 1 - px) * ddx) if dx > 0 else (-1, (px - mx) * ddx)
    sty, sdy = (1, (my + 1 - py) * ddy) if dy > 0 else (-1, (py - my) * ddy)
    side = 0
    while True:
        if sdx < sdy:
            sdx += ddx; mx += stx; side = 0
        else:
            sdy += ddy; my += sty; side = 1
        if GRID[my, mx]:
            break
    dist = (sdx - ddx) if side == 0 else (sdy - ddy)
    hit = (py + dist * dy) if side == 0 else (px + dist * dx)
    return dist, side, hit - math.floor(hit), mx, my


def falloff(d):
    return math.exp(-d * 0.06)


def wall_colour(kind, u, v):
    # v: 0 at the top of the wall, 1 at the floor.
    if v > 0.955:
        return BASEBOARD
    ink = False
    if kind == 0:  # narrow vertical bands of small stacked "^" chevrons
        su, sv = (u * 7) % 1, (v * WALL_H * 11) % 1
        ink = abs(sv - (1 - abs(su - 0.5) * 1.1)) < 0.06 or su < 0.035
    elif kind == 1:  # fine pegboard holes
        su, sv = (u * 18) % 1, (v * WALL_H * 18) % 1
        ink = abs(su - 0.5) < 0.09 and abs(sv - 0.5) < 0.09
    else:  # pinstripes
        ink = (u * 14) % 1 < 0.1
    return WALL_INK if ink else WALL


def render(yaw_deg, dead_panel=None, panel_state="lit", panel_ids=None):
    heading = -math.pi / 2 + math.radians(yaw_deg)
    plane = math.tan(FOV / 2)
    focal = (W / 2) / plane  # pixels per unit at distance 1, same on both axes
    img = np.zeros((H, W, 3), np.float32)
    cam = (np.arange(W) / W) * 2 - 1
    ang = heading + np.arctan(cam * plane)
    cos_rel = np.cos(ang - heading)
    rng = np.random.default_rng(3)

    for y in range(H):
        dy = y - HORIZON
        if dy == 0:
            continue
        height = EYE if dy > 0 else (WALL_H - EYE)
        row_dist = height * focal / abs(dy)
        corr = row_dist / cos_rel
        wx = POS[0] + np.cos(ang) * corr
        wy = POS[1] + np.sin(ang) * corr
        f = np.full((W, 1), falloff(row_dist))
        if dy > 0:  # smooth, slightly blotchy carpet
            n = np.sin(wx * 1.3 + np.cos(wy * 0.9) * 2) * 6 + rng.normal(0, 3, W)
            col = CARPET[None, :] + n[:, None]
        else:  # plain ceiling with long tubes running along y, every 3rd column, gaps between
            fx, fy = wx % 3, wy % 4
            tube = (fx > 1.05) & (fx < 1.95) & (fy > 1.35) & (fy < 1.75)  # wide panels in rows
            tile_line = ((wx % 1) < 0.03) | ((wy % 1) < 0.03)
            col = np.where(tile_line[:, None], CEILING * 0.94, CEILING)
            col = np.where(tube[:, None], TUBE * 1.2, col)
            f = np.where(tube[:, None], 1.0, f)
            if dead_panel is not None:
                # One panel on a bad ballast: "dead" is a grey housing, no glow;
                # "dim" is a brownish half-strike, mid-flicker.
                pid = (np.floor(wx / 3) == dead_panel[0]) & (np.floor(wy / 4) == dead_panel[1]) & tube
                if panel_state == "dead":
                    col = np.where(pid[:, None], DEAD_TUBE, col)
                    f = np.where(pid[:, None], falloff(row_dist), f)
                elif panel_state == "dim":
                    col = np.where(pid[:, None], TUBE * 0.72, col)
                if panel_ids is not None:
                    panel_ids[y] = np.where(tube, np.floor(wx / 3) * 1000 + np.floor(wy / 4), -1e9)
        img[y] = col * f + HAZE * (1 - f)

    for x in range(W):
        a = ang[x]
        dist, side, u, mx, my = cast(a)
        dist = max(dist * cos_rel[x], 0.05)
        top = HORIZON - (WALL_H - EYE) * focal / dist
        bottom = HORIZON + EYE * focal / dist
        f = falloff(dist) * (0.86 if side else 1.0)
        kind = texture_kind(mx, my)
        for y in range(max(int(top), 0), min(int(bottom), H)):
            v = (y - top) / max(bottom - top, 1)
            img[y, x] = wall_colour(kind, u, v) * f + HAZE * (1 - f)

    # Cheap-camera vignette and a darker foreground floor, both straight from the photo.
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2))
    img *= np.clip(1.08 - 0.32 * r ** 2, 0.55, 1.0)[:, :, None]
    return quantise(img)


if __name__ == "__main__":
    yaw = float(sys.argv[1]) if len(sys.argv) > 1 else 28
    out = sys.argv[2] if len(sys.argv) > 2 else f"scene_{int(yaw)}.png"
    Image.fromarray(render(yaw)).resize((W * SCALE, H * SCALE), Image.NEAREST).save(out)
    print(out)
