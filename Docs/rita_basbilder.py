"""Draw the faceless placeholder portraits in Assets/Resources/Basbilder (Herr_NN.png, Dam_NN.png).

Run: python Docs/rita_basbilder.py Assets/Resources/Basbilder   (needs Pillow)
All pictures are drawn from shapes here, so there is nothing to ask permission for."""
import math
import os
import sys
from PIL import Image, ImageDraw, ImageFilter

W, H = 600, 900
S = 3  # supersampling
OUT = sys.argv[1]

SKIN = ["#f2c9a5", "#e8b58c", "#d39a6c", "#b97a4d", "#8d5a36", "#f6d6bd", "#c98e62", "#6e4428"]
HAIR = {"blond": "#d9b36a", "ljus": "#e8cf94", "brun": "#6b4428", "mork": "#3a2618", "svart": "#1c1714",
        "rod": "#a5482a", "gra": "#9a958c"}
# (base, pattern, collar) jersey colours, all forest/orienteering-ish
JERSEY = [("#1f6b3a", "#f08a24", "#f5ead2"), ("#c8402c", "#f5ead2", "#1d2a44"), ("#1d4f8c", "#f2c230", "#ffffff"),
          ("#f08a24", "#1f4d2b", "#f5ead2"), ("#5b2a6e", "#33c2b0", "#ffffff"), ("#222831", "#e94b3c", "#f5ead2"),
          ("#0f7f86", "#f5ead2", "#123f42"), ("#e3c13b", "#1d4f8c", "#1d2a44")]


def P(*v):
    return tuple(int(x * S) for x in v)


def ell(d, cx, cy, rx, ry, fill):
    d.ellipse(P(cx - rx, cy - ry, cx + rx, cy + ry), fill=fill)


def shade(hex_color, f):
    c = tuple(int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    return tuple(max(0, min(255, int(v * f))) for v in c) + (255,)


def body(img, skin, jersey, slim):
    d = ImageDraw.Draw(img)
    base, pat, collar = jersey
    sw = 250 if not slim else 225          # half shoulder width
    top = 560
    # neck
    d.rounded_rectangle(P(300 - 46, 410, 300 + 46, 590), radius=40 * S, fill=shade(skin, 0.88))
    # shoulders / torso as a rounded shape
    torso = Image.new("L", img.size, 0)
    t = ImageDraw.Draw(torso)
    t.rounded_rectangle(P(300 - sw, top, 300 + sw, H + 200), radius=150 * S, fill=255)
    jersey_layer = Image.new("RGBA", img.size, shade(base, 1.0))
    jd = ImageDraw.Draw(jersey_layer)
    # sublimated orienteering pattern: diagonal bands and a zigzag
    for k in range(-4, 6):
        x0 = 300 + k * 120
        jd.polygon(P(x0, H + 50, x0 + 50, H + 50, x0 + 330, top - 60, x0 + 280, top - 60), fill=shade(pat, 1.0))
    zz = []
    for i in range(0, 13):
        zz += [300 - 330 + i * 55, (760 if i % 2 == 0 else 720)]
    jd.line(P(*zz), fill=shade(collar, 1.0), width=14 * S, joint="curve")
    # darker sides for some depth
    grad = Image.new("L", img.size, 0)
    gd = ImageDraw.Draw(grad)
    for i in range(60):
        a = int(110 * (1 - i / 60))
        gd.line(P(300 - sw + i, top, 300 - sw + i, H), fill=a, width=S)
        gd.line(P(300 + sw - i, top, 300 + sw - i, H), fill=a, width=S)
    dark = Image.new("RGBA", img.size, (0, 0, 0, 255))
    jersey_layer = Image.composite(dark, jersey_layer, grad.point(lambda v: v // 2))
    img.paste(jersey_layer, (0, 0), torso)
    # collar
    d = ImageDraw.Draw(img)
    d.polygon(P(300 - 70, top - 4, 300 + 70, top - 4, 300, top + 90), fill=shade(collar, 1.0))
    d.polygon(P(300 - 50, top - 4, 300 + 50, top - 4, 300, top + 66), fill=shade(skin, 0.85))


def head(d, skin):
    ell(d, 300 - 100, 335, 20, 32, shade(skin, 0.92))   # ears
    ell(d, 300 + 100, 335, 20, 32, shade(skin, 0.92))
    ell(d, 300, 315, 102, 128, shade(skin, 1.0))
    # soft jaw shadow
    d.chord(P(300 - 100, 230, 300 + 100, 443), 20, 160, fill=shade(skin, 0.95))


def hair_back(d, style, hc):
    c = shade(hc, 1.0)
    if style == "long":
        d.rounded_rectangle(P(300 - 128, 230, 300 + 128, 600), radius=110 * S, fill=shade(hc, 0.85))
    elif style == "pony":
        d.polygon(P(380, 230, 450, 270, 470, 420, 430, 560, 400, 420, 360, 260), fill=shade(hc, 0.85))
    elif style == "braid":
        for i in range(6):
            ell(d, 300 + 112 + i * 3, 300 + i * 48, 26 - i * 1.5, 30, shade(hc, 0.82 if i % 2 else 0.92))
    elif style == "bun":
        ell(d, 300, 168, 58, 50, c)
    elif style == "curly":
        for a in range(0, 360, 30):
            r = math.radians(a)
            ell(d, 300 + 108 * math.cos(r), 290 + 112 * math.sin(r) - 10, 44, 44, c)


def hair_front(d, style, hc):
    c = shade(hc, 1.0)
    if style in ("short", "side", "pony", "bun", "braid", "long", "bob", "curly", "buzz"):
        if style == "buzz":
            d.chord(P(300 - 104, 186, 300 + 104, 420), 180, 360, fill=shade(hc, 0.9))
            return
        d.chord(P(300 - 110, 176, 300 + 110, 430), 175, 365, fill=c)
    if style == "short":
        d.polygon(P(300 - 104, 290, 300 - 60, 230, 300 + 20, 222, 300 + 104, 285, 300 + 100, 230, 300, 172, 300 - 100, 228), fill=c)
    elif style == "side":
        d.polygon(P(300 - 106, 300, 300 - 90, 220, 300 + 30, 180, 300 + 108, 250, 300 + 108, 300, 300 + 40, 238, 300 - 50, 248), fill=c)
    elif style == "bob":
        d.rounded_rectangle(P(300 - 124, 230, 300 - 82, 430), radius=20 * S, fill=c)
        d.rounded_rectangle(P(300 + 82, 230, 300 + 124, 430), radius=20 * S, fill=c)
    elif style == "long":
        d.polygon(P(300 - 112, 300, 300 - 30, 214, 300 + 112, 300, 300 + 110, 230, 300, 176, 300 - 110, 230), fill=c)
        d.rounded_rectangle(P(300 - 126, 260, 300 - 90, 520), radius=18 * S, fill=c)
        d.rounded_rectangle(P(300 + 90, 260, 300 + 126, 520), radius=18 * S, fill=c)
    elif style in ("pony", "bun", "braid"):
        d.polygon(P(300 - 106, 290, 300 - 40, 226, 300 + 60, 224, 300 + 106, 290, 300 + 104, 228, 300, 176, 300 - 104, 228), fill=c)
    elif style == "curly":
        for a in range(190, 350, 22):
            r = math.radians(a)
            ell(d, 300 + 96 * math.cos(r), 300 + 112 * math.sin(r), 34, 34, c)


def extras(d, extra, hc, jersey):
    if extra == "band":
        d.rounded_rectangle(P(300 - 106, 236, 300 + 106, 268), radius=12 * S, fill=shade(jersey[1], 1.0))
    elif extra == "beard":
        d.chord(P(300 - 100, 250, 300 + 100, 446), 15, 165, fill=shade(hc, 0.9))
        d.chord(P(300 - 70, 300, 300 + 70, 412), 20, 160, fill=None)
    elif extra == "cap":
        d.chord(P(300 - 112, 170, 300 + 112, 380), 180, 360, fill=shade(jersey[0], 0.9))
        d.rounded_rectangle(P(300 - 112, 262, 300 + 160, 286), radius=12 * S, fill=shade(jersey[0], 0.7))


def portrait(skin, hair_style, hair_color, jersey, extra=None, slim=False):
    img = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    hair_back(d, hair_style, HAIR[hair_color])
    body(img, skin, jersey, slim)
    d = ImageDraw.Draw(img)
    head(d, skin)
    hair_front(d, hair_style, HAIR[hair_color])
    if extra:
        extras(d, extra, HAIR[hair_color], jersey)
    img = img.resize((W, H), Image.LANCZOS)
    # soft contact shadow so the figure sits on the card
    alpha = img.split()[3]
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    glow.putalpha(alpha.filter(ImageFilter.GaussianBlur(10)).point(lambda v: v * 0.35))
    return Image.alpha_composite(glow, img)


MEN = [
    (0, "short", "brun", 0, None), (1, "side", "blond", 1, None), (3, "buzz", "mork", 2, "beard"),
    (5, "short", "rod", 3, "band"), (4, "curly", "svart", 4, None), (2, "side", "mork", 5, None),
    (6, "short", "ljus", 6, "cap"), (7, "buzz", "svart", 7, None), (0, "side", "gra", 2, "beard"),
    (1, "short", "blond", 0, "band"),
]
WOMEN = [
    (0, "pony", "blond", 0, None), (5, "long", "brun", 1, None), (2, "bun", "mork", 2, None),
    (1, "braid", "rod", 3, "band"), (4, "curly", "svart", 4, None), (6, "bob", "ljus", 5, None),
    (3, "pony", "mork", 6, "band"), (7, "bun", "svart", 7, None), (0, "long", "blond", 2, "band"),
    (5, "braid", "brun", 0, None),
]

os.makedirs(OUT, exist_ok=True)
for prefix, items, slim in (("Herr", MEN, False), ("Dam", WOMEN, True)):
    for i, (s, style, hc, j, ex) in enumerate(items, 1):
        portrait(SKIN[s], style, hc, JERSEY[j], ex, slim).save(os.path.join(OUT, f"{prefix}_{i:02d}.png"), optimize=True)
print("ok")
