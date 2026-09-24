"""Backrooms Level 0 raycaster — procedural, palette-limited (Lospec Backrooms-22).

Wolfenstein-style DDA raycast of a random open-plan office maze: yellow striped
wallpaper with a dark baseboard, damp carpet, drop-ceiling tiles with fluorescent
panels, distance falloff into brown murk. Rendered small, quantised to the 22-colour
palette with 4x4 Bayer ordered dithering, then upscaled nearest-neighbour.

    python render.py [seed] [out.png]
"""
import math
import sys

import numpy as np
from PIL import Image

PALETTE_HEX = """f3f4de e9eac0 e0e0a3 d3d381 c3c07e aaa669 93885f 786c4d 655842 544738 46392f
362a24 2c211d 3d3633 5d5451 726863 8a7f79 9f948e b3a7a0 c8c0b5 d5ccbf e5ddcc""".split()
PALETTE = np.array([[int(h[i:i + 2], 16) for i in (0, 2, 4)] for h in PALETTE_HEX], dtype=np.float32)

W, H = 320, 180
SCALE = 4
FOV = math.radians(70)

WALLPAPER = np.array([212, 211, 135], np.float32)
WALLPAPER_STRIPE = np.array([190, 186, 118], np.float32)
BASEBOARD = np.array([84, 71, 56], np.float32)
CARPET = np.array([150, 138, 96], np.float32)
CEILING = np.array([229, 221, 204], np.float32)
CEILING_GRID = np.array([196, 188, 176], np.float32)
LIGHT = np.array([246, 247, 226], np.float32)
MURK = np.array([54, 42, 36], np.float32)


def build_map(rng, size=56):
    grid = np.zeros((size, size), np.uint8)
    grid[0, :] = grid[-1, :] = grid[:, 0] = grid[:, -1] = 1
    # Office partitions: random straight wall runs on a coarse lattice.
    for _ in range(size * 3):
        x, y = rng.integers(1, size - 1, 2)
        length = rng.integers(2, 7)
        if rng.random() < 0.5:
            grid[y, x:min(size - 1, x + length)] = 1
        else:
            grid[y:min(size - 1, y + length), x] = 1
    # Lone pillars.
    for _ in range(size):
        x, y = rng.integers(1, size - 1, 2)
        grid[y, x] = 1
    # A long corridor ahead of the camera with doorways off both sides, so there's
    # depth down the middle and dark openings along it.
    cx, cy = size // 2, size - 4  # size//2 is even, so the corridor's centre line carries lights
    grid[cy - 30:cy + 1, cx - 1:cx + 2] = 0
    for y in range(cy - 30, cy + 1):
        for side in (-2, 2):
            # Doorways come in runs of open cells so they read as openings, not gaps.
            grid[y, cx + side] = 0 if (y // 2) % 3 == int(rng.integers(0, 3)) else 1
    grid[cy - 31, cx - 3:cx + 4] = 1  # far end wall
    grid[cy - 31, cx] = 0  # ...with one dark opening straight ahead
    return grid, (cx + 0.5, cy + 0.5)


def cast(grid, pos, angle):
    px, py = pos
    dx, dy = math.cos(angle), math.sin(angle)
    mx, my = int(px), int(py)
    ddx = abs(1 / dx) if dx else 1e30
    ddy = abs(1 / dy) if dy else 1e30
    stx, sdx = (1, (mx + 1 - px) * ddx) if dx > 0 else (-1, (px - mx) * ddx)
    sty, sdy = (1, (my + 1 - py) * ddy) if dy > 0 else (-1, (py - my) * ddy)
    side = 0
    for _ in range(256):
        if sdx < sdy:
            sdx += ddx; mx += stx; side = 0
        else:
            sdy += ddy; my += sty; side = 1
        if grid[my, mx]:
            break
    dist = (sdx - ddx) if side == 0 else (sdy - ddy)
    hit = (py + dist * dy) if side == 0 else (px + dist * dx)
    return dist, side, hit - math.floor(hit)


def falloff(dist):
    # Fluorescent-lit near field, then a fairly quick slide into murk.
    return float(np.clip(math.exp(-dist * 0.085), 0.0, 1.0))


def render(seed):
    rng = np.random.default_rng(seed)
    grid, pos = build_map(rng)
    heading = -math.pi / 2 + rng.uniform(-0.06, 0.06)
    img = np.zeros((H, W, 3), np.float32)
    horizon = H // 2
    plane = math.tan(FOV / 2)

    # Floor and ceiling: per-row distance, per-pixel world position.
    for y in range(H):
        if y == horizon:
            continue
        row_dist = (H / 2) / abs(y - horizon) / plane * 0.95
        cam = (np.arange(W) / W) * 2 - 1
        ang = heading + np.arctan(cam * plane)
        corr = row_dist / np.cos(ang - heading)
        wx = pos[0] + np.cos(ang) * corr
        wy = pos[1] + np.sin(ang) * corr
        f = np.array([falloff(row_dist)] * W)[:, None]
        if y > horizon:  # carpet: mottled with a damp-stain noise
            n = (np.sin(wx * 7.1) * np.cos(wy * 6.3) + rng.normal(0, 0.35, W)) * 10
            col = CARPET[None, :] + n[:, None]
        else:  # drop ceiling: 1x1 tiles, a light panel every third tile
            fx, fy = wx - np.floor(wx), wy - np.floor(wy)
            grid_line = (fx < 0.04) | (fy < 0.04)
            is_light = ((np.floor(wx) % 2 == 0) & (np.floor(wy) % 3 == 0)
                        & (fx > 0.22) & (fx < 0.78) & (fy > 0.05) & (fy < 0.95))
            col = np.where(grid_line[:, None], CEILING_GRID, CEILING)
            col = np.where(is_light[:, None], LIGHT * 1.15, col)
            f = np.where(is_light[:, None], np.clip(f * 1.6, 0, 1), f)
        img[y] = col * f + MURK * (1 - f)

    # Walls.
    for x in range(W):
        cam = 2 * x / W - 1
        ang = heading + math.atan(cam * plane)
        dist, side, u = cast(grid, pos, ang)
        dist *= math.cos(ang - heading)
        dist = max(dist, 0.05)
        line_h = (H / 2) / dist / plane * 0.95 * 2
        top = int(horizon - line_h / 2)
        bottom = int(horizon + line_h / 2)
        f = falloff(dist) * (0.82 if side else 1.0)
        for y in range(max(top, 0), min(bottom, H)):
            v = (y - top) / max(line_h, 1)
            if v > 0.93:
                col = BASEBOARD
            else:
                stripe = (u * 10) % 1 < 0.14
                col = WALLPAPER_STRIPE if stripe else WALLPAPER
                col = col * (0.9 + 0.1 * (1 - v))  # ceiling lights brighten the tops
            img[y, x] = col * f + MURK * (1 - f)

    return quantise(img)


BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5)


def quantise(img):
    h, w, _ = img.shape
    dither = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w][:, :, None] * 22
    px = np.clip(img + dither, 0, 255).reshape(-1, 1, 3)
    idx = np.argmin(((px - PALETTE[None, :, :]) ** 2).sum(-1), axis=1)
    return PALETTE[idx].reshape(h, w, 3).astype(np.uint8)


if __name__ == "__main__":
    seed = int(sys.argv[1]) if len(sys.argv) > 1 else 22
    out = sys.argv[2] if len(sys.argv) > 2 else f"backrooms_{seed}.png"
    frame = render(seed)
    Image.fromarray(frame).resize((W * SCALE, H * SCALE), Image.NEAREST).save(out)
    print(out)
