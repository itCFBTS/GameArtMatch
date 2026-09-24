"""N64-cartridge take on the diagonal-split icon, 32x32 pixel art, drawn per pixel.

Cartridge anatomy (from a blank N64 cart): ~1.5:1 wide, a flat top with rounded
outer corners, flat bottom; a centre panel between two
wings, split by vertical grooves; an L-shaped notch at each wing's bottom-outer corner;
a rounded label recess debossed into the same grey (shadowed top/left rim, lit
bottom/right rim) — a blank cartridge, which is the point: the other half is its art.

The F2 idea is kept: cartridge top-left, its picture bottom-right, a yellow seam on the
diagonal between them.

    python n64.py
"""
import math
from PIL import Image, ImageDraw

N = 32
OUTLINE = (27, 26, 24)
PLASTIC = (150, 157, 164)
PLASTIC_LT = (186, 192, 198)
PLASTIC_DK = (112, 118, 125)
GROOVE = (78, 83, 90)
LABEL_BEVEL = (96, 102, 109)
LABEL = (232, 230, 227)
SKY_TOP, SKY_BOT = (79, 216, 235), (150, 232, 242)
SUN = (255, 201, 77)
HILL, HILL_DK = (167, 139, 250), (112, 84, 204)
SEAM = None  # no seam line: a hard cut between cart and art (chosen over white/pink/yellow/lilac)

X0, X1 = 1, 30          # cartridge extents
BOTTOM = 26
WING_L, WING_R = 8, 23  # groove columns
LABEL_BOX = (10, 10, 21, 23)
SEAM_D = 31             # x + y on the seam


def top_edge(x):
    """y of the first plastic row at column x: flat across the whole top, outer
    corners rounded."""
    edge = min(x - X0, X1 - x)
    return 8 + {0: 2, 1: 1}.get(edge, 0)


def inside(x, y):
    return X0 <= x <= X1 and top_edge(x) <= y <= BOTTOM


def label_inside(x, y):
    lx0, ly0, lx1, ly1 = LABEL_BOX
    if not (lx0 <= x <= lx1 and ly0 <= y <= ly1):
        return False
    # rounded corners: drop the corner pixel
    return not ((x in (lx0, lx1)) and (y in (ly0, ly1)))


def cart_colour(x, y):
    top = top_edge(x)
    if x in (WING_L, WING_R):
        return GROOVE
    if label_inside(x, y):
        # Debossed recess in the same grey: its top/left rim in shadow, bottom/right
        # rim catching the light — a blank cartridge, no sticker.
        if not label_inside(x - 1, y) or not label_inside(x, y - 1):
            return PLASTIC_DK
        if not label_inside(x + 1, y) or not label_inside(x, y + 1):
            return PLASTIC_LT
        return PLASTIC
    # Wing feet: an L-shaped notch at each wing's bottom-outer corner — a short
    # horizontal crease meeting a vertical line partway across the wing.
    if y == 21 and (X0 < x <= 3 or 28 <= x < X1):
        return PLASTIC_DK
    if y >= 21 and y < BOTTOM and x in (4, 27):
        return PLASTIC_DK
    if y == top:
        return PLASTIC_LT                         # top highlight along the arc
    if x == X1 - 1:
        return PLASTIC_DK                         # shade down the right edge (no bottom shade line)
    return PLASTIC


def picture_colour(x, y):
    t = (y - 4) / (BOTTOM - 4)
    sky = tuple(int(a + (b - a) * t) for a, b in zip(SKY_TOP, SKY_BOT))
    if (x - 25) ** 2 + (y - 13) ** 2 <= 3:
        return SUN
    h1 = BOTTOM - 1 - max(0, 5 - ((x - 14) ** 2) / 9)
    h2 = BOTTOM - 1 - max(0, 7 - ((x - 25) ** 2) / 10)
    if y >= h2:
        return HILL_DK
    if y >= h1:
        return HILL
    return sky


def render(seam_d=SEAM_D):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    for y in range(N):
        for x in range(N):
            if not inside(x, y):
                continue
            edge = any(not inside(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if edge:
                c = OUTLINE
            elif SEAM is not None and x + y == seam_d:
                c = SEAM
            elif x + y > seam_d:
                c = picture_colour(x, y)
            else:
                c = cart_colour(x, y)
            img.putpixel((x, y), (*c, 255))
    return img


def sheet(icons, out):
    pad, label_h = 18, 20
    sizes = [32, 64, 256]
    col_w = sum(sizes) + pad * (len(sizes) + 1)
    row_h = 256 + pad * 2 + label_h
    s = Image.new("RGB", (col_w * 2, row_h * len(icons)))
    d = ImageDraw.Draw(s)
    for r, (name, icon) in enumerate(icons.items()):
        for b, bg in enumerate([(30, 30, 28), (235, 235, 230)]):
            left, top = b * col_w, r * row_h
            d.rectangle([left, top, left + col_w - 1, top + row_h - 1], fill=bg)
            d.text((left + pad, top + 4), name, fill=(200, 200, 200) if b == 0 else (60, 60, 60))
            x = left + pad
            for size in sizes:
                im = icon.resize((size, size), Image.NEAREST)
                s.paste(im, (x, top + label_h + pad + (256 - size) // 2), im)
                x += size + pad
    s.save(out)


if __name__ == "__main__":
    icons = {
        "N64 split (seam through the label)": render(),
        "N64 cartridge only (no split) - shape check": render(seam_d=999),
    }
    for name, icon in icons.items():
        icon.resize((256, 256), Image.NEAREST).save(("n64_split" if "split" in name.split()[1] else "n64_plain") + "_256.png")
    sheet(icons, "n64_concepts.png")
    print("n64_concepts.png")
