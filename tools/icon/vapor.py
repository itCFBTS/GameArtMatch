"""Vaporwave take on the road + car picture: magenta-to-orange sunset sky, a striped
sun sitting on the horizon, a neon perspective grid either side of a dark road with
cyan edges and pink centre dashes, and a hot-pink car. Variant B also recolours the
cartridge as N64 "Atomic Purple".

    python vapor.py
"""
from PIL import Image
import n64

SKY_TOP, SKY_LO = (70, 20, 110), (255, 110, 150)
SUN_HI, SUN_LO = (255, 236, 110), (255, 150, 50)  # yellow to orange: stays distinct from the pink sky
GROUND = (34, 10, 60)
GRID = (120, 40, 150)       # dim: the grid sits back so the car pops
HORIZON_GLOW = (255, 90, 190)
ROAD = (24, 14, 44)
DASH = (255, 120, 220)
CAR, CAR_DK = (255, 70, 170), (170, 30, 120)
GLASS = (200, 150, 255)  # lilac rear window — no cyan left in the palette
TYRE = (16, 8, 26)
TAIL = (255, 195, 85)  # amber, halfway between pale yellow and the sun's orange

HORIZON = 14
VP_X = 25


def sky_or_sun(x, y):
    # Striped sun half-sunk in the horizon, kept inside the sky corner the seam leaves.
    cx, r2 = 26, 17
    dx, dy = x - cx, y - HORIZON
    if dx * dx + dy * dy <= r2:
        if y == HORIZON - 2:
            pass  # the one gap stripe that fits at this size
        else:
            t = (y - (HORIZON - 4)) / 3
            return tuple(int(a + (b - a) * max(0, min(1, t))) for a, b in zip(SUN_HI, SUN_LO))
    t = max(0.0, min(1.0, (y - 9) / (HORIZON - 9)))
    return tuple(int(a + (b - a) * t) for a, b in zip(SKY_TOP, SKY_LO))


def ground(x, y):
    """Road down the middle, neon grid either side."""
    if y == HORIZON:
        return HORIZON_GLOW  # unbroken: the road's 1-px tip no longer notches the line
    t = (y - HORIZON) / (n64.BOTTOM - HORIZON)
    left, right, centre = VP_X - 17 * t, VP_X + 9 * t, VP_X - 5 * t
    if left <= x <= right:
        return ROAD  # no edge lines or centre dashes: the road reads by its darkness
    # Grid: horizontal lines bunching toward the horizon, vertical lines converging
    # on the vanishing point.
    if y in (HORIZON + 2, HORIZON + 5, HORIZON + 9):
        return GRID
    for k in (-3, 2):
        gx = VP_X + k * 5 * t
        if abs(x - gx) < 0.5:
            return GRID
    return GROUND


def car(x, y):
    if 18 <= x <= 25 and 18 <= y <= 22:
        if y == 18:
            return CAR_DK if x in (18, 25) else CAR
        if y == 19 and 20 <= x <= 23:
            return GLASS
        if y == 22 and x in (18, 19, 24, 25):
            return TYRE
        if y == 21 and x in (19, 24):
            return TAIL
        return CAR if y < 22 else CAR_DK
    return None


SHOW_CAR = True


def vapor_scene(x, y):
    if y < HORIZON:
        return sky_or_sun(x, y)
    return (car(x, y) if SHOW_CAR else None) or ground(x, y)


ATOMIC_PURPLE = dict(
    PLASTIC=(126, 92, 178), PLASTIC_LT=(170, 136, 220), PLASTIC_DK=(88, 60, 132),
    GROOVE=(60, 40, 96),
)

OPTIONS = {
    # Saved candidates — each also written as option_<key>_256.png.
    "car_amber": dict(show_car=True),
    "no_car": dict(show_car=False),
}

if __name__ == "__main__":
    n64.picture_colour = vapor_scene
    icons = {}
    for key, opt in OPTIONS.items():
        SHOW_CAR = opt["show_car"]
        globals()["SHOW_CAR"] = opt["show_car"]
        icon = n64.render()
        icon.resize((256, 256), Image.NEAREST).save(f"option_{key}_256.png")
        icon.save(f"option_{key}_32.png")
        icons[key.replace("_", " ")] = icon
    n64.sheet(icons, "vapor.png")
    print("vapor.png")
