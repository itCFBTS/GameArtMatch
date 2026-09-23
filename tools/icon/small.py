"""Taskbar-sized (16x16) simplifications of the vaporwave N64 icon, hand-placed.

A: mini cartridge — same silhouette as the 32px icon, reduced to essentials.
B: square badge — fills the whole 16x16 so it holds its own next to square icons.
Both have a no-car and a car version. Preview: a mock taskbar at 16/24/32.

    python small.py
"""
from PIL import Image, ImageDraw

PAL = {
    ".": None,
    "K": (27, 26, 24),        # outline
    "G": (150, 157, 164),     # plastic
    "g": (112, 118, 125),     # groove / recess rim / notch
    "P": (70, 20, 110),       # sky top
    "M": (170, 60, 140),      # sky mid
    "p": (255, 110, 150),     # sky low
    "Y": (255, 236, 110),     # sun
    "O": (255, 150, 50),      # sun low
    "H": (255, 90, 190),      # horizon glow
    "D": (34, 10, 60),        # ground
    "V": (120, 40, 150),      # grid line
    "C": (255, 70, 170),      # car
    "L": (200, 150, 255),     # car window
    "A": (255, 195, 85),      # amber tail light
}

# 12 rows tall (y2..13) so the margins above and below are equal (2 and 2). No wing
# notches — noise at this size. Diagonal: picture where x + y >= 15. Groove at x=3 and
# label recess from x=5: a 2px left wing, so the cartridge detail doesn't crowd right.
MINI_CART = """
................
................
..KKKKKKKKKKKK..
.KGgGGGGGGGGPPK.
KGGgGggggggYYMMK
KGGgGgGGGGYYYYMK
KGGgGgGGGpOOOOpK
KGGgGgGGHHHHHHHK
KGGgGgGDDDDDDDDK
KGGgGgVVVVVVVVVK
KGGgGDDDDDDDDDDK
KGGgDDDDDDDDDDDK
KGGDDDDDDDDDDDDK
KKKKKKKKKKKKKKKK
................
................
"""

# Same, sun removed: its pixels take the sky band of their row (magenta, magenta, pink).
MINI_CART_NO_SUN = "\n".join(
    row.replace("Y", "M") if y in (4, 5) else row.replace("O", "p") if y == 6 else row
    for y, row in enumerate(MINI_CART.strip("\n").split("\n")))

MINI_CART_CAR = """
................
................
..KKKKKKKKKKKK..
.KGGgGGGGGGGGPK.
KGGGgGggggggYYMK
KGGGgGgGGGGYYYYK
KGGGgGgGGGpOOOOK
KGGGgGgGGHHHHHHK
KGGGgGgGDDDDDDDK
KGGGgGgVVCLCVVVK
KGGGgGDDDACADDDK
KGGGgDDDDDDDDDDK
KGGGDDDDDDDDDDDK
KKKKKKKKKKKKKKKK
................
................
"""


def square(with_car):
    """Square badge from rules rather than hand-placed rows, so the diagonal is exact
    (picture where x + y >= 15): rounded 16x16 tile, groove and label-recess corner on
    the plastic side, sunset / horizon / one grid line on the picture side."""
    rows = []
    for y in range(16):
        row = ""
        for x in range(16):
            corner = (x in (0, 15)) and (y in (0, 15))
            if corner:
                row += "."
            elif x in (0, 15) or y in (0, 15):
                row += "K"
            elif x + y < 15:  # plastic
                if x == 4:
                    row += "g"
                elif (y == 4 and x >= 6) or (x == 6 and y >= 4):
                    row += "g"  # label recess: top and left rim
                else:
                    row += "G"
            else:  # picture
                if with_car and 10 <= x <= 13 and 10 <= y <= 12:
                    row += {10: "C", 11: "L" if x in (11, 12) else "C", 12: "A" if x in (10, 13) else "C"}[y]
                elif y < 8 and (x - 12) ** 2 + (y - 8) ** 2 <= 11 and y >= 5:
                    row += "O" if y == 7 else "Y"
                elif y <= 2:
                    row += "P"
                elif y <= 4:
                    row += "M"
                elif y <= 7:
                    row += "p"
                elif y == 8:
                    row += "H"
                elif y == 11:
                    row += "V"
                else:
                    row += "D"
        rows.append(row)
    return "\n".join(rows)


SQUARE = square(False)
SQUARE_CAR = square(True)


def to_image(grid):
    rows = grid.strip("\n").split("\n")
    assert len(rows) == 16 and all(len(r) == 16 for r in rows), [len(r) for r in rows]
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if PAL[ch]:
                img.putpixel((x, y), (*PAL[ch], 255))
    return img


ICONS = {
    "A mini cart": MINI_CART,
    "A mini cart + car": MINI_CART_CAR,
    "B square": SQUARE,
    "B square + car": SQUARE_CAR,
}


def taskbar_preview(icons, out):
    """Each icon in a strip of generic square 'app' placeholders, on a dark and a
    light panel, at 16 (1x), 24 (1.5x — what a 150%-scaled taskbar shows) and 32."""
    rows = []
    for bg, fg in (((32, 32, 36), (90, 90, 100)), ((238, 238, 240), (170, 170, 180))):
        for size in (16, 24, 32):
            gap = size // 2
            w = (len(icons) + 3) * (size + gap) + gap + 150
            strip = Image.new("RGB", (w, size + gap * 2), bg)
            d = ImageDraw.Draw(strip)
            d.text((6, gap), f"{size}px", fill=fg)
            x = 60
            # two placeholder app icons for scale
            for _ in range(2):
                d.rounded_rectangle([x, gap, x + size - 1, gap + size - 1], radius=max(2, size // 6), fill=fg)
                x += size + gap
            for name, icon in icons.items():
                im = icon.resize((size, size), Image.NEAREST)
                strip.paste(im, (x, gap), im)
                x += size + gap
            rows.append(strip)
    width = max(r.width for r in rows)
    sheet = Image.new("RGB", (width, sum(r.height for r in rows)), (0, 0, 0))
    y = 0
    for i, r in enumerate(rows):
        # pad each strip to full width in its own panel colour (no black gaps)
        sheet.paste(r.getpixel((0, 0)), (0, y, width, y + r.height))
        sheet.paste(r, (0, y))
        y += r.height
    sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)  # 2x so it's viewable
    sheet.save(out)


if __name__ == "__main__":
    icons = {name: to_image(grid) for name, grid in ICONS.items()}
    for name, icon in icons.items():
        key = name.lower().replace(" + ", "_").replace(" ", "_")
        icon.save(f"small_{key}_16.png")
    taskbar_preview(icons, "taskbar_preview.png")
    # big view of the four for detail
    big = Image.new("RGBA", (4 * 16 * 10 + 5 * 20, 16 * 10 + 40), (235, 235, 230, 255))
    for i, icon in enumerate(icons.values()):
        im = icon.resize((160, 160), Image.NEAREST)
        big.paste(im, (20 + i * 180, 20), im)
    big.save("small_big.png")
    print("taskbar_preview.png small_big.png")
