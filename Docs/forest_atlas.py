"""Paints the forest texture atlas for O-Manager's 3D world (TvAtlas).

6 x 6 tiles of 512 px; each tile has 16 px padding round a 480 px picture (the shader maps a tile's 0..1 to the
inner part). Bark tiles repeat (padding wraps), the others are cut-outs (alpha) with colour bled into the clear parts.

usage: python3 tex.py out.png [tile previews dir]
"""
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TILE, PAD = 512, 16
IN = TILE - 2 * PAD          # 480
SS = 3                       # supersampling when painting
N = 6

rng = np.random.default_rng(12345)


def R(a, b):
    return a + (b - a) * rng.random()


# ------------------------------------------------------------------ helpers

def fft_noise(size, beta, seed, aspect=(1.0, 1.0)):
    """Periodic noise with a 1/f^beta spectrum, 0..1. aspect stretches the features (x, y)."""
    g = np.random.default_rng(seed)
    w = g.normal(size=(size, size)) + 1j * g.normal(size=(size, size))
    fy = np.fft.fftfreq(size)[:, None] * aspect[1]
    fx = np.fft.fftfreq(size)[None, :] * aspect[0]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = 1.0 / f ** beta
    amp[0, 0] = 0
    n = np.real(np.fft.ifft2(w * amp))
    n = (n - n.min()) / (n.max() - n.min())
    return n


def voronoi(size, count, seed, stretch=(1.0, 1.0), warp=0.0):
    """Periodic Voronoi: distance to nearest (f1), second nearest (f2) in pixels of the stretched space, and cell id."""
    g = np.random.default_rng(seed)
    pts = g.random((count, 2)) * size
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    if warp > 0:
        xs = xs + (fft_noise(size, 1.8, seed + 500) - 0.5) * warp
        ys = ys + (fft_noise(size, 1.8, seed + 501) - 0.5) * warp
    f1 = np.full((size, size), 1e9, np.float32)
    f2 = np.full((size, size), 1e9, np.float32)
    cid = np.zeros((size, size), np.int32)
    for k, (px, py) in enumerate(pts):
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                dx = (xs - (px + ox)) * stretch[0]
                dy = (ys - (py + oy)) * stretch[1]
                d = np.sqrt(dx * dx + dy * dy)
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d))
                cid = np.where(closer, k, cid)
                f1 = np.where(closer, d, f1)
    return f1, f2, cid


def lerp(a, b, t):
    return a + (b - a) * t


def col(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def to_img(rgb, alpha=None):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    if alpha is None:
        alpha = np.full(rgb.shape[:2], 255, np.uint8)
    return Image.fromarray(np.dstack([rgb, np.clip(alpha, 0, 255).astype(np.uint8)]), 'RGBA')


# ------------------------------------------------------------------ bark (repeating)

def relief(h, strength):
    """Light from the upper left on a height field (repeating)."""
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    gy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    return 1.0 + strength * (-gx * 0.7 - gy * 0.7)


def bark_pine_low():
    s = IN
    f1, f2, cid = voronoi(s, 26, 1, (1.0, 0.33), warp=70)
    edge = f2 - f1
    plate_n = fft_noise(s, 1.6, 2, (1.0, 0.5))
    fine = fft_noise(s, 1.0, 3)
    ids = (np.sin(cid * 12.9898) * 43758.5453) % 1.0
    base = lerp(col('#6b5a4c'), col('#8a6a52'), ids[..., None])
    base = lerp(base, col('#a07a5a'), (plate_n[..., None] * 0.6))
    # plates: lighter flat tops, crusty grey patches
    grey = smooth(0.55, 0.8, fft_noise(s, 1.8, 4))
    base = lerp(base, col('#8d8780'), grey[..., None] * 0.45)
    shade = 0.82 + 0.3 * fine
    crack = smooth(1.0, 12.0 + 8.0 * fine, edge)          # 0 in the fissure
    height = crack * (0.8 + 0.2 * plate_n) + 0.15 * fft_noise(s, 1.2, 5, (1.0, 0.4))
    deep = col('#2e2018')
    base = base * 1.12
    rgb = lerp(deep, base * shade[..., None], smooth(0.0, 0.45, crack)[..., None])
    rgb *= np.clip(relief(height * 18, 0.5), 0.55, 1.5)[..., None]
    # flakes peeling in layers on the plates
    layers = smooth(0.45, 0.5, (fft_noise(s, 1.3, 6, (1.0, 0.25)) * 7) % 1.0) * crack
    rgb = lerp(rgb, rgb * 1.12 + 8, layers[..., None] * 0.5)
    return to_img(rgb)


def bark_pine_high():
    """The upper trunk of a Scots pine: orange, thin papery flakes peeling sideways."""
    s = IN
    n = fft_noise(s, 1.5, 12, (0.3, 1.0))
    base = lerp(col('#b25e32'), col('#d88a50'), n[..., None])
    img = to_img(base)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(15)
    for _ in range(900):
        x, y = g.random() * s, g.random() * s
        w, h = 8 + g.random() * 40, 2 + g.random() * 6
        t = g.random()
        c = tuple(int(v) for v in lerp(col('#c87a46'), col('#f0c08a'), t)) + (255,)
        dark = tuple(int(v) for v in lerp(col('#7a3e20'), col('#9a5530'), t)) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.ellipse([x + ox - w / 2, y + oy - h / 2 + 1.5, x + ox + w / 2, y + oy + h / 2 + 1.5], fill=dark)
                d.ellipse([x + ox - w / 2, y + oy - h / 2, x + ox + w / 2, y + oy + h / 2], fill=c)
    img = img.filter(ImageFilter.GaussianBlur(0.7))
    rgb = np.asarray(img)[..., :3].astype(np.float32)
    rgb *= (0.85 + 0.25 * fft_noise(s, 1.1, 14, (0.5, 1.0)))[..., None]
    return to_img(rgb)


def bark_spruce():
    s = IN
    f1, f2, cid = voronoi(s, 160, 21, (1.0, 0.8), warp=25)
    edge = f2 - f1
    ids = (np.sin(cid * 39.346) * 43758.5453) % 1.0
    base = lerp(col('#6c5c50'), col('#857266'), ids[..., None])
    base = lerp(base, col('#9a8f84'), smooth(0.5, 0.85, fft_noise(s, 1.7, 22))[..., None] * 0.6)
    scale = smooth(0.0, 2.5, edge)
    # each scale a little lighter at its lower edge
    rgb = lerp(col('#4a3a30'), base, scale[..., None])
    rgb *= np.clip(relief(smooth(0.0, 6.0, edge) * 6, 0.4), 0.7, 1.3)[..., None]
    rgb *= (0.86 + 0.24 * fft_noise(s, 1.0, 23))[..., None]
    # resin and lichen specks
    lich = smooth(0.78, 0.86, fft_noise(s, 1.3, 24))
    rgb = lerp(rgb, col('#9aa48a'), lich[..., None] * 0.6)
    return to_img(rgb)


def bark_birch():
    s = IN
    n = fft_noise(s, 1.5, 31, (0.4, 1.0))
    rgb = lerp(col('#e9e6dc'), col('#f6f3ea'), n[..., None])
    rgb = lerp(rgb, col('#cfc8b6'), smooth(0.55, 0.8, fft_noise(s, 1.6, 32))[..., None] * 0.5)
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(33)
    # lenticels: short dark horizontal dashes; drawn three times shifted so the tile repeats
    for _ in range(170):
        x, y = g.random() * s, g.random() * s
        w, h = 6 + g.random() * 38, 1.2 + g.random() * 2.6
        c = tuple(int(v) for v in lerp(col('#2a2622'), col('#6d655c'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.ellipse([x + ox - w / 2, y + oy - h / 2, x + ox + w / 2, y + oy + h / 2], fill=c)
    # black rough patches (diamonds) here and there
    for _ in range(5):
        x, y = g.random() * s, g.random() * s
        w, h = 18 + g.random() * 50, 8 + g.random() * 20
        c = tuple(int(v) for v in lerp(col('#2a2622'), col('#4a423a'), g.random())) + (255,)
        pts = [(x - w / 2 + g.random() * 6, y + g.random() * 3), (x - w * 0.15, y - h / 2), (x + w * 0.2, y - h * 0.35),
               (x + w / 2, y), (x + w * 0.1, y + h / 2), (x - w * 0.25, y + h * 0.3)]
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.polygon([(px + ox, py + oy) for px, py in pts], fill=c)
    # peeling paper edges: pale curls
    for _ in range(40):
        x, y = g.random() * s, g.random() * s
        w = 10 + g.random() * 40
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(x + ox, y + oy), (x + ox + w, y + oy + g.random() * 2)], fill=(250, 246, 236, 255), width=2)
    return img.filter(ImageFilter.GaussianBlur(0.6))


def bark_beech():
    s = IN
    n = fft_noise(s, 1.7, 41)
    rgb = lerp(col('#7f7f78'), col('#a3a39a'), n[..., None])
    lich = smooth(0.6, 0.75, fft_noise(s, 1.5, 42))
    rgb = lerp(rgb, col('#a9b08f'), lich[..., None] * 0.55)
    dark = smooth(0.7, 0.85, fft_noise(s, 1.4, 43, (1.0, 0.3)))
    rgb = lerp(rgb, col('#55554f'), dark[..., None] * 0.5)
    rgb *= (0.92 + 0.12 * fft_noise(s, 0.8, 44))[..., None]
    return to_img(rgb)


# ------------------------------------------------------------------ painting helpers for cut-outs

class Canvas:
    def __init__(self):
        self.size = IN * SS
        self.img = Image.new('RGBA', (self.size, self.size), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def P(self, x, y):
        # 0..1 coordinates, y up
        return (x * self.size, (1 - y) * self.size)

    def line(self, pts, width, c):
        self.d.line([self.P(*p) for p in pts], fill=c, width=max(1, int(width * SS)), joint='curve')

    def taper(self, pts, w0, w1, c0, c1=None):
        """A tapering stroke along pts (0..1 coords), width in output px."""
        c1 = c1 if c1 is not None else c0
        n = len(pts)
        for i in range(n - 1):
            t = i / max(1, n - 2)
            w = (w0 + (w1 - w0) * t) * SS
            cc = tuple(int(c0[k] + (c1[k] - c0[k]) * t) for k in range(4))
            a, b = self.P(*pts[i]), self.P(*pts[i + 1])
            self.d.line([a, b], fill=cc, width=max(1, int(round(w))))
            r = w / 2
            if r >= 1:
                self.d.ellipse([b[0] - r, b[1] - r, b[0] + r, b[1] + r], fill=cc)

    def poly(self, pts, c):
        self.d.polygon([self.P(*p) for p in pts], fill=c)

    def ellipse(self, x, y, rx, ry, c):
        a = self.P(x - rx, y + ry)
        b = self.P(x + rx, y - ry)
        self.d.ellipse([a[0], a[1], b[0], b[1]], fill=c)

    def done(self):
        img = self.img.convert('RGBa').resize((IN, IN), Image.LANCZOS).convert('RGBA')
        return img


def rgba(h, a=255, f=1.0):
    c = col(h) * f
    return tuple(int(max(0, min(255, v))) for v in c) + (a,)


def mix(h1, h2, t, a=255):
    c = lerp(col(h1), col(h2), t)
    return tuple(int(v) for v in c) + (a,)


def leaf_shape(cx, cy, length, width, angle, tip=0.5, n=14, serr=0.0):
    """Points of a leaf outline from the base (cx, cy) along angle."""
    ca, sa = math.cos(angle), math.sin(angle)
    left, right = [], []
    for i in range(n + 1):
        t = i / n
        # width profile: round base, pointed tip
        w = width * math.sin(math.pi * t ** (0.8 + tip)) * (1.0 - 0.15 * t)
        if serr > 0 and 0 < i < n:
            w *= 1.0 + (serr if i % 2 else -serr)
        px, py = t * length, w
        left.append((cx + px * ca - py * sa, cy + px * sa + py * ca))
        right.append((cx + px * ca + py * sa, cy + px * sa - py * ca))
    return left + right[::-1]


def bezier(p0, p1, p2, n=12):
    out = []
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]
        out.append((x, y))
    return out


# ------------------------------------------------------------------ foliage cards

def pine_needles():
    """A Scots pine branch end from the side/above: a fan of shoots, each a dense brush of long paired needles."""
    c = Canvas()
    g = np.random.default_rng(51)
    twig = rgba('#5e4532')
    main = bezier((0.04, 0.08), (0.4, 0.38), (0.86, 0.6), 18)
    c.taper(main, 8, 3.5, twig, rgba('#8a5a38'))
    shoots = []
    for t in (0.3, 0.42, 0.52, 0.62, 0.7, 0.78, 0.86, 0.93, 1.0):
        i = min(len(main) - 2, int(t * (len(main) - 1)))
        bx, by = main[i]
        dirm = math.atan2(main[i + 1][1] - main[i][1], main[i + 1][0] - main[i][0])
        if t >= 1.0:
            shoots.append((main[-1][0], main[-1][1], dirm, 0.24))
            continue
        side = 1 if len(shoots) % 2 else -1
        ang = dirm + side * R(0.45, 0.95)
        ln = R(0.16, 0.26) * (0.7 + 0.5 * t)
        ex, ey = bx + math.cos(ang) * ln, by + math.sin(ang) * ln
        c.taper(bezier((bx, by), (bx + math.cos(ang) * ln * 0.5, by + math.sin(ang) * ln * 0.5 + 0.02), (ex, ey), 6), 5, 2.5, twig)
        shoots.append((ex, ey, ang, ln))
    for (ex, ey, ang, ln) in shoots:
        sx, sy = ex - math.cos(ang) * ln * 0.95, ey - math.sin(ang) * ln * 0.95
        for k in range(320):
            t = g.random() ** 0.55
            px, py = lerp(sx, ex, t), lerp(sy, ey, t)
            spread = (g.random() - 0.5) * 2.6
            a = ang + spread * (0.5 + 0.7 * (1 - t))
            nl = R(0.07, 0.11) * (0.75 + 0.4 * t)
            qx, qy = px + math.cos(a) * nl, py + math.sin(a) * nl
            bend = 0.18 * (g.random() - 0.5)
            mx, my = (px + qx) / 2 - math.sin(a) * nl * bend, (py + qy) / 2 + math.cos(a) * nl * bend
            shade = g.random() * 0.7 + 0.3 * t
            c0 = mix('#223d22', '#36552f', shade)
            c1 = mix('#4d7048', '#86a67a', shade)
            c.taper(bezier((px, py), (mx, my), (qx, qy), 4), 2.6, 1.3, c0, c1)
        c.ellipse(ex, ey, 0.01, 0.012, rgba('#8a5a3a'))
    return c.done()

def spruce_spray():
    """A Norway spruce branch seen from above: flat sprays of branchlets densely clad in short needles."""
    c = Canvas()
    g = np.random.default_rng(61)
    twig = rgba('#5a4434')
    axis = bezier((0.5, 0.02), (0.47, 0.5), (0.52, 0.97), 20)
    c.taper(axis, 6, 2, twig)

    def branchlet(p0, ang, ln, depth):
        pts = []
        x, y = p0
        for i in range(8):
            t = i / 7
            a = ang + 0.25 * math.sin(t * 2.0) * (1 if ang > math.pi / 2 else -1)
            pts.append((x + math.cos(a) * ln * t, y + math.sin(a) * ln * t))
        c.taper(pts, 2.5, 1.2, twig)
        # needles all along, short, pointing forward and out
        count = int(ln * 900)
        for k in range(count):
            t = g.random()
            i = min(6, int(t * 7))
            px = lerp(pts[i][0], pts[i + 1][0], t * 7 - i)
            py = lerp(pts[i][1], pts[i + 1][1], t * 7 - i)
            side = 1 if g.random() < 0.5 else -1
            a = ang + side * R(0.5, 1.3)
            nl = R(0.016, 0.026)
            tipness = smooth(0.7, 1.0, np.float32(t))
            c0 = mix('#1e361f', '#2e4b29', g.random())
            c1 = mix('#3d5f33', '#6b8f45', float(tipness) * 0.8 + g.random() * 0.2)
            c.taper([(px, py), (px + math.cos(a) * nl, py + math.sin(a) * nl)], 1.8, 1.0, c0, c1)
        if depth == 0:
            for k in range(3):
                t = 0.3 + 0.2 * k
                i = int(t * 7)
                side = 1 if k % 2 else -1
                branchlet(pts[i], ang + side * R(0.6, 0.9), ln * R(0.35, 0.5), 1)

    for k in range(14):
        t = 0.06 + 0.86 * k / 13
        i = int(t * (len(axis) - 1))
        side = 1 if k % 2 else -1
        ln = 0.42 * math.sin(math.pi * (0.15 + 0.8 * t)) * R(0.8, 1.05)
        ang = math.pi / 2 + side * R(0.75, 1.05)
        branchlet(axis[i], ang, ln, 0)
    # the leading tip
    branchlet(axis[-4], math.pi / 2, 0.12, 1)
    return c.done()


def birch_leaves():
    """Drooping birch twigs with small toothed leaves."""
    c = Canvas()
    g = np.random.default_rng(71)
    twig = rgba('#4a3628')
    for s in range(5):
        x0 = 0.15 + 0.17 * s + R(-0.04, 0.04)
        pts = bezier((x0, 0.98), (x0 + R(-0.15, 0.15), 0.6), (x0 + R(-0.1, 0.1), R(0.05, 0.25)), 14)
        c.taper(pts, 2.5, 1.0, twig)
        for k, (px, py) in enumerate(pts[1:]):
            for side in (-1, 1):
                if g.random() < 0.25:
                    continue
                a = -math.pi / 2 + side * R(0.5, 1.2) + R(-0.3, 0.3)
                ln = R(0.06, 0.09)
                stalk = 0.012
                bx, by = px + math.cos(a) * stalk, py + math.sin(a) * stalk
                shade = g.random()
                fill = mix('#5f8a2e', '#9cbc4a', shade)
                if g.random() < 0.08:
                    fill = mix('#b8b04a', '#d8c25a', g.random())
                c.poly(leaf_shape(bx, by, ln, ln * 0.42, a, tip=0.3, n=12, serr=0.08), fill)
                # mid vein
                c.line([(bx, by), (bx + math.cos(a) * ln * 0.85, by + math.sin(a) * ln * 0.85)], 1, mix('#4a6a22', '#86a43e', shade))
    return c.done()


def broad_leaves():
    """Beech / oak / aspen leaves: a cluster of overlapping oval leaves on twigs."""
    c = Canvas()
    g = np.random.default_rng(81)
    twig = rgba('#4f3d30')
    for s in range(4):
        a0 = R(0, 2 * math.pi)
        p0 = (0.5 + 0.12 * math.cos(a0), 0.5 + 0.12 * math.sin(a0))
        a = a0 + R(-0.4, 0.4)
        p1 = (0.5 + 0.45 * math.cos(a), 0.5 + 0.45 * math.sin(a))
        pts = bezier((0.5, 0.5), p0, p1, 10)
        c.taper(pts, 3, 1.2, twig)
        for k, (px, py) in enumerate(pts[2:]):
            for side in (-1, 1):
                aa = a + side * R(0.5, 1.1)
                ln = R(0.09, 0.13)
                shade = g.random()
                fill = mix('#3f6a28', '#78a03e', shade)
                c.poly(leaf_shape(px, py, ln, ln * 0.33, aa, tip=0.2, n=16), fill)
                c.line([(px, py), (px + math.cos(aa) * ln * 0.9, py + math.sin(aa) * ln * 0.9)], 1.2, mix('#3a5a22', '#9ab85a', shade))
                # side veins
                for v in range(5):
                    t = 0.2 + 0.14 * v
                    vx, vy = px + math.cos(aa) * ln * t, py + math.sin(aa) * ln * t
                    for sd in (-1, 1):
                        va = aa + sd * 0.9
                        c.line([(vx, vy), (vx + math.cos(va) * ln * 0.12, vy + math.sin(va) * ln * 0.12)], 1, mix('#355a22', '#6a8e3a', shade, 140))
    return c.done()


def bilberry():
    """A bilberry plant from the side: green angular stems branching upwards, small oval leaves, blue berries."""
    c = Canvas()
    g = np.random.default_rng(91)
    stems = []

    def stem(x, y, ang, ln, w, depth):
        pts = [(x, y)]
        for i in range(6):
            ang += R(-0.18, 0.18)
            x += math.cos(ang) * ln / 6
            y += math.sin(ang) * ln / 6
            pts.append((x, y))
        c.taper(pts, w, w * 0.6, mix('#3e6a26', '#4f7a2c', g.random()), rgba('#6a9a38'))
        stems.append(pts)
        if depth < 2:
            for k in range(2 if depth == 0 else 1):
                i = int(R(2, 5))
                side = 1 if g.random() < 0.5 else -1
                stem(pts[i][0], pts[i][1], ang + side * R(0.4, 0.8), ln * R(0.45, 0.65), w * 0.7, depth + 1)

    for k in range(9):
        x = 0.12 + 0.76 * k / 8 + R(-0.04, 0.04)
        stem(x, 0.0, math.pi / 2 + (x - 0.5) * 0.6 + R(-0.25, 0.25), R(0.7, 0.95), 5, 0)
    leaves = []
    for pts in stems:
        for i in range(1, len(pts)):
            if g.random() < 0.15:
                continue
            px, py = pts[i]
            side = 1 if i % 2 else -1
            a = math.pi / 2 + side * R(0.6, 1.4)
            ln = R(0.05, 0.08)
            leaves.append((px, py, a, ln, g.random()))
    # berries hang under the leaves, a few per plant
    berries = []
    for pts in stems:
        if g.random() < 0.55:
            i = int(R(1, len(pts) - 1))
            px, py = pts[i]
            berries.append((px + R(-0.015, 0.015), py - R(0.02, 0.035), R(0.017, 0.023)))
    for (px, py, a, ln, sh) in leaves:
        fill = mix('#3c6a24', '#7aa63e', sh)
        if g.random() < 0.06:
            fill = mix('#8a4a2a', '#b0602e', g.random())   # an autumn red leaf
        c.poly(leaf_shape(px, py, ln, ln * 0.42, a, tip=0.15, n=12, serr=0.04), fill)
        c.line([(px, py), (px + math.cos(a) * ln * 0.8, py + math.sin(a) * ln * 0.8)], 1, mix('#3a5a20', '#a4c06a', sh, 170))
    for (px, py, r) in berries:
        c.line([(px, py + r), (px, py + r + 0.02)], 1.5, rgba('#4a5a2a'))
        c.ellipse(px, py, r, r, rgba('#1c2340'))
        c.ellipse(px - r * 0.15, py + r * 0.1, r * 0.8, r * 0.8, rgba('#2f3e6a'))
        c.ellipse(px - r * 0.3, py + r * 0.3, r * 0.35, r * 0.3, rgba('#8a9cc4', 200))     # bloom
        c.ellipse(px + r * 0.05, py - r * 0.55, r * 0.3, r * 0.22, rgba('#14121e'))        # the crown at the bottom
    return c.done()


def grass():
    """A tussock of grass blades from the side, a few seed heads and dry blades."""
    c = Canvas()
    g = np.random.default_rng(101)
    for k in range(110):
        x = 0.5 + (g.random() - 0.5) * 0.7 * g.random() ** 0.5 * 1.4
        h = R(0.45, 0.98)
        lean = (x - 0.5) * 1.2 + R(-0.25, 0.25)
        tip = (x + lean * h * 0.6, h)
        mid = (x + lean * h * 0.15, h * 0.55)
        dry = g.random() < 0.22
        c0 = mix('#3e5a24', '#5c7a30', g.random()) if not dry else mix('#7a7040', '#9a8a52', g.random())
        c1 = mix('#8aa848', '#b8c070', g.random()) if not dry else mix('#c8b878', '#e0d098', g.random())
        c.taper(bezier((x, 0.0), mid, tip, 8), R(4, 6.5), 0.8, c0, c1)
    for k in range(6):
        x = R(0.3, 0.7)
        h = R(0.85, 1.0)
        tip = (x + R(-0.12, 0.12), h)
        c.taper(bezier((x, 0.0), (x, h * 0.6), tip, 8), 2, 1.2, rgba('#7a8a48'))
        for j in range(10):
            t = 0.82 + 0.18 * j / 9
            px, py = lerp(x, tip[0], t), lerp(0, h, t)
            c.ellipse(px + R(-0.008, 0.008), py, 0.006, 0.014, rgba('#b8aa72'))
    return c.done()


def fern():
    """A fern frond from above: a stalk with separate leaflets (pinnae), each cut into small lobes, shorter to the tip."""
    c = Canvas()
    g = np.random.default_rng(111)
    stalk = bezier((0.5, 0.02), (0.54, 0.5), (0.47, 0.98), 26)
    c.taper(stalk, 4, 1.2, rgba('#5a7a30'), rgba('#7a9a40'))
    for i in range(2, len(stalk) - 1):
        t = i / (len(stalk) - 1)
        ln = 0.4 * math.sin(math.pi * min(1.0, 0.1 + t * 0.95)) * (1.05 - t * 0.45)
        px, py = stalk[i]
        for side in (-1, 1):
            a = math.pi / 2 + side * (1.25 - 0.45 * t) + R(-0.05, 0.05)
            ex, ey = px + math.cos(a) * ln, py + math.sin(a) * ln
            pts = bezier((px, py), (px + math.cos(a) * ln * 0.5, py + math.sin(a) * ln * 0.5 + 0.015), (ex, ey), 9)
            shade = g.random()
            c.taper(pts, 2.0, 0.8, mix('#4a7428', '#6a9238', shade))
            nl = len(pts)
            for j in range(1, nl):
                lt = j / nl
                ll = ln * 0.16 * (1.1 - lt) + 0.004
                qx, qy = pts[j]
                aj = math.atan2(pts[min(j, nl - 1)][1] - pts[j - 1][1], pts[min(j, nl - 1)][0] - pts[j - 1][0])
                for sd in (-1, 1):
                    aa = aj + sd * 1.0
                    c.poly(leaf_shape(qx, qy, ll, ll * 0.38, aa, tip=0.05, n=7), mix('#335f20', '#79a843', shade * 0.6 + 0.4 * lt))
    return c.done()

def heather():
    """Heather / ling from the side: a low bushy mound of wiry woody stems, tiny scale leaves, purple flower spikes."""
    c = Canvas()
    g = np.random.default_rng(121)

    def stem(x, y, a, ln, w, depth):
        pts = [(x, y)]
        for i in range(7):
            a += R(-0.2, 0.2)
            x += math.cos(a) * ln / 7
            y += math.sin(a) * ln / 7
            pts.append((x, y))
        c.taper(pts, w, w * 0.5, rgba('#4e3c2e'), rgba('#6a5a3c'))
        leafy.append(pts)
        if depth < 2:
            for k in range(3):
                i = int(R(2, 6))
                stem(pts[i][0], pts[i][1], a + (1 if g.random() < 0.5 else -1) * R(0.3, 0.7), ln * R(0.4, 0.6), w * 0.65, depth + 1)

    leafy = []
    for k in range(9):
        x = R(0.15, 0.85)
        stem(x, 0.0, math.pi / 2 + (x - 0.5) * 1.4 + R(-0.2, 0.2), R(0.55, 0.85), 4, 0)
    for pts in leafy:
        flowering = g.random() < 0.55
        for j in range(1, len(pts)):
            px, py = pts[j]
            top = j >= len(pts) - 3
            for m in range(7):
                a = math.pi / 2 + R(-1.6, 1.6)
                ln = R(0.012, 0.022)
                if flowering and top:
                    cc = mix('#7a3a6a', '#c27aaa', g.random())
                    c.ellipse(px + math.cos(a) * ln, py + math.sin(a) * ln, 0.0065, 0.0065, cc)
                else:
                    cc = mix('#34482a', '#6e7e3e', g.random())
                    c.line([(px, py), (px + math.cos(a) * ln, py + math.sin(a) * ln)], 1.6, cc)
    return c.done()

def litter():
    """The forest floor from above, cut out: pine needles, small twigs, cones, a few dead leaves."""
    c = Canvas()
    g = np.random.default_rng(131)
    # needles
    for k in range(1300):
        x, y = g.random(), g.random()
        a = g.random() * math.pi
        ln = R(0.02, 0.04)
        cc = mix('#6a4a2e', '#b08a58', g.random(), int(R(200, 255)))
        c.line([(x, y), (x + math.cos(a) * ln, y + math.sin(a) * ln)], 1.4, cc)
    # dead leaves
    for k in range(18):
        x, y = R(0.05, 0.95), R(0.05, 0.95)
        a = g.random() * 6.28
        ln = R(0.03, 0.05)
        c.poly(leaf_shape(x, y, ln, ln * 0.4, a, tip=0.3, n=10, serr=0.06), mix('#8a6a32', '#c09a4a', g.random()))
    # twigs
    for k in range(14):
        x, y = R(0.05, 0.95), R(0.05, 0.95)
        a = g.random() * 6.28
        ln = R(0.12, 0.3)
        pts = []
        for i in range(8):
            a += R(-0.15, 0.15)
            pts.append((x, y))
            x += math.cos(a) * ln / 7
            y += math.sin(a) * ln / 7
        cc = mix('#4a3a2c', '#7a6650', g.random())
        c.taper(pts, R(2.5, 4.0), 1.2, cc)
        for i in range(2, 7):
            if g.random() < 0.5:
                bx, by = pts[i]
                ba = a + (1 if g.random() < 0.5 else -1) * R(0.5, 1.0)
                bl = ln * R(0.15, 0.3)
                c.taper([(bx, by), (bx + math.cos(ba) * bl, by + math.sin(ba) * bl)], 1.6, 0.8, cc)
    # cones
    for k in range(4):
        x, y = R(0.1, 0.9), R(0.1, 0.9)
        a = g.random() * 6.28
        for j in range(16):
            t = j / 15
            px, py = x + math.cos(a) * 0.05 * t, y + math.sin(a) * 0.05 * t
            r = 0.016 * math.sin(math.pi * (0.15 + 0.8 * t)) + 0.004
            c.ellipse(px, py, r, r, mix('#4a3222', '#8a6038', (j % 3) / 2))
    return c.done()


def twig():
    """A dead stick with side twigs, seen from above, to lie on the ground."""
    c = Canvas()
    g = np.random.default_rng(141)
    x, y, a = 0.03, 0.5, R(-0.1, 0.1)
    pts = []
    for i in range(14):
        pts.append((x, y))
        a += R(-0.12, 0.12)
        x += math.cos(a) * 0.072
        y += math.sin(a) * 0.072
    c.taper(pts, 26, 9, rgba('#5e5246'), rgba('#7d7062'))
    # bark texture along the stick: darker underside and pale lichen
    for i in range(len(pts) - 1):
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        c.line([(ax, ay - 0.006), (bx, by - 0.006)], 5, rgba('#43382e', 200))
    for i in range(2, 13):
        if g.random() < 0.25:
            continue
        bx, by = pts[i]
        side = 1 if i % 2 else -1
        ba = a + side * R(0.5, 0.9)
        bl = R(0.1, 0.24) * (1.1 - i / 14)
        q = [(bx, by)]
        for j in range(4):
            ba += R(-0.2, 0.2)
            bx += math.cos(ba) * bl / 4
            by += math.sin(ba) * bl / 4
            q.append((bx, by))
        c.taper(q, 9, 2.5, rgba('#5e5246'), rgba('#857868'))
    for i in range(90):
        k = int(g.random() * 13)
        px, py = pts[k]
        c.ellipse(px + R(-0.012, 0.012), py + R(-0.01, 0.01), 0.006, 0.004, mix('#9aa080', '#c6c6a8', g.random(), 220))
    return c.done()

def dead_twigs():
    """The dead lower twigs of a spruce: a fine grey bare branch network (from the side)."""
    c = Canvas()
    g = np.random.default_rng(151)

    def branch(x, y, a, ln, w, depth):
        pts = [(x, y)]
        for i in range(6):
            a += R(-0.2, 0.2)
            x += math.cos(a) * ln / 6
            y += math.sin(a) * ln / 6
            pts.append((x, y))
        c.taper(pts, w, w * 0.5, mix('#4a423a', '#6a6258', g.random()))
        if depth < 3:
            for k in range(3):
                i = int(R(1, 6))
                side = 1 if g.random() < 0.5 else -1
                branch(pts[i][0], pts[i][1], a + side * R(0.4, 0.9), ln * R(0.35, 0.55), w * 0.6, depth + 1)

    branch(0.02, 0.55, -0.12, 0.95, 6, 0)
    branch(0.02, 0.45, 0.1, 0.8, 5, 1)
    # beard lichen
    for k in range(20):
        x, y = R(0.2, 0.9), R(0.3, 0.6)
        c.taper(bezier((x, y), (x + R(-0.02, 0.02), y - 0.04), (x + R(-0.03, 0.03), y - R(0.05, 0.1)), 5), 2, 0.8, rgba('#9aa48a', 220))
    return c.done()


def lingonberry():
    """Lingonberry from the side: short upright stems with small glossy dark oval leaves and a few red berries."""
    c = Canvas()
    g = np.random.default_rng(161)
    for k in range(14):
        x = R(0.08, 0.92)
        h = R(0.35, 0.75)
        lean = (x - 0.5) * 0.5 + R(-0.15, 0.15)
        pts = bezier((x, 0.0), (x + lean * 0.1, h * 0.5), (x + lean * h * 0.3, h), 8)
        c.taper(pts, 3.2, 1.6, rgba('#5a4a30'), rgba('#6a7a3a'))
        for j in range(2, len(pts)):
            px, py = pts[j]
            for side in (-1, 1):
                if g.random() < 0.2:
                    continue
                a = math.pi / 2 + side * R(0.5, 1.2)
                ln = R(0.05, 0.075)
                sh = g.random()
                c.poly(leaf_shape(px, py, ln, ln * 0.48, a, tip=0.05, n=10), mix('#1f4018', '#3a6428', sh))
                # the glossy light edge
                c.line([(px, py), (px + math.cos(a) * ln * 0.8, py + math.sin(a) * ln * 0.8)], 1.3, mix('#4a7a3a', '#8aac6a', sh, 160))
        if g.random() < 0.45:
            px, py = pts[-1]
            for b in range(int(R(2, 5))):
                bx, by = px + R(-0.03, 0.03), py + R(0.0, 0.04)
                r = R(0.016, 0.022)
                c.ellipse(bx, by, r, r, rgba('#8a1a1a'))
                c.ellipse(bx - r * 0.2, by + r * 0.2, r * 0.75, r * 0.75, rgba('#c42a26'))
                c.ellipse(bx - r * 0.35, by + r * 0.4, r * 0.25, r * 0.2, rgba('#f0a8a0', 220))
    return c.done()


def sedge():
    """Sedge and cotton grass of a marsh: thin stiff blades, a few white cotton heads."""
    c = Canvas()
    g = np.random.default_rng(171)
    for k in range(90):
        x = 0.5 + (g.random() - 0.5) * 0.8
        h = R(0.55, 1.0)
        lean = (x - 0.5) * 0.6 + R(-0.12, 0.12)
        dry = g.random() < 0.3
        c0 = mix('#4a5a2a', '#6a7a3a', g.random()) if not dry else mix('#8a7a48', '#a8986a', g.random())
        c1 = mix('#8a9a4a', '#a8b060', g.random()) if not dry else mix('#c8b888', '#e0d4a8', g.random())
        c.taper(bezier((x, 0), (x + lean * 0.1, h * 0.6), (x + lean * h * 0.5, h), 8), R(2.5, 4), 0.8, c0, c1)
    for k in range(7):
        x, h = R(0.25, 0.75), R(0.85, 1.0)
        tip = (x + R(-0.08, 0.08), h - 0.04)
        c.taper(bezier((x, 0), (x, h * 0.5), tip, 8), 1.6, 1.0, rgba('#6a7040'))
        for j in range(26):
            a = g.random() * 6.28
            r = R(0.0, 0.03)
            c.ellipse(tip[0] + math.cos(a) * r, tip[1] + math.sin(a) * r * 1.2 + 0.015, 0.008, 0.008, rgba('#f4f2ea', 230))
    return c.done()


def juniper():
    """A whole juniper bush on one card: a dense upright column of prickly grey-green shoots, pointed at the top."""
    c = Canvas()
    g = np.random.default_rng(181)
    for k in range(34):
        x = R(0.18, 0.82)
        top = 0.97 - abs(x - 0.5) * 1.1 + R(-0.08, 0.04)
        x2 = 0.5 + (x - 0.5) * R(0.5, 0.9)
        pts = bezier((0.5 + (x - 0.5) * 0.3, 0.0), (x, top * 0.45), (x2, top), 14)
        c.taper(pts, 3.5, 1.2, rgba('#4a3a2e'))
        for j, (px, py) in enumerate(pts[2:]):
            for m in range(9):
                a = math.pi / 2 + R(-1.9, 1.9)
                ln = R(0.035, 0.06)
                c.taper([(px, py), (px + math.cos(a) * ln, py + math.sin(a) * ln)], 3.0, 1.0,
                        mix('#2a4430', '#45624a', g.random()), mix('#6e8c72', '#94ab92', g.random()))
        if g.random() < 0.35:
            px, py = pts[int(R(5, 13))]
            c.ellipse(px + R(-0.02, 0.02), py, 0.011, 0.011, rgba('#34405a'))
            c.ellipse(px - 0.003, py + 0.003, 0.005, 0.005, rgba('#8a9ab8', 200))
    return c.done()


def flowers():
    """Forest flowers among grass: wood anemone (white), cow-wheat (yellow), a few harebells (blue)."""
    c = Canvas()
    g = np.random.default_rng(191)
    for k in range(40):
        x = R(0.05, 0.95)
        h = R(0.3, 0.8)
        c.taper(bezier((x, 0), (x + R(-0.05, 0.05), h * 0.5), (x + R(-0.08, 0.08), h), 6), 2.4, 1.0, rgba('#4a6a2a'), rgba('#6a8a3a'))
    for k in range(22):
        x, y = R(0.08, 0.92), R(0.3, 0.85)
        c.taper([(x, 0.0), (x + R(-0.03, 0.03), y)], 2.0, 1.2, rgba('#4a6a2a'))
        kind = g.random()
        if kind < 0.45:
            for p_ in range(6):
                a = p_ * 1.047 + R(-0.1, 0.1)
                c.poly(leaf_shape(x, y, 0.035, 0.016, a, tip=0.0, n=6), mix('#f6f4ee', '#e8d8e8', g.random()))
            c.ellipse(x, y, 0.008, 0.008, rgba('#e8d040'))
        elif kind < 0.8:
            for p_ in range(4):
                c.ellipse(x + R(-0.01, 0.01), y - p_ * 0.02, 0.008, 0.012, rgba('#e8c030'))
        else:
            c.poly([(x - 0.018, y), (x + 0.018, y), (x + 0.01, y - 0.04), (x - 0.01, y - 0.04)], rgba('#6a6ac8'))
    for k in range(30):
        x, y = R(0.05, 0.95), R(0.05, 0.5)
        a = math.pi / 2 + R(-1.2, 1.2)
        c.poly(leaf_shape(x, y, 0.06, 0.025, a, tip=0.2, n=8), mix('#3e6a26', '#6a9a3a', g.random()))
    return c.done()


def lichen_mask(s, seed, cover, size=1.6):
    """Crusty lichen spots: noise thresholded and broken up at the edges by fine noise, so no round blobs."""
    big = fft_noise(s, size, seed)
    fine = fft_noise(s, 0.9, seed + 1)
    m = big * 0.75 + fine * 0.25
    t = np.quantile(m, 1 - cover)
    return smooth(t, t + 0.03, m)


def granite():
    """Grey granite (boulders, cliff tops): fine grains, a few dark cracks, faint grey-green crust lichen (repeats)."""
    s = IN
    base = fft_noise(s, 1.7, 301)
    g = np.random.default_rng(302)
    grain = g.random((s, s))
    rgb = lerp(col('#6c6a65'), col('#8f8c85'), base[..., None])
    rgb *= (0.84 + 0.26 * grain)[..., None]
    feld = (g.random((s, s)) > 0.965)
    rgb = lerp(rgb, col('#b9b2a8'), feld[..., None] * 0.5)
    mica = (g.random((s, s)) > 0.98)
    rgb = lerp(rgb, col('#2c2a28'), mica[..., None] * 0.6)
    pink = smooth(0.65, 0.9, fft_noise(s, 0.8, 303))
    rgb = lerp(rgb, col('#94827a'), pink[..., None] * 0.25)
    f1, f2, _ = voronoi(s, 7, 305, warp=70)
    crack = smooth(2.0, 0.0, f2 - f1)
    rgb = lerp(rgb, col('#2e2c2a'), crack[..., None] * 0.6)
    rgb = lerp(rgb, col('#a9ab9a'), lichen_mask(s, 306, 0.07)[..., None] * 0.35)
    rgb = lerp(rgb, col('#8e8c5c'), lichen_mask(s, 308, 0.04)[..., None] * 0.35)
    rgb = lerp(rgb, col('#2f302a'), lichen_mask(s, 310, 0.05, 1.3)[..., None] * 0.3)
    return to_img(rgb)


def cliff_rock():
    """A cliff face: tall blocks split by mostly upright joints and a few ledges, each block lit as a rounded slab,
    dark water stains running down from the joints, faint crust lichen and a little moss on the ledges (repeats)."""
    s = IN
    f1, f2, cid = voronoi(s, 10, 314, (1.0, 0.33), warp=40)
    edge = f2 - f1
    joint = smooth(2.2, 0.0, edge)
    # every block its own shade
    shade = (np.random.default_rng(317).random(10)[cid] - 0.5) * 0.16
    height = smooth(0.0, 26.0, edge) + fft_noise(s, 1.9, 318) * 0.35
    lit = relief(height * 40.0, 0.03)
    bed = fft_noise(s, 1.8, 311, (0.2, 1.0))
    rgb = lerp(col('#5f5d59'), col('#86837c'), bed[..., None])
    rgb *= (1.0 + shade)[..., None]
    rgb *= (0.88 + 0.2 * np.random.default_rng(312).random((s, s)))[..., None]
    rgb *= np.clip(lit, 0.7, 1.25)[..., None]
    # stains: run down from the joints (top of the picture is up), long and thin
    st = fft_noise(s, 1.5, 313, (0.08, 1.0))
    stain = smooth(0.58, 0.8, st)
    rgb = lerp(rgb, col('#2f2e2b'), stain[..., None] * 0.45)
    rust = smooth(0.72, 0.85, fft_noise(s, 1.4, 319, (0.15, 1.0)))
    rgb = lerp(rgb, col('#6a5848'), rust[..., None] * 0.3)
    rgb = lerp(rgb, col('#1e1d1b'), joint[..., None] * 0.75)
    # ledges: the flat-ish tops of blocks (just under a joint, seen from the side), with moss
    below = np.roll(joint, -5, 0) * (1 - joint)
    moss = smooth(0.45, 0.65, fft_noise(s, 1.5, 315))
    rgb = lerp(rgb, col('#4d6530'), (below * moss)[..., None] * 0.7)
    rgb = lerp(rgb, col('#a4a796'), lichen_mask(s, 316, 0.06)[..., None] * 0.3)
    rgb = lerp(rgb, col('#2c2d27'), lichen_mask(s, 320, 0.06, 1.3)[..., None] * 0.3)
    return to_img(rgb)


def dead_wood():
    """Grey weathered wood of a fallen trunk or a stump with the bark gone: long cracks and grain (repeats)."""
    s = IN
    grain = fft_noise(s, 1.4, 321, (0.06, 1.0))
    rgb = lerp(col('#6e665c'), col('#a09486'), grain[..., None])
    cracks = smooth(0.82, 0.9, fft_noise(s, 1.2, 322, (0.04, 1.0)))
    rgb = lerp(rgb, col('#2e2822'), cracks[..., None] * 0.8)
    bark = smooth(0.6, 0.7, fft_noise(s, 1.9, 323))
    rgb = lerp(rgb, col('#4a3a2e'), bark[..., None] * 0.7)
    moss = smooth(0.62, 0.75, fft_noise(s, 1.7, 324))
    rgb = lerp(rgb, col('#5a7034'), moss[..., None] * 0.6)
    return to_img(rgb)


def path_soil():
    """A trodden path: packed brown earth, small stones, a few needles and roots (repeats)."""
    s = IN
    n = fft_noise(s, 1.6, 331)
    rgb = lerp(col('#5e4a36'), col('#857058'), n[..., None])
    rgb *= (0.85 + 0.25 * np.random.default_rng(332).random((s, s)))[..., None]
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(333)
    for _ in range(170):
        x, y, r = g.random() * s, g.random() * s, 1.5 + g.random() * 3.5
        c_ = tuple(int(v) for v in lerp(col('#6a665e'), col('#9a958a'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.ellipse([x + ox - r, y + oy - r * 0.8, x + ox + r, y + oy + r * 0.8], fill=c_)
    for _ in range(500):
        x, y = g.random() * s, g.random() * s
        a, ln = g.random() * 6.28, 6 + g.random() * 10
        c_ = tuple(int(v) for v in lerp(col('#8a6a48'), col('#b89a70'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(x + ox, y + oy), (x + ox + math.cos(a) * ln, y + oy + math.sin(a) * ln)], fill=c_, width=1)
    img = img.filter(ImageFilter.GaussianBlur(0.5))
    # Ragged edges across (u): the path strip's sides fray into the moss; a little greener and darker near them
    rgb = np.asarray(img).astype(np.float32)[..., :3]
    xs = np.mgrid[0:s, 0:s][1].astype(np.float32)
    edge = np.minimum(xs, s - 1 - xs) / s
    rag = fft_noise(s, 1.3, 334) * 0.7 + fft_noise(s, 0.7, 335) * 0.3
    limit = 0.05 + 0.22 * rag
    alpha = smooth(limit - 0.012, limit + 0.012, edge) * 255
    near = smooth(limit + 0.12, limit, edge)
    rgb = lerp(rgb, col('#4e5530'), near[..., None] * 0.45)
    return to_img(rgb, alpha)


def moss_tex():
    """Thick green moss (repeats): cushions with light tips, for the tops of stones and logs."""
    s = IN
    f1, f2, cid = voronoi(s, 120, 341, warp=20)
    cush = smooth(0.0, 18.0, f1)
    n = fft_noise(s, 1.5, 342)
    rgb = lerp(col('#4a6a26'), col('#7a9a3a'), (n * 0.6 + (1 - cush) * 0.4)[..., None])
    speck = np.random.default_rng(343).random((s, s))
    rgb = lerp(rgb, col('#a8c060'), (speck > 0.9)[..., None] * 0.6)
    rgb = lerp(rgb, col('#2a3a18'), smooth(0.7, 1.0, cush)[..., None] * 0.5)
    return to_img(rgb)



# ------------------------------------------------------------------ the town (repeating; light and fairly neutral,
# so the house's own colour, multiplied on, still shows)

def plaster():
    """Rendered wall (2 m): soft trowel marks, faint damp patches low down, a few hairline cracks, about 0.9 grey."""
    s = IN
    n = fft_noise(s, 1.9, 401) * 0.6 + fft_noise(s, 1.2, 402) * 0.4
    lum = 220 + (n - 0.5) * 40
    lum = lum * (0.94 + 0.09 * np.random.default_rng(403).random((s, s)))
    damp = smooth(0.62, 0.85, fft_noise(s, 1.6, 404, (0.4, 1.0)))
    lum = lum * (1 - damp * 0.06)
    rgb = np.dstack([lum, lum * 0.985, lum * 0.96])
    f1, f2, _ = voronoi(s, 5, 405, warp=90)
    crack = smooth(1.2, 0.0, f2 - f1) * smooth(0.55, 0.7, fft_noise(s, 1.5, 406))
    rgb = lerp(rgb, col('#8a857c'), crack[..., None] * 0.5)
    return to_img(rgb)


def brick():
    """Red brick (1.2 m): 15 courses in running bond, each brick its own shade, light mortar joints."""
    s = IN
    rows = 15
    rh = s / rows
    g = np.random.default_rng(411)
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    row = np.floor(ys / rh).astype(int)
    bw = s / 4.0
    shift = (row % 2) * bw / 2
    colx = np.floor(((xs + shift) % s) / bw).astype(int)
    bid = (row * 7 + colx) % 97
    shade = g.random(97)
    hue = g.random(97)
    base = lerp(col('#8e3f2c'), col('#b0583c'), shade[bid][..., None])
    base = lerp(base, col('#6a3a30'), (hue[bid] > 0.8)[..., None] * 0.6)
    base *= (0.9 + 0.18 * g.random((s, s)))[..., None]
    base *= (0.94 + 0.12 * fft_noise(s, 1.4, 412))[..., None]
    fy = ys - row * rh
    fx = ((xs + shift) % s) - colx * bw
    joint = np.maximum(smooth(2.6, 1.2, fy), smooth(2.6, 1.2, np.minimum(fx, bw - fx)))
    rgb = lerp(base, col('#b9b2a4'), joint[..., None])
    soot = smooth(0.6, 0.85, fft_noise(s, 1.6, 413))
    rgb = lerp(rgb, col('#3a2a24'), soot[..., None] * 0.25)
    return to_img(rgb)


def wood_panel():
    """Painted wooden boards (2 m), board and batten: 10 wide boards, narrow battens over the joints, grain."""
    s = IN
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    bw = s / 10.0
    fx = xs % bw
    grain = fft_noise(s, 1.3, 421, (0.05, 1.0))
    lum = 214 + (grain - 0.5) * 30
    bid = np.floor(xs / bw).astype(int)
    lum = lum * (0.95 + 0.08 * np.random.default_rng(422).random(11)[bid])
    batten = smooth(bw * 0.16, bw * 0.12, np.minimum(fx, bw - fx))
    edge = smooth(bw * 0.16, bw * 0.13, np.minimum(fx, bw - fx)) - smooth(bw * 0.13, bw * 0.10, np.minimum(fx, bw - fx))
    lum = lum * (1 + batten * 0.06) * (1 - np.clip(edge, 0, 1) * 0.35)
    shadow = smooth(bw * 0.22, bw * 0.16, fx) * (1 - batten)
    lum = lum * (1 - shadow * 0.15)
    rgb = np.dstack([lum, lum * 0.98, lum * 0.95])
    peel = smooth(0.78, 0.86, fft_noise(s, 1.5, 423))
    rgb = lerp(rgb, col('#8f8a80'), peel[..., None] * 0.4)
    return to_img(rgb)


def roof_tiles():
    """Clay roof tiles (2 m, the slope runs up the picture): 7 courses of rounded tiles, each overlapping the one
    below, shadows under each course; grey enough that the roof's own colour (red, black) shows."""
    s = IN
    rows, cols = 7, 8
    rh, cw = s / rows, s / cols
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    row = np.floor(ys / rh).astype(int)
    shift = (row % 2) * cw / 2
    fx = ((xs + shift) % cw) / cw
    fy = (ys - row * rh) / rh               # 0 at the top of a course (image y down: the course above overlaps)
    round_ = np.sin(fx * math.pi) ** 0.6     # the barrel of a tile
    lum = 150 + 80 * round_
    lum = lum * (1 - smooth(0.7, 1.0, fy) * 0.45)           # shadow at the bottom edge under the next course
    lum = lum * (1 - smooth(0.12, 0.0, fy) * 0.25)
    g = np.random.default_rng(431)
    tid = (row * 13 + np.floor((xs + shift) / cw).astype(int)) % 101
    lum = lum * (0.9 + 0.16 * g.random(101)[tid])
    lum = lum * (0.95 + 0.1 * fft_noise(s, 1.5, 432))
    rgb = np.dstack([lum, lum * 0.97, lum * 0.94])
    moss = smooth(0.68, 0.8, fft_noise(s, 1.6, 433)) * smooth(0.4, 0.9, fy)
    rgb = lerp(rgb, col('#6a7a3a'), moss[..., None] * 0.35)
    return to_img(rgb)


def metal_sheet():
    """Ribbed sheet metal (2 m): standing seams every 25 cm, soft dents, a little rust at the seams."""
    s = IN
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    pw = s / 8.0
    fx = xs % pw
    seam = smooth(3.0, 1.0, np.minimum(fx, pw - fx))
    lum = 205 + (fft_noise(s, 1.8, 441, (0.3, 1.0)) - 0.5) * 30
    lum = lum * (1 - seam * 0.3) + smooth(5.0, 3.0, fx) * 15
    rgb = np.dstack([lum, lum * 1.0, lum * 1.02])
    rust = smooth(0.7, 0.85, fft_noise(s, 1.5, 442, (0.2, 1.0))) * smooth(10.0, 2.0, np.minimum(fx, pw - fx))
    rgb = lerp(rgb, col('#7a5a40'), rust[..., None] * 0.4)
    return to_img(rgb)


def concrete():
    """Concrete (2 m): smooth panels with form joints, pores and rain streaks."""
    s = IN
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    lum = 200 + (fft_noise(s, 1.7, 451) - 0.5) * 30
    lum = lum * (0.95 + 0.07 * np.random.default_rng(452).random((s, s)))
    pores = np.random.default_rng(453).random((s, s)) > 0.992
    lum = lum * (1 - pores * 0.35)
    joint = np.maximum(smooth(2.0, 0.5, np.minimum(ys % (s / 2), s / 2 - ys % (s / 2))), smooth(2.0, 0.5, np.minimum(xs % (s / 2), s / 2 - xs % (s / 2))))
    lum = lum * (1 - joint * 0.3)
    streak = smooth(0.6, 0.85, fft_noise(s, 1.5, 454, (0.06, 1.0)))
    lum = lum * (1 - streak * 0.12)
    return to_img(np.dstack([lum, lum * 0.99, lum * 0.97]))


def glass():
    """A window pane (stretched over each window): the sky and the street mirrored in it, brighter at the top,
    a soft diagonal sheen, the dark room behind and a curtain at one side."""
    s = IN
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32) / s
    sky = lerp(np.array([200, 215, 225], np.float32), np.array([90, 100, 110], np.float32), smooth(0.0, 0.75, ys)[..., None])
    room = np.array([40, 42, 45], np.float32)
    rgb = lerp(sky, np.broadcast_to(room, sky.shape), smooth(0.25, 0.8, ys)[..., None] * 0.75)
    sheen = smooth(0.12, 0.0, np.abs((xs + ys * 0.6) - 0.55))
    rgb = rgb + sheen[..., None] * 70
    sheen2 = smooth(0.05, 0.0, np.abs((xs + ys * 0.6) - 0.8))
    rgb = rgb + sheen2[..., None] * 40
    curtain = smooth(0.2, 0.16, xs) * (0.85 + 0.15 * np.sin(xs * 60))
    rgb = lerp(rgb, col('#d8d0c0') * 0.8, curtain[..., None] * smooth(0.1, 0.3, ys)[..., None] * 0.7)
    return to_img(rgb)


# ------------------------------------------------------------------ assembling

def bleed(img):
    """Fills the clear pixels with the colour of the nearest painted ones, so mipmaps have no dark halos."""
    a = np.asarray(img).astype(np.float32)
    rgb, al = a[..., :3], a[..., 3:4] / 255.0
    acc = rgb * al
    w = al.copy()
    out = rgb.copy()
    from scipy.ndimage import gaussian_filter
    for sigma in (2, 6, 16, 40):
        s_acc = np.dstack([gaussian_filter(acc[..., k], sigma, mode='wrap') for k in range(3)])
        s_w = gaussian_filter(w[..., 0], sigma, mode='wrap')[..., None]
        fill = s_acc / np.maximum(s_w, 1e-6)
        need = (al < 0.5) & (s_w > 1e-4)
        out = np.where(need, fill, out)
        al_mask = (al >= 0.5)
    out = np.where(al >= 0.5, rgb, out)
    return Image.fromarray(np.dstack([np.clip(out, 0, 255), a[..., 3:4]]).astype(np.uint8), 'RGBA')


def place(atlas, img, index, repeat):
    tx, ty = index % N, index // N
    x0, y0 = tx * TILE, ty * TILE
    if repeat:
        big = Image.new('RGBA', (IN * 3, IN * 3))
        for i in range(3):
            for j in range(3):
                big.paste(img, (i * IN, j * IN))
        crop = big.crop((IN - PAD, IN - PAD, 2 * IN + PAD, 2 * IN + PAD))
        atlas.paste(crop, (x0, y0))
    else:
        img = bleed(img)
        tile = Image.new('RGBA', (TILE, TILE))
        # the padding repeats the bled edge colour, clear
        tile.paste(img.resize((TILE, TILE)).point(lambda v: v), (0, 0))
        arr = np.asarray(tile).copy()
        arr[..., 3] = 0
        tile = Image.fromarray(arr, 'RGBA')
        tile.paste(img, (PAD, PAD))
        atlas.paste(tile, (x0, y0))


TILES = [
    ('pine bark low', bark_pine_low, True),
    ('pine bark high', bark_pine_high, True),
    ('spruce bark', bark_spruce, True),
    ('birch bark', bark_birch, True),
    ('beech bark', bark_beech, True),
    ('pine needles', pine_needles, False),
    ('spruce spray', spruce_spray, False),
    ('birch leaves', birch_leaves, False),
    ('broad leaves', broad_leaves, False),
    ('bilberry', bilberry, False),
    ('grass', grass, False),
    ('fern', fern, False),
    ('heather', heather, False),
    ('litter', litter, False),
    ('twig', twig, False),
    ('dead twigs', dead_twigs, False),
    ('lingonberry', lingonberry, False),
    ('sedge', sedge, False),
    ('juniper', juniper, False),
    ('flowers', flowers, False),
    ('granite', granite, True),
    ('cliff rock', cliff_rock, True),
    ('dead wood', dead_wood, True),
    ('path soil', path_soil, True),
    ('moss', moss_tex, True),
    ('plaster', plaster, True),
    ('brick', brick, True),
    ('wood panel', wood_panel, True),
    ('roof tiles', roof_tiles, True),
    ('metal sheet', metal_sheet, True),
    ('concrete', concrete, True),
    ('glass', glass, True),
]

if __name__ == '__main__':
    out = sys.argv[1]
    prev = sys.argv[2] if len(sys.argv) > 2 else None
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None
    atlas = Image.new('RGBA', (TILE * N, TILE * N), (0, 0, 0, 0))
    for i, (name, fn, rep) in enumerate(TILES):
        img = fn()
        place(atlas, img, i, rep)
        if prev:
            bg = Image.new('RGBA', img.size, (120, 110, 90, 255))
            bg.alpha_composite(img)
            bg.convert('RGB').save(f'{prev}/{i:02d}-{name.replace(" ", "-")}.png')
        print(i, name, flush=True)
    atlas.save(out)


# ------------------------------------------------------------------ the forest floor (repeating, multiplied on the ground's colour)

def forest_floor(size=1024):
    """Moss cushions, needles, twigs and small leaves as light and dark, about 0.9 on average, with a little colour:
    greener in the moss, warmer where needles lie. Repeats; the shader lays it every 2.5 m."""
    s = size
    big = fft_noise(s, 2.0, 201)
    mid = fft_noise(s, 1.6, 202)
    fine = fft_noise(s, 0.9, 203)
    moss = smooth(0.42, 0.62, big * 0.6 + mid * 0.4)
    # moss: soft lumpy cushions
    f1, f2, cid = voronoi(256, 90, 204)
    cush = np.array(Image.fromarray((smooth(0.0, 14.0, f1) * 255).astype(np.uint8)).resize((s, s), Image.BICUBIC)) / 255.0
    lum = 0.42 + 0.34 * fine + 0.36 * (1 - cush) * moss - 0.16 * (1 - moss) * mid + 0.12 * big
    rgb = np.dstack([lum, lum, lum]) * 200
    # colour: moss a little greener, needle litter a little warmer
    rgb[..., 0] *= 1.0 - 0.06 * moss + 0.04 * (1 - moss)
    rgb[..., 1] *= 1.0 + 0.03 * moss
    rgb[..., 2] *= 1.0 - 0.06 * moss - 0.08 * (1 - moss)
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(205)
    # needles
    for _ in range(6000):
        x, y = g.random() * s, g.random() * s
        if moss[int(y) % s, int(x) % s] > 0.6 and g.random() < 0.9:
            continue
        a = g.random() * math.pi
        ln = 8 + g.random() * 14
        t = g.random()
        c = tuple(int(v) for v in lerp(col('#8a7a62'), col('#c8b89a'), t)) + (255,)
        if g.random() < 0.4:
            c = tuple(int(v) for v in lerp(col('#4a3c30'), col('#6a5a48'), t)) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(x + ox, y + oy), (x + ox + math.cos(a) * ln, y + oy + math.sin(a) * ln)], fill=c, width=2)
    # small twigs
    for _ in range(140):
        x, y = g.random() * s, g.random() * s
        a = g.random() * 6.28
        pts = []
        for i in range(6):
            pts.append((x, y))
            a += (g.random() - 0.5) * 0.4
            x += math.cos(a) * 12
            y += math.sin(a) * 12
        c = tuple(int(v) for v in lerp(col('#5a4a3a'), col('#8a7a66'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(px + ox, py + oy) for px, py in pts], fill=c, width=3)
    # dead leaves
    for _ in range(50):
        x, y = g.random() * s, g.random() * s
        a = g.random() * 6.28
        ln = 14 + g.random() * 10
        pts = leaf_shape(x, y, ln, ln * 0.4, a, tip=0.3, n=8)
        c = tuple(int(v) for v in lerp(col('#a08a60'), col('#c8b080'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.polygon([(px + ox, py + oy) for px, py in pts], fill=c)
    # moss tips: tiny light green specks on the cushions
    arr = np.asarray(img).astype(np.float32)
    speck = (g.random((s, s)) < 0.08) * moss
    arr[..., 0] = lerp(arr[..., 0], 225, speck * 0.45)
    arr[..., 1] = lerp(arr[..., 1], 240, speck * 0.45)
    arr[..., 2] = lerp(arr[..., 2], 200, speck * 0.45)
    img = Image.fromarray(arr.astype(np.uint8), 'RGBA').filter(ImageFilter.GaussianBlur(0.5))
    # keep the average near 0.9 so the ground's colour stays as it was
    arr = np.asarray(img).astype(np.float32)
    m = arr[..., :3].mean()
    arr[..., :3] *= 0.9 * 255 / m
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGBA').convert('RGB')


if __name__ == '__main__' and len(sys.argv) > 4:
    forest_floor().save(sys.argv[4])
