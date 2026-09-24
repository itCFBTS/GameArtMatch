"""24x24 version of the vaporwave N64 icon (no car), from rules so the diagonal is
exact. Sits between the hand-placed 16px mini cart and the 32px full design:
24 wide x 16 tall cartridge (4px margins top and bottom), 3px left wing + groove +
label-recess corner on the plastic side; sky bands, round sun, horizon glow and two
grid lines on the picture side. Picture where x + y >= 23 (through the centre).

    python small24.py
"""
from PIL import Image, ImageDraw
import small

S = 24
TOP, BOTTOM = 4, 19       # outline rows
HORIZON = 11
DIAG = 23


def outline_or_inside(x, y):
    """'K' for outline, 'I' for interior, None for outside — flat top, rounded corners."""
    if y < TOP or y > BOTTOM:
        return None
    if y == TOP:
        return "K" if 2 <= x <= S - 3 else None
    if y == TOP + 1:
        if x in (1, S - 2):
            return "K"
        return "I" if 2 <= x <= S - 3 else None
    if y == BOTTOM or x in (0, S - 1):
        return "K"
    return "I"


def plastic(x, y):
    if x == 4:
        return "g"                       # groove
    if (y == 6 and x >= 6) or (x == 6 and y >= 6):
        return "g"                       # label recess: top and left rim
    return "G"


def picture(x, y):
    if y < HORIZON and (x - 18) ** 2 + (y - HORIZON) ** 2 <= 11 and y >= 8:
        return "O" if y == HORIZON - 1 else "Y"
    if y <= 6:
        return "P"
    if y <= 8:
        return "M"
    if y < HORIZON:
        return "p"
    if y == HORIZON:
        return "H"
    if y in (13, 17):
        return "V"
    return "D"


def grid():
    rows = []
    for y in range(S):
        row = ""
        for x in range(S):
            kind = outline_or_inside(x, y)
            if kind is None:
                row += "."
            elif kind == "K":
                row += "K"
            else:
                row += picture(x, y) if x + y >= DIAG else plastic(x, y)
        rows.append(row)
    return rows


def to_image(rows):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if small.PAL[ch]:
                img.putpixel((x, y), (*small.PAL[ch], 255))
    return img


if __name__ == "__main__":
    rows = grid()
    print("\n".join(rows))
    i24 = to_image(rows)
    i24.save("small_24.png")
    i16 = small.to_image(small.MINI_CART)
    i32 = Image.open("option_no_car_32.png").convert("RGBA")
    # side by side at 1x and at 6x, light and dark
    sheet = Image.new("RGBA", (40 + (16 + 24 + 32) * 6 + 60, 2 * (32 * 6 + 60)), (235, 235, 230, 255))
    for row, bg in enumerate([(235, 235, 230, 255), (30, 30, 28, 255)]):
        top = row * (32 * 6 + 60)
        panel = Image.new("RGBA", (sheet.width, 32 * 6 + 60), bg)
        x = 20
        for icon in (i16, i24, i32):
            big = icon.resize((icon.width * 6, icon.height * 6), Image.NEAREST)
            panel.paste(big, (x, 30 + (32 * 6 - big.height) // 2), big)
            x += big.width + 30
        sheet.paste(panel, (0, top))
    sheet.save("sizes_16_24_32.png")
    small.taskbar_preview({"16": i16, "24": i24, "32": i32}, "taskbar_16_24_32.png")
    print("ok")
