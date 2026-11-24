# Makes the bronze, silver and gold pack pictures from the green pack art (2026-11-24).
# Run from the repo root:  python Docs/make_pack_variants.py
# Reads Assets/UI/Menu/pack.png, pack_body.png, pack_top.png and writes Assets/Resources/UI/Packs/<kind>_<name>.png.
# The green is turned into metal by its brightness; the orange band keeps its colour in bronze and turns dark in
# silver and gold; the "RUNNER PACK" text on the band is replaced by "BRONZE PACK" etc.
import colorsys, os, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SRC = "Assets/UI/Menu/"
OUT = "Assets/Resources/UI/Packs/"
RAMPS = {
    "bronze": [(0.0, (46, 22, 8)), (0.5, (170, 98, 50)), (1.0, (246, 190, 140))],
    "silver": [(0.0, (40, 45, 54)), (0.5, (150, 158, 172)), (1.0, (246, 249, 255))],
    "gold":   [(0.0, (62, 38, 0)), (0.5, (205, 150, 24)), (1.0, (255, 240, 150))],
}
BAND = {"bronze": None, "silver": (0.0, [(0, (18, 20, 26)), (1, (90, 96, 108))]), "gold": (0.0, [(0, (28, 18, 2)), (1, (120, 84, 10))])}
TEXT = {"bronze": "BRONZE PACK", "silver": "SILVER PACK", "gold": "GOLDEN PACK"}
TEXT_COLOR = {"bronze": (252, 240, 222), "silver": (236, 241, 250), "gold": (255, 236, 150)}
BAND_ROWS = (648, 830)

def ramp(stops, v):
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if v <= b:
            t = (v - a) / (b - a) if b > a else 0
            return tuple(ca[i] + (cb[i] - ca[i]) * t for i in range(3))
    return stops[-1][1]

def recolor(im, kind):
    px = im.load()
    w, h = im.size
    out = im.copy()
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            hue = hh * 360
            lum = (0.3 * r + 0.59 * g + 0.11 * b) / 255
            if 70 <= hue <= 200 and s > 0.10:
                mix = min(1.0, (s - 0.10) / 0.2)
                c = ramp(RAMPS[kind], min(1.0, lum * 1.25))
                op[x, y] = tuple(int(c[i] * mix + (r, g, b)[i] * (1 - mix)) for i in range(3)) + (a,)
            elif BAND[kind] and BAND_ROWS[0] <= y <= BAND_ROWS[1] and (hue < 45 or hue > 340) and s > 0.45:
                c = ramp(BAND[kind][1] and [(0, BAND[kind][1][0][1]), (1, BAND[kind][1][1][1])], min(1.0, lum * 1.4))
                op[x, y] = tuple(int(v2) for v2 in c) + (a,)
    return out

def retext(im, kind):
    # Wipe the old words by copying the band's own colour from its left edge, then write the new ones
    px = im.load()
    w, h = im.size
    cover = (30, 664, 690, 790)
    slope = -0.015   # the band rises a little to the right
    for y in range(cover[1], cover[3]):
        for x in range(cover[0], cover[2]):
            sy = int(round(y - (x - 60) * slope))
            px[x, y] = px[60, sy]
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    font = None
    for path in ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"]:
        if os.path.exists(path):
            font = ImageFont.truetype(path, 74)
            break
    text = TEXT[kind]
    bbox = d.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    cx, cy = 352, 727
    d.text((cx - tw / 2 + 4, cy - 52 + 5), text, font=font, fill=(0, 0, 0, 120))
    d.text((cx - tw / 2, cy - 52), text, font=font, fill=TEXT_COLOR[kind] + (255,))
    layer = layer.rotate(-2.0, resample=Image.BICUBIC, center=(352, 727))
    im.alpha_composite(layer)
    return im

def main():
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[1:] or list(RAMPS)
    for kind in only:
        for name in ("pack", "pack_body"):
            im = Image.open(SRC + name + ".png").convert("RGBA")
            im = retext(recolor(im, kind), kind)
            im.save(OUT + kind + "_" + name + ".png")
        im = recolor(Image.open(SRC + "pack_top.png").convert("RGBA"), kind)
        im.save(OUT + kind + "_pack_top.png")

main()
