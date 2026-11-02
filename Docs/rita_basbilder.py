"""Draw the placeholder portraits in Assets/Resources/Basbilder (Herr_NN.png, Dam_NN.png).

Run: python Docs/rita_basbilder.py Assets/Resources/Basbilder   (needs Pillow)
Every picture is drawn from shapes in this file, so there is nobody to ask permission from.
"""
import math
import os
import sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter

W, H = 600, 900
S = 3                       # drawn three times larger, then scaled down for smooth edges
CX = 300
NECK_LIFT = 38           # how far the shoulders are moved up towards the chin
CROP = (60, 105, 540, 825)  # the part of the drawing that is kept, so the person fills the picture

SKIN = ["#f3cfb0", "#e9b993", "#d6a077", "#b98058", "#8f5d3c", "#f7dcc6", "#c99068", "#6f4630"]
HAIR = {"blond": "#c99a55", "ljus": "#e0c287", "brun": "#5e3b24", "mork": "#33221a", "svart": "#1b1614",
        "rod": "#9c4526", "gra": "#a29d96"}
EYES = {"bla": "#4f7fa8", "gron": "#5f7f4a", "brun": "#5a3a22", "mork": "#2e2018"}
JERSEY = [("#1f6b3a", "#f08a24", "#f5ead2"), ("#c8402c", "#f5ead2", "#1d2a44"), ("#1d4f8c", "#f2c230", "#ffffff"),
          ("#f08a24", "#1f4d2b", "#f5ead2"), ("#5b2a6e", "#33c2b0", "#ffffff"), ("#222831", "#e94b3c", "#f5ead2"),
          ("#0f7f86", "#f5ead2", "#123f42"), ("#e3c13b", "#1d4f8c", "#1d2a44")]


# ---------- helpers ----------

def rgb(hex_color, f=1.0, a=255):
    c = tuple(int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    return tuple(max(0, min(255, int(v * f))) for v in c) + (a,)


def mix(h1, h2, t):
    a, b = rgb(h1), rgb(h2)
    return "#%02x%02x%02x" % tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def P(points):
    return [(x * S, y * S) for x, y in points]


def spline(points, closed=True, steps=14):
    """Smooth Catmull-Rom curve through the points."""
    pts = list(points)
    n = len(pts)
    out = []
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = pts[(i - 1) % n] if closed or i > 0 else pts[0]
        p1, p2 = pts[i], pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if closed or i + 2 < n else pts[-1]
        for s in range(steps):
            t = s / steps
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2
                                    + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3) for k in (0, 1)))
    if not closed:
        out.append(pts[-1])
    return out


def mask_poly(points, smooth=True):
    m = Image.new("L", (W * S, H * S), 0)
    ImageDraw.Draw(m).polygon(P(spline(points) if smooth else points), fill=255)
    return m


def mask_ellipse(cx, cy, rx, ry):
    m = Image.new("L", (W * S, H * S), 0)
    ImageDraw.Draw(m).ellipse(P([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=255)
    return m


def paint(img, mask, color, alpha=1.0):
    layer = Image.new("RGBA", img.size, color)
    if alpha < 1:
        mask = mask.point(lambda v: int(v * alpha))
    img.paste(layer, (0, 0), mask)


def blur(mask, r):
    return mask.filter(ImageFilter.GaussianBlur(r * S))


def inside(mask, clip):
    return ImageChops.multiply(mask, clip)


def hgrad(x0, x1, a0, a1):
    """Horizontal ramp of alpha from x0 to x1."""
    row = Image.new("L", (W * S, 1))
    px = row.load()
    for x in range(W * S):
        t = min(1, max(0, (x / S - x0) / (x1 - x0)))
        px[x, 0] = int(a0 + (a1 - a0) * t)
    return row.resize((W * S, H * S))


def vgrad(y0, y1, a0, a1):
    col = Image.new("L", (1, H * S))
    px = col.load()
    for y in range(H * S):
        t = min(1, max(0, (y / S - y0) / (y1 - y0)))
        px[0, y] = int(a0 + (a1 - a0) * t)
    return col.resize((W * S, H * S))


def stroke(img, points, color, width, alpha=1.0, smooth=True):
    m = Image.new("L", img.size, 0)
    pts = spline(points, closed=False, steps=10) if smooth else points
    ImageDraw.Draw(m).line(P(pts), fill=255, width=int(width * S), joint="curve")
    for p in (pts[0], pts[-1]):
        r = width * S / 2
        ImageDraw.Draw(m).ellipse((p[0] * S - r, p[1] * S - r, p[0] * S + r, p[1] * S + r), fill=255)
    paint(img, m, color, alpha)


# ---------- the person ----------

def face_outline(woman):
    rx = 92 if woman else 98
    top, mid, chin = 168, 318, 462 if not woman else 455
    pts = []
    for i in range(0, 181, 12):            # skull, left to right over the top
        a = math.radians(180 + i)
        pts.append((CX + rx * 1.02 * math.cos(a), mid + (mid - top) * math.sin(a)))
    jaw = 0.62 if woman else 0.7
    pts += [(CX + rx, mid + 45), (CX + rx * 0.9, mid + 95), (CX + rx * jaw, chin - 28),
            (CX + 26, chin - 2), (CX - 26, chin - 2), (CX - rx * jaw, chin - 28), (CX - rx * 0.9, mid + 95),
            (CX - rx, mid + 45)]
    return pts, rx


def body(img, skin, jersey, woman):
    base, pat, trim = jersey
    sw = 236 if not woman else 214
    neck_w = 44 if not woman else 38
    # neck, shaded under the jaw
    neck = mask_poly([(CX - neck_w, 400), (CX + neck_w, 400), (CX + neck_w + 6, 560), (CX + neck_w + 30, 600),
                      (CX - neck_w - 30, 600), (CX - neck_w - 6, 560)])
    paint(img, neck, rgb(skin, 0.93))
    paint(img, inside(blur(mask_ellipse(CX, 450, 80, 40), 14), neck), rgb(skin, 0.62), 0.55)
    paint(img, inside(hgrad(CX - 40, CX + 50, 0, 120), neck), rgb(skin, 0.7), 0.5)

    # shoulders and chest in the club jersey, drawn on their own layer and lifted to shorten the neck
    tl = Image.new("RGBA", img.size, (0, 0, 0, 0))
    torso_pts = [(CX - neck_w - 8, 548), (CX - 120, 580), (CX - sw + 20, 622), (CX - sw - 18, 700), (CX - sw - 30, 940),
                 (CX + sw + 30, 940), (CX + sw + 18, 700), (CX + sw - 20, 622), (CX + 120, 580), (CX + neck_w + 8, 548),
                 (CX, 600)]
    torso = mask_poly(torso_pts)
    layer = Image.new("RGBA", tl.size, rgb(base))
    d = ImageDraw.Draw(layer)
    for k in range(-5, 6):                 # sublimated diagonal bands
        x0 = CX + k * 118
        d.polygon(P([(x0, 980), (x0 + 46, 980), (x0 + 360, 520), (x0 + 314, 520)]), fill=rgb(pat))
    zz = [(CX - 300 + i * 50, 770 if i % 2 == 0 else 735) for i in range(13)]
    d.line(P(zz), fill=rgb(trim), width=12 * S, joint="curve")
    tl.paste(layer, (0, 0), torso)
    # roundness: darker towards the arms and the bottom, light on the left shoulder
    paint(tl, inside(hgrad(CX - sw - 30, CX - sw + 70, 150, 0), torso), (0, 0, 0, 255), 0.45)
    paint(tl, inside(hgrad(CX + sw - 70, CX + sw + 30, 0, 170), torso), (0, 0, 0, 255), 0.5)
    paint(tl, inside(vgrad(760, 920, 0, 120), torso), (0, 0, 0, 255), 0.5)
    paint(tl, inside(blur(mask_ellipse(CX - 150, 630, 70, 26), 18), torso), (255, 255, 255, 255), 0.25)
    # shoulder seams
    stroke(tl, [(CX - 96, 584), (CX - 160, 640), (CX - 200, 760)], rgb(base, 0.6), 3, 0.5)
    stroke(tl, [(CX + 96, 584), (CX + 160, 640), (CX + 200, 760)], rgb(base, 0.6), 3, 0.5)
    # round collar with a trim
    collar = mask_poly([(CX - neck_w - 10, 546), (CX, 590 if not woman else 600), (CX + neck_w + 10, 546),
                        (CX + neck_w + 22, 560), (CX, 612 if not woman else 622), (CX - neck_w - 22, 560)])
    paint(tl, collar, rgb(trim))
    paint(tl, inside(blur(mask_ellipse(CX, 548, neck_w + 20, 12), 6), torso), rgb(skin, 0.5), 0.35)
    img.alpha_composite(tl.transform(tl.size, Image.AFFINE, (1, 0, 0, 0, 1, NECK_LIFT * S)))


def ears(img, skin, rx):
    for side in (-1, 1):
        x = CX + side * (rx + 2)
        paint(img, mask_ellipse(x, 335, 17, 30), rgb(skin, 0.95))
        paint(img, mask_ellipse(x + side * 2, 337, 9, 19), rgb(skin, 0.8), 0.7)


def face(img, skin, woman, eye, hair_color, look):
    outline, rx = face_outline(woman)
    ears(img, skin, rx)
    fm = mask_poly(outline)
    paint(img, fm, rgb(skin))
    # light from the upper left: shade the right side and the jaw
    paint(img, inside(hgrad(CX + 10, CX + rx + 10, 0, 110), fm), rgb(skin, 0.72), 0.55)
    paint(img, inside(vgrad(400, 465, 0, 90), fm), rgb(skin, 0.75), 0.5)
    # cheeks
    blush = "#d9786a"
    for side in (-1, 1):
        paint(img, inside(blur(mask_ellipse(CX + side * 52, 384, 26, 16), 10), fm), rgb(blush), 0.28 if woman else 0.14)

    ey = 333
    brow_c = mix(hair_color, "#2a1d16", 0.35)
    for side in (-1, 1):
        ex = CX + side * 40
        # eye socket shadow
        paint(img, inside(blur(mask_ellipse(ex, ey - 4, 26, 14), 6), fm), rgb(skin, 0.8), 0.45)
        white = Image.new("L", img.size, 0)
        ImageDraw.Draw(white).polygon(P(spline([(ex - 17, ey), (ex - 6, ey - 8 - look), (ex + 8, ey - 8 - look),
                                                (ex + 17, ey), (ex + 6, ey + 6), (ex - 6, ey + 6)])), fill=255)
        paint(img, white, rgb("#f6f1ea"))
        iris = inside(mask_ellipse(ex + 1, ey - 1, 8.5, 8.5), white)
        paint(img, iris, rgb(eye))
        paint(img, inside(mask_ellipse(ex + 1, ey - 1, 4.2, 4.2), white), rgb("#120c0a"))
        paint(img, mask_ellipse(ex - 2, ey - 4, 2.2, 2.2), (255, 255, 255, 255), 0.9)
        # upper lid and lashes
        stroke(img, [(ex - 18, ey + 1), (ex - 6, ey - 9 - look), (ex + 8, ey - 9 - look), (ex + 18, ey)],
               rgb("#2b1d17"), 3.2 if woman else 2.4)
        if woman:
            stroke(img, [(ex + side * 16, ey - 2), (ex + side * 22, ey - 7)], rgb("#2b1d17"), 2.2)
        stroke(img, [(ex - 12, ey + 9), (ex, ey + 11), (ex + 12, ey + 9)], rgb(skin, 0.78), 1.6, 0.6)
        # eyebrow, thicker for men
        stroke(img, [(ex - side * 20, ey - 20), (ex - side * 2, ey - 28), (ex + side * 20, ey - 24)],
               rgb(brow_c), 5 if woman else 7.5)

    # nose: a soft shadow down one side and under the tip
    stroke(img, [(CX + 6, 342), (CX + 9, 368), (CX + 13, 386)], rgb(skin, 0.74), 4, 0.55)
    paint(img, inside(blur(mask_ellipse(CX + 2, 393, 18, 6), 3), fm), rgb(skin, 0.7), 0.5)
    for side in (-1, 1):
        paint(img, mask_ellipse(CX + side * 9, 391, 4, 2.6), rgb(skin, 0.55), 0.7)
    paint(img, mask_ellipse(CX - 3, 378, 5, 7), (255, 255, 255, 255), 0.18)

    # mouth with a small smile
    lip = mix(skin, "#b4504a", 0.45 if woman else 0.28)
    my = 421
    upper = mask_poly([(CX - 24, my), (CX - 10, my - 6), (CX, my - 4), (CX + 10, my - 6), (CX + 24, my), (CX, my + 2)])
    lower = mask_poly([(CX - 22, my + 1), (CX, my + 3), (CX + 22, my + 1), (CX + 12, my + 11), (CX - 12, my + 11)])
    paint(img, upper, rgb(lip, 0.85))
    paint(img, lower, rgb(lip))
    stroke(img, [(CX - 25, my - 1), (CX - 10, my + 3), (CX + 10, my + 3), (CX + 25, my - 1)], rgb(lip, 0.55), 2.2)
    paint(img, mask_ellipse(CX - 4, my + 6, 6, 2.2), (255, 255, 255, 255), 0.25)
    paint(img, inside(blur(mask_ellipse(CX, my + 22, 16, 5), 4), fm), rgb(skin, 0.75), 0.4)
    return fm


# ---------- hair ----------

def hair_shapes(style, woman):
    """(behind the head, in front of the face) outlines for each hair style."""
    back, front = [], []
    cap = [(CX - 108, 330), (CX - 112, 260), (CX - 92, 196), (CX - 40, 160), (CX + 30, 156), (CX + 92, 188),
           (CX + 114, 250), (CX + 108, 330)]
    if style == "short":
        front.append(cap[:4] + [(CX, 152), (CX + 70, 170)] + cap[6:] +
                     [(CX + 98, 270), (CX + 70, 236), (CX + 20, 226), (CX - 40, 234), (CX - 86, 258), (CX - 98, 300)])
    elif style == "side":
        front.append(cap + [(CX + 100, 286), (CX + 86, 240), (CX + 30, 214), (CX - 30, 210), (CX - 70, 226),
                            (CX - 90, 270), (CX - 100, 310)])
    elif style == "buzz":
        front.append([(CX - 100, 300), (CX - 102, 240), (CX - 70, 182), (CX, 162), (CX + 70, 182), (CX + 102, 240),
                      (CX + 100, 300), (CX + 92, 250), (CX + 40, 222), (CX - 40, 222), (CX - 92, 250)])
    elif style == "curly":
        front.append("curls")
    elif style == "long":
        back.append([(CX - 120, 260), (CX - 130, 420), (CX - 140, 560), (CX - 90, 600), (CX, 590), (CX + 90, 600),
                     (CX + 140, 560), (CX + 130, 420), (CX + 120, 260), (CX, 200)])
        front.append(cap + [(CX + 104, 400), (CX + 96, 290), (CX + 70, 228), (CX + 10, 210), (CX - 10, 230),
                            (CX - 60, 222), (CX - 96, 280), (CX - 104, 400)])
    elif style == "bob":
        front.append([(CX - 124, 430), (CX - 122, 260), (CX - 92, 190), (CX - 30, 158), (CX + 40, 158), (CX + 96, 192),
                      (CX + 124, 260), (CX + 124, 430), (CX + 96, 436), (CX + 94, 300), (CX + 60, 236), (CX - 20, 244),
                      (CX - 80, 250), (CX - 98, 300), (CX - 96, 436)])
    elif style in ("pony", "bun", "braid"):
        front.append(cap[:5] + [(CX + 92, 188), (CX + 112, 250), (CX + 106, 320), (CX + 94, 262), (CX + 50, 222),
                                (CX - 10, 214), (CX - 66, 228), (CX - 96, 270), (CX - 104, 320)])
        if style == "pony":
            back.append([(CX + 70, 200), (CX + 130, 230), (CX + 152, 330), (CX + 140, 470), (CX + 116, 560),
                         (CX + 112, 450), (CX + 110, 320), (CX + 80, 240)])
        elif style == "bun":
            back.append("bun")
        else:
            back.append("braid")
    return back, front


def hair(img, style, color, woman, layer):
    back, front = hair_shapes(style, woman)
    shapes = back if layer == "back" else front
    base = rgb(color)
    for shape in shapes:
        if shape == "bun":
            m = mask_ellipse(CX + 6, 152, 54, 46)
        elif shape == "braid":
            m = Image.new("L", img.size, 0)
            for i in range(8):
                m = ImageChops.lighter(m, mask_ellipse(CX + 112 + i * 2.5, 300 + i * 40, 24 - i, 26))
        elif shape == "curls":
            m = Image.new("L", img.size, 0)
            for a in range(195, 346, 15):
                r = math.radians(a)
                m = ImageChops.lighter(m, mask_ellipse(CX + 100 * math.cos(r), 300 + 128 * math.sin(r), 34, 32))
            for a in range(150, 196, 15):
                for side in (-1, 1):
                    r = math.radians(a)
                    m = ImageChops.lighter(m, mask_ellipse(CX + side * 108 * -math.cos(r), 300 + 100 * math.sin(r) - 30, 26, 26))
        else:
            m = mask_poly(shape)
        if layer == "back":
            paint(img, m, rgb(color, 0.78))
            continue
        # soft shadow the hair casts on the forehead
        shadow = blur(m, 6).transform(m.size, Image.AFFINE, (1, 0, 0, 0, 1, -8 * S))
        paint(img, ImageChops.subtract(shadow, m), (40, 25, 20, 255), 0.22)
        paint(img, m, base)
        paint(img, inside(hgrad(CX - 20, CX + 120, 0, 120), m), rgb(color, 0.55), 0.6)
        # a sheen across the top
        sheen = inside(blur(mask_ellipse(CX - 40, 200, 60, 20), 12), m)
        paint(img, sheen, (255, 255, 255, 255), 0.22)
        # a few strands
        if style not in ("buzz", "curly"):
            for k in range(-3, 4):
                stroke(img, [(CX + k * 22, 172), (CX + k * 26 + 6, 200), (CX + k * 30 + 10, 230)],
                       rgb(color, 0.7), 2, 0.35)


def extras(img, extra, color, jersey, fm):
    if extra == "band":
        m = mask_poly([(CX - 112, 236), (CX, 222), (CX + 112, 236), (CX + 112, 262), (CX, 248), (CX - 112, 262)])
        paint(img, m, rgb(jersey[1]))
        paint(img, inside(hgrad(CX, CX + 112, 0, 120), m), (0, 0, 0, 255), 0.4)
    elif extra in ("beard", "stubble"):
        m = mask_poly([(CX - 96, 340), (CX - 88, 410), (CX - 46, 456), (CX, 470), (CX + 46, 456), (CX + 88, 410),
                       (CX + 96, 340), (CX + 80, 380), (CX + 40, 404), (CX, 400), (CX - 40, 404), (CX - 80, 380)])
        m = inside(blur(m, 3), fm)
        paint(img, m, rgb(color, 0.85), 0.9 if extra == "beard" else 0.35)
        # keep the mouth visible
        paint(img, mask_poly([(CX - 24, 421), (CX, 418), (CX + 24, 421), (CX + 12, 432), (CX - 12, 432)]),
              rgb("#a8574c"), 0.85 if extra == "beard" else 0)
    elif extra == "cap":
        crown = mask_poly([(CX - 112, 262), (CX - 110, 210), (CX - 80, 170), (CX, 150), (CX + 80, 170),
                           (CX + 110, 210), (CX + 112, 262)])
        paint(img, crown, rgb(jersey[0], 0.95))
        paint(img, inside(hgrad(CX, CX + 112, 0, 120), crown), (0, 0, 0, 255), 0.4)
        visor = mask_poly([(CX - 116, 258), (CX + 40, 250), (CX + 170, 262), (CX + 172, 280), (CX + 40, 276),
                           (CX - 116, 276)])
        paint(img, visor, rgb(jersey[0], 0.7))
    elif extra == "glasses":
        for side in (-1, 1):
            ex = CX + side * 40
            ring = mask_ellipse(ex, 333, 25, 19)
            hole = mask_ellipse(ex, 333, 21, 15)
            paint(img, ImageChops.subtract(ring, hole), rgb("#2b2b2b"))
            paint(img, hole, (220, 235, 255, 255), 0.12)
        stroke(img, [(CX - 16, 330), (CX, 326), (CX + 16, 330)], rgb("#2b2b2b"), 3)


def portrait(p, woman):
    skin, style, hcol, eye, jersey, extra, look = p
    img = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
    hair(img, style, HAIR[hcol], woman, "back")
    body(img, SKIN[skin], JERSEY[jersey], woman)
    fm = face(img, SKIN[skin], woman, EYES[eye], HAIR[hcol], look)
    if extra in ("beard", "stubble"):
        extras(img, extra, HAIR[hcol], JERSEY[jersey], fm)
    hair(img, style, HAIR[hcol], woman, "front")
    if extra in ("band", "cap", "glasses"):
        extras(img, extra, HAIR[hcol], JERSEY[jersey], fm)
    img = img.crop(tuple(v * S for v in CROP)).resize((W, H), Image.LANCZOS)
    alpha = img.split()[3]
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    shadow.putalpha(alpha.filter(ImageFilter.GaussianBlur(10)).point(lambda v: int(v * 0.3)))
    return Image.alpha_composite(shadow, img)


# skin, hair style, hair colour, eyes, jersey, extra, eye openness
MEN = [
    (0, "short", "brun", "bla", 0, None, 0), (1, "side", "blond", "gron", 1, None, 1),
    (3, "buzz", "mork", "brun", 2, "beard", 0), (5, "short", "rod", "bla", 3, "band", 1),
    (4, "curly", "svart", "mork", 4, None, 0), (2, "side", "mork", "brun", 5, "stubble", 1),
    (6, "short", "ljus", "bla", 6, "cap", 0), (7, "buzz", "svart", "mork", 7, None, 1),
    (0, "side", "gra", "bla", 2, "beard", 0), (1, "short", "blond", "gron", 0, "glasses", 1),
]
WOMEN = [
    (0, "pony", "blond", "bla", 0, None, 1), (5, "long", "brun", "gron", 1, None, 0),
    (2, "bun", "mork", "brun", 2, None, 1), (1, "braid", "rod", "gron", 3, "band", 0),
    (4, "curly", "svart", "mork", 4, None, 1), (6, "bob", "ljus", "bla", 5, None, 0),
    (3, "pony", "mork", "brun", 6, "band", 1), (7, "bun", "svart", "mork", 7, None, 0),
    (0, "long", "blond", "bla", 2, "band", 1), (5, "braid", "brun", "gron", 0, "glasses", 0),
]

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    for prefix, people, woman in (("Herr", MEN, False), ("Dam", WOMEN, True)):
        for i, p in enumerate(people, 1):
            portrait(p, woman).save(os.path.join(out, f"{prefix}_{i:02d}.png"), optimize=True)
            print(prefix, i)
