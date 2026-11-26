"""Paints the forest texture atlas for O-Manager's 3D world (TvAtlas).

7 x 7 tiles of 512 px; each tile has 16 px padding round a 480 px picture (the shader maps a tile's 0..1 to the
inner part). Bark tiles repeat (padding wraps), the others are cut-outs (alpha) with colour bled into the clear parts.

usage: python3 tex.py out.png [tile previews dir]
"""
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TILE, PAD = 512, 16
IN = TILE - 2 * PAD          # 480
U = 1                        # pixels per pixel of the normal atlas (2 for the sharp bark atlas, --hd)
SS = 3                       # supersampling when painting
N = 7

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


def band_noise(size, f0, seed, aspect=(1.0, 1.0), width=0.5):
    """Periodic noise with features of one size: about f0 waves across the picture. Smooth, so its zero lines
    are long unbroken curves (good for bark cracks). aspect stretches like fft_noise."""
    g = np.random.default_rng(seed)
    w = g.normal(size=(size, size)) + 1j * g.normal(size=(size, size))
    fy = np.fft.fftfreq(size, 1.0 / size)[:, None] * aspect[1]
    fx = np.fft.fftfreq(size, 1.0 / size)[None, :] * aspect[0]
    f = np.sqrt(fx * fx + fy * fy)
    amp = np.exp(-((np.log(np.maximum(f, 1e-6)) - np.log(f0)) / width) ** 2)
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


def bark_pine_low(seed=0):
    """Old Scots pine bark after Anders' photo (2026-11-25, round 6): a mosaic of uneven, scaly plates (about 4-12 cm,
    taller than wide) with short cracks round each one, running every way, not long lines up the trunk. Some plates
    are grey and weathered, some show the warmer red-brown under-bark, some carry pale lichen. A picture is 0.4 m round
    and 0.8 m up the trunk (fewer repeats); two versions (seed) so the trees differ."""
    s = IN
    from scipy.ndimage import gaussian_filter
    k = seed * 100
    fine = fft_noise(s, 1.0, 8 + k)   # (2026-11-25: 0.6 was nearly white noise: specks up close)
    wob = fft_noise(s, 1.3, 11 + k)
    # big plates: stretched, warped cells; small plates inside some of them, only partly cut
    f1, f2, cid = voronoi(s, 220, 9 + k, (1.0, 0.72), warp=30 * U)
    g1, g2, gid = voronoi(s, 420, 19 + k, (1.0, 1.2), warp=12 * U)
    f1, f2, g1, g2 = f1 / U, f2 / U, g1 / U, g2 / U
    jag = (fft_noise(s, 0.9, 25 + k) - 0.5) * 2.2
    width = 0.7 + 2.6 * wob ** 1.5
    edge_big = (f2 - f1 + jag) / width
    keep = smooth(0.55, 0.7, fft_noise(s, 1.2, 13 + k))
    edge_small = (g2 - g1 + jag * 0.6) / (0.5 + 0.8 * wob) + (1.0 - keep) * 6.0
    # cracks across a plate are fainter than the ones beside it; some edges hardly show (plates grown together)
    soft = smooth(0.5, 0.75, fft_noise(s, 1.3, 26 + k))
    edge_big = edge_big + soft * 2.5 + 1.2 * ((np.sin(cid * 3.71 + 0.2) * 5419.3) % 1.0 > 0.6)
    # cracks up the trunk (edges standing upright) are wider and deeper than the ones across
    gy, gx = np.gradient(gaussian_filter(f2 - f1, 2.0 * U, mode='wrap'))
    upright = np.abs(gx) / (np.sqrt(gx * gx + gy * gy) + 1e-6)
    edge_big = edge_big / (0.45 + 1.4 * upright ** 2)
    dist = np.minimum(edge_big, edge_small * 1.3)
    crack = np.maximum(1.0 - smooth(0.4, 1.4, edge_big), (1.0 - smooth(0.4, 1.2, edge_small)) * 0.55)
    # each plate is a slightly bulging, crusty scale
    crust = fft_noise(s, 0.9, 43 + k)
    height = np.clip(smooth(0.4, 7.0, dist) * (0.75 + 0.25 * crust) + 0.16 * (fine - 0.5), 0, 1)
    height = gaussian_filter(height, 0.6 * U, mode='wrap')
    ids = (np.sin(cid * 12.9898 + 0.7 * seed) * 43758.5453) % 1.0
    sid = (np.sin(gid * 78.233 + 0.3 * seed) * 12543.531) % 1.0
    tone = ids * 0.5 + sid * 0.3 + fft_noise(s, 1.5, 3 + k) * 0.2
    plate = lerp(col('#8c7c6e'), col('#bcae9e'), tone[..., None])
    # red-brown under-bark where a scale has fallen off, grey weathered plates, pale lichen
    red = smooth(0.66, 0.88, sid * 0.7 + fft_noise(s, 1.0, 2 + k) * 0.3)
    plate = lerp(plate, col('#a8765a'), red[..., None] * 0.55)
    grey = smooth(0.55, 0.8, fft_noise(s, 1.6, 4 + k))
    plate = lerp(plate, col('#aeaaa4'), grey[..., None] * 0.5)
    deep = lerp(col('#46322a'), col('#6a4e3e'), fft_noise(s, 1.3, 6 + k)[..., None])
    rgb = lerp(deep, plate, (1.0 - crack)[..., None]) * (0.82 + 0.18 * height)[..., None]
    rgb *= (0.85 + 0.26 * fine)[..., None]
    # small dark pits and crumbs on the plates
    rgb *= (1.0 - 0.35 * smooth(0.72, 0.9, fft_noise(s, 0.4, 28 + k)))[..., None]
    # bigger patches across the trunk: some greyer and lighter, some darker brown
    patch = fft_noise(s, 2.0, 27 + k)
    rgb = lerp(rgb, rgb * np.array([1.08, 1.08, 1.1]), smooth(0.55, 0.75, patch)[..., None])
    rgb *= (1.0 - 0.18 * smooth(0.55, 0.25, patch))[..., None]
    rgb *= np.clip(relief(height * 9 * U, 0.5), 0.72, 1.25)[..., None]
    lich = lichen_mask(s, 7 + k, 0.035, 1.4)
    rgb = lerp(rgb, col('#a8ac98'), (lich * smooth(0.3, 0.6, height))[..., None] * 0.45)
    return to_img(rgb)


def bark_pine_low2():
    return bark_pine_low(1)


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
        w, h = (8 + g.random() * 40) * U, (2 + g.random() * 6) * U
        t = g.random()
        c = tuple(int(v) for v in lerp(col('#c87a46'), col('#f0c08a'), t)) + (255,)
        dark = tuple(int(v) for v in lerp(col('#7a3e20'), col('#9a5530'), t)) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.ellipse([x + ox - w / 2, y + oy - h / 2 + 1.5 * U, x + ox + w / 2, y + oy + h / 2 + 1.5 * U], fill=dark)
                d.ellipse([x + ox - w / 2, y + oy - h / 2, x + ox + w / 2, y + oy + h / 2], fill=c)
    img = img.filter(ImageFilter.GaussianBlur(0.7 * U))
    rgb = np.asarray(img)[..., :3].astype(np.float32)
    rgb *= (0.85 + 0.25 * fft_noise(s, 1.1, 14, (0.5, 1.0)))[..., None]
    return to_img(rgb)


def bark_spruce():
    """Norway spruce bark (2026-11-25, Anders' photos): small thin rounded scales, 1-3 cm, grey-brown with a few
    lighter grey and reddish ones, dark gaps between, the lower edge of each scale lifting off (a shadow under it),
    pale grey-green crust lichen in patches. About 0.4 m round by 0.4 m up a picture."""
    s = IN
    K = 2                                        # painted at twice the size, then made smaller (smooth edges)
    S2 = s * K
    g = np.random.default_rng(31)
    img = Image.new('RGB', (S2 * 3, S2 * 3), tuple(int(v) for v in col('#4f4740')))
    d = ImageDraw.Draw(img)
    tone = fft_noise(s, 1.6, 32)
    shapes = []
    for k in range(5600):
        x, y = g.random() * S2, g.random() * S2
        w = (8 + g.random() * 15) * K * U
        h = w * (0.5 + g.random() * 0.3)
        t = tone[int(y / K) % s, int(x / K) % s]
        base = lerp(col('#6a625a'), col('#8e877e'), 0.4 * g.random() + 0.6 * t)
        if g.random() < 0.06:
            base = lerp(base, col('#86604c'), 0.4)          # a fresh reddish scale where an old one fell off
        if g.random() < 0.2:
            base = lerp(base, col('#9c978c'), 0.4)          # a weathered pale one
        n = 9
        ang0 = g.random() * 6.283
        rad = [0.75 + 0.25 * g.random() for _ in range(n)]
        pts = [(math.cos(ang0 + i * 6.283 / n) * w / 2 * rad[i], math.sin(ang0 + i * 6.283 / n) * h / 2 * rad[i]) for i in range(n)]
        for ox in (0, S2, 2 * S2):
            for oy in (0, S2, 2 * S2):
                shapes.append((y + oy, x + ox, pts, base, h))
    # from the top down over the whole painting: each scale lies over the one above it, and the order repeats
    shapes.sort(key=lambda q: q[0])
    for (cy, cx, pts, base, h) in shapes:
        d.polygon([(cx + px, cy + py + 2.0 * K * U) for px, py in pts], fill=tuple(int(v) for v in base * 0.62))
        d.polygon([(cx + px, cy + py) for px, py in pts], fill=tuple(int(v) for v in base))
        d.polygon([(cx + px * 0.75, cy + py * 0.75 + h * 0.12) for px, py in pts], fill=tuple(int(v) for v in np.minimum(base * 1.08, 255)))
    img = img.crop((S2, S2, 2 * S2, 2 * S2)).filter(ImageFilter.GaussianBlur(0.8 * K * U)).resize((s, s), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32)
    rgb *= (0.86 + 0.24 * fft_noise(s, 1.2, 33))[..., None]
    lich = lichen_mask(s, 34, 0.1, 1.3)
    rgb = lerp(rgb, col('#9a9c90'), lich[..., None] * 0.45)
    return to_img(rgb)


def bark_birch():
    """Silver birch (about 0.5 m a picture): chalk white with a faint warm tint, black horizontal lenticels of
    many lengths, black rough diamonds and streaks here and there, thin grey lines of peeling paper."""
    s = IN
    n = fft_noise(s, 1.5, 31, (1.0, 0.4))
    rgb = lerp(col('#e4e1d8'), col('#f5f3ec'), n[..., None])
    rgb = lerp(rgb, col('#d2cbbd'), smooth(0.6, 0.85, fft_noise(s, 1.6, 32))[..., None] * 0.4)
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(33)

    def wrap_poly(pts, c):
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.polygon([(px + ox, py + oy) for px, py in pts], fill=c)

    # lenticels: thin dark horizontal dashes, tapering at both ends
    for _ in range(700):
        x, y = g.random() * s, g.random() * s
        w, h = (5 + g.random() ** 2 * 60) * U, (0.5 + g.random() * 1.1) * U
        c = tuple(int(v) for v in lerp(col('#1e1b19'), col('#5a524a'), g.random())) + (255,)
        wrap_poly([(x - w / 2, y), (x - w / 4, y - h / 2), (x + w / 4, y - h / 2 + g.random()), (x + w / 2, y), (x + w / 4, y + h / 2), (x - w / 4, y + h / 2)], c)
    # rough black patches below old branches: ragged, wider than tall, from noise
    arr = np.asarray(img).astype(np.float32)
    blot = lichen_mask(s, 34, 0.07, 1.5)
    blot = blot * smooth(0.3, 0.6, fft_noise(s, 1.1, 35, (1.0, 0.35)))
    arr[..., :3] = lerp(arr[..., :3], col('#24201c'), blot[..., None] * 0.92)
    img = Image.fromarray(arr.astype(np.uint8), 'RGBA')
    d = ImageDraw.Draw(img)
    # (2026-11-25, Anders' birch photos) black chevrons where old branches were: an upside-down V, thick in the
    # middle, ragged, with a dark knot under its point
    # (the picture is 0.5 m round by 1 m up: everything drawn half as tall as it looks)
    for _ in range(2):
        x, y = g.random() * s, g.random() * s
        w, hh = (80 + g.random() * 70) * U, (15 + g.random() * 15) * U
        th = (6 + g.random() * 5) * U
        top, bot = [], []
        for k in range(13):
            t = k / 12.0
            px = x - w / 2 + w * t
            py = y - hh / 2 + hh * abs(t - 0.5) * 2
            th_k = th * (1 - 0.75 * abs(t - 0.5) * 2) * (0.7 + 0.6 * g.random())
            top.append((px, py - th_k * 0.5))
            bot.append((px, py + th_k * 0.5))
        wrap_poly(top + bot[::-1], (30, 27, 24, 255))
        wrap_poly([(x - 8 * U, y - hh / 2 + 5 * U), (x + 8 * U, y - hh / 2 + 5 * U), (x + 5 * U, y - hh / 2 + 13 * U), (x - 5 * U, y - hh / 2 + 13 * U)], (34, 30, 26, 255))
    # peeling paper: pale lines with a grey shadow under them
    for _ in range(50):
        x, y = g.random() * s, g.random() * s
        w = (10 + g.random() * 40) * U
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(x + ox, y + oy + 1.5 * U), (x + ox + w, y + oy + 1.5 * U)], fill=(170, 165, 155, 255), width=U)
                d.line([(x + ox, y + oy), (x + ox + w, y + oy)], fill=(252, 250, 244, 255), width=2 * U)
    return img.filter(ImageFilter.GaussianBlur(0.5 * U))


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
        count = int(ln * 1400)
        for k in range(count):
            t = g.random()
            i = min(6, int(t * 7))
            px = lerp(pts[i][0], pts[i + 1][0], t * 7 - i)
            py = lerp(pts[i][1], pts[i + 1][1], t * 7 - i)
            side = 1 if g.random() < 0.5 else -1
            a = ang + side * R(0.5, 1.3)
            nl = R(0.02, 0.032)
            tipness = smooth(0.7, 1.0, np.float32(t))
            c0 = mix('#233f27', '#34532e', g.random())
            c1 = mix('#486a38', '#80a04e', float(tipness) * 0.8 + g.random() * 0.2)
            c.taper([(px, py), (px + math.cos(a) * nl, py + math.sin(a) * nl)], 1.8, 1.0, c0, c1)
        if depth == 0:
            for k in range(5):
                t = 0.2 + 0.14 * k
                i = int(t * 7)
                side = 1 if k % 2 else -1
                branchlet(pts[i], ang + side * R(0.6, 0.9), ln * R(0.35, 0.5), 1)

    for k in range(18):
        t = 0.05 + 0.88 * k / 17
        i = int(t * (len(axis) - 1))
        side = 1 if k % 2 else -1
        ln = 0.47 * math.sin(math.pi * (0.15 + 0.8 * t)) * R(0.85, 1.05)
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
    # (2026-11-25, Anders: far too few leaves in the crowns, he wants about ten times as many): many hanging twigs
    # side by side, each thick with small leaves, so a card is a curtain of leaves with only small gaps
    for s in range(11):
        x0 = 0.06 + 0.088 * s + R(-0.03, 0.03)
        pts = bezier((x0, 0.98), (x0 + R(-0.12, 0.12), 0.6), (x0 + R(-0.08, 0.08), R(0.03, 0.15)), 22)
        c.taper(pts, 2.2, 0.9, twig)
        for k, (px, py) in enumerate(pts[1:]):
            for side in (-1, 1, 0):
                if side == 0 and g.random() < 0.5:
                    continue
                a = -math.pi / 2 + side * R(0.4, 1.3) + R(-0.3, 0.3)
                ln = R(0.045, 0.07)
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
    for s in range(8):
        a0 = s * 0.785 + R(-0.3, 0.3)
        p0 = (0.5 + 0.12 * math.cos(a0), 0.5 + 0.12 * math.sin(a0))
        a = a0 + R(-0.4, 0.4)
        p1 = (0.5 + 0.45 * math.cos(a), 0.5 + 0.45 * math.sin(a))
        pts = bezier((0.5, 0.5), p0, p1, 10)
        c.taper(pts, 3, 1.2, twig)
        for k, (px, py) in enumerate(pts[2:]):
            for side in (-1, 1):
                aa = a + side * R(0.5, 1.1)
                ln = R(0.075, 0.11)
                shade = g.random()
                fill = mix('#3f6a28', '#78a03e', shade)
                c.poly(leaf_shape(px, py, ln, ln * 0.45, aa, tip=0.2, n=16), fill)
                c.line([(px, py), (px + math.cos(aa) * ln * 0.9, py + math.sin(aa) * ln * 0.9)], 1.2, mix('#3a5a22', '#9ab85a', shade))
                # side veins
                for v in range(5):
                    t = 0.2 + 0.14 * v
                    vx, vy = px + math.cos(aa) * ln * t, py + math.sin(aa) * ln * t
                    for sd in (-1, 1):
                        va = aa + sd * 0.9
                        c.line([(vx, vy), (vx + math.cos(va) * ln * 0.12, vy + math.sin(va) * ln * 0.12)], 1, mix('#355a22', '#6a8e3a', shade, 140))
    return c.done()


def lichen_top():
    """Reindeer lichen seen from above: pale grey-white cushions of fine branches, ragged edge (alpha)."""
    c = Canvas()
    g = np.random.default_rng(97)
    for k in range(3000):
        r = math.sqrt(g.random()) * 0.46
        a = g.random() * 6.283
        px, py = 0.5 + math.cos(a) * r, 0.5 + math.sin(a) * r
        rr = R(0.006, 0.016)
        t = g.random()
        c.ellipse(px, py, rr, rr, mix('#8c9282', '#cdd1c0', t))
        c.ellipse(px - rr * 0.2, py + rr * 0.2, rr * 0.5, rr * 0.5, mix('#b4b8a6', '#dfe2d4', t))
    return c.done()


def moss_sock():
    """The foot of an old trunk: moss and crust lichen growing up the bark, thick at the bottom, in ragged tongues
    higher up (alpha above them). u goes round the trunk and repeats; v is 0 at the ground and 1 at the top."""
    s = IN
    ys, xs = np.mgrid[0:s, 0:s].astype(np.float32)
    up = 1.0 - ys / s                                  # 0 at the bottom row, 1 at the top
    tongues = fft_noise(s, 1.4, 501, (0.2, 1.0)) * 0.7 + fft_noise(s, 0.8, 502) * 0.3
    reach = 0.25 + 0.6 * tongues
    alpha = smooth(reach + 0.02, reach - 0.02, up) * 255
    alpha[:, :] = np.where(up < 0.03, 0, alpha)        # a clear row at the very top and bottom
    alpha = np.where(up > 0.97, 0, alpha)
    n = fft_noise(s, 1.5, 504) * 0.5 + fft_noise(s, 0.7, 506) * 0.5
    speck = np.random.default_rng(507).random((s, s))
    rgb = lerp(col('#3e5a1c'), col('#86a43a'), n[..., None])
    rgb = lerp(rgb, col('#a6c050'), (speck > 0.85)[..., None] * 0.35)
    rgb = lerp(rgb, col('#26341a'), (speck < 0.1)[..., None] * 0.4)
    lich = smooth(0.55, 0.0, reach - up) * smooth(0.5, 0.7, fft_noise(s, 1.3, 505))
    rgb = lerp(rgb, col('#a8b08e'), lich[..., None] * 0.6)
    return to_img(rgb, alpha)


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
        dry = g.random() < 0.07
        c0 = mix('#3e5a24', '#5a7c2e', g.random()) if not dry else mix('#7a7040', '#9a8a52', g.random())
        c1 = mix('#6c9636', '#8eb04a', g.random()) if not dry else mix('#c8b878', '#e0d098', g.random())
        c.taper(bezier((x, 0.0), mid, tip, 8), R(4, 6.5), 0.8, c0, c1)
    for k in range(3):
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

def ragged(img, seed, cy=0.5, ry=0.5):
    """Thin a cut-out out towards a ragged, roundish edge (the card's square outline must never show,
    2026-11-25 Anders: the litter lay in rectangles). The part centred at cy with half-height ry."""
    a = np.asarray(img).astype(np.float32).copy()
    s = a.shape[0]
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32) / s
    d = np.sqrt(((xx - 0.5) / 0.5) ** 2 + (((1 - yy) - cy) / ry) ** 2)
    n = fft_noise(s, 1.6, seed) * 0.6 + fft_noise(s, 0.6, seed + 1) * 0.4
    keep = smooth(1.0, 0.55, d + (n - 0.5) * 0.7)
    a[..., 3] *= keep
    return Image.fromarray(a.astype(np.uint8), 'RGBA')


def litter():
    """The forest floor from above, cut out, in two halves (v 0.5-1: conifer litter of pine and spruce needles,
    twigs and cones; v 0-0.5: fallen birch and aspen leaves with some needles), each thinning out to a ragged edge so
    the cards melt into the ground and into each other."""
    c = Canvas()
    g = np.random.default_rng(131)

    def needles(y0, y1, count):
        for k in range(count):
            x, y = R(0.02, 0.98), R(y0, y1)
            a = g.random() * math.pi
            ln = R(0.012, 0.035)
            cc = mix('#5a3e26', '#b48c5a', g.random(), int(R(200, 255))) if g.random() < 0.8 else mix('#6a6a3a', '#8a8a4a', g.random())
            c.line([(x, y), (x + math.cos(a) * ln, y + math.sin(a) * ln)], R(1.0, 1.6), cc)

    def twigs(y0, y1, count):
        for k in range(count):
            x, y = R(0.1, 0.9), R(y0 + 0.05, y1 - 0.05)
            a = g.random() * 6.28
            ln = R(0.08, 0.22)
            pts = []
            for i in range(8):
                a += R(-0.15, 0.15)
                pts.append((x, y))
                x += math.cos(a) * ln / 7
                y += math.sin(a) * ln / 7
            cc = mix('#3e3026', '#7a6650', g.random())
            c.taper(pts, R(2.0, 3.5), 1.0, cc)

    def cones(y0, y1, count):
        for k in range(count):
            x, y = R(0.15, 0.85), R(y0 + 0.08, y1 - 0.08)
            a = g.random() * 6.28
            ln = R(0.035, 0.06)
            for j in range(16):
                t = j / 15
                px, py = x + math.cos(a) * ln * t, y + math.sin(a) * ln * t
                r = ln * 0.3 * math.sin(math.pi * (0.15 + 0.8 * t)) + 0.003
                c.ellipse(px, py, r, r * 0.8, mix('#3e2a1c', '#7a5634', (j % 3) / 2))

    def leaves(y0, y1, count):
        for k in range(count):
            x, y = R(0.05, 0.95), R(y0, y1)
            a = g.random() * 6.28
            ln = R(0.022, 0.04)
            t = g.random()
            cc = mix('#7a5a28', '#c8a04a', t) if g.random() < 0.7 else mix('#5a3a22', '#8a5a34', t)
            c.poly(leaf_shape(x, y, ln, ln * 0.62, a, tip=0.1, n=10, serr=0.08), cc)

    # conifer half (top)
    needles(0.5, 1.0, 1500)
    twigs(0.5, 1.0, 9)
    cones(0.5, 1.0, 4)
    leaves(0.5, 1.0, 6)
    # leaf half (bottom)
    needles(0.0, 0.5, 350)
    leaves(0.0, 0.5, 260)
    twigs(0.0, 0.5, 5)
    img = c.done()
    top = ragged(img, 137, 0.75, 0.25)
    bot = ragged(img, 139, 0.25, 0.25)
    a = np.asarray(top).astype(np.float32)
    b = np.asarray(bot).astype(np.float32)
    s_ = a.shape[0]
    out = np.where((np.arange(s_)[:, None, None] < s_ // 2), a, b)
    return Image.fromarray(out.astype(np.uint8), 'RGBA')


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
    """Lingonberry: a low bushy mat of short upright shoots with small dark glossy oval leaves, a few red berries."""
    c = Canvas()
    g = np.random.default_rng(161)
    shoots = []
    for k in range(40):
        x = R(0.04, 0.96)
        top = (0.45 + 0.35 * math.sin(math.pi * x)) * R(0.7, 1.05)
        pts = bezier((x, 0.0), (x + R(-0.06, 0.06), top * 0.5), (x + R(-0.12, 0.12), top), 8)
        c.taper(pts, 2.5, 1.2, rgba('#5a4a2e'))
        shoots.append(pts)
    leaves = []
    for pts in shoots:
        for i, (px, py) in enumerate(pts[1:]):
            for side in (-1, 1):
                a = math.pi / 2 + side * R(0.5, 1.2)
                ln = R(0.04, 0.06)
                leaves.append((py, px, a, ln, g.random()))
    leaves.sort()
    for (py, px, a, ln, sh) in leaves:
        t = min(1.0, sh * 0.5 + py)
        c.poly(leaf_shape(px, py, ln, ln * 0.55, a, tip=0.05, n=10), mix('#1e3a18', '#4a7a2a', t))
        c.line([(px + math.cos(a) * ln * 0.2, py + math.sin(a) * ln * 0.2), (px + math.cos(a) * ln * 0.6, py + math.sin(a) * ln * 0.6)], 1, mix('#6a9a4a', '#a8d080', t, 150))
    for pts in shoots:
        if g.random() < 0.15:
            px, py = pts[-1]
            for j in range(3):
                r = R(0.012, 0.016)
                bx, by = px + R(-0.02, 0.02), py - R(0.0, 0.03)
                c.ellipse(bx, by, r, r, rgba('#a01c1c'))
                c.ellipse(bx - r * 0.3, by + r * 0.3, r * 0.3, r * 0.3, rgba('#f08080', 200))
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
    """Moss carpet (repeats, about 0.7 m a picture; 2026-11-25, Anders' photos of mossy spruce forest): a dense
    pile of tiny feathery sprigs, yellow-green on top, dark between, in small soft cushions. No big shapes."""
    s = IN
    K = 2
    S2 = s * K
    g = np.random.default_rng(345)
    from scipy.ndimage import gaussian_filter
    f1, f2, cid = voronoi(s, 70, 341, warp=25)
    dome = np.sqrt(np.clip(1.0 - (f1 / (f1 + f2 + 1e-3) * 2.0) ** 2, 0, 1))
    big = fft_noise(s, 1.8, 342)
    img = Image.new('RGB', (S2 * 3, S2 * 3), tuple(int(v) for v in col('#26340f')))
    d = ImageDraw.Draw(img)
    for k in range(9000):
        x, y = g.random() * S2, g.random() * S2
        lit = dome[int(y / K) % s, int(x / K) % s] * 0.6 + big[int(y / K) % s, int(x / K) % s] * 0.4
        t = min(1.0, max(0.0, lit * 0.8 + g.random() * 0.4))
        c = tuple(int(v) for v in lerp(col('#40581a'), col('#9cb446'), t))
        a = g.random() * 6.283
        ln = (5 + g.random() * 7) * K
        ex, ey = math.cos(a) * ln, math.sin(a) * ln
        for ox in (0, S2, 2 * S2):
            for oy in (0, S2, 2 * S2):
                cx, cy = x + ox, y + oy
                d.line([(cx, cy), (cx + ex, cy + ey)], fill=c, width=K)
                # little side shoots: a feathery sprig
                for j in (0.35, 0.6, 0.85):
                    bx, by = cx + ex * j, cy + ey * j
                    for sd in (-1, 1):
                        sa = a + sd * 0.9
                        d.line([(bx, by), (bx + math.cos(sa) * ln * 0.3, by + math.sin(sa) * ln * 0.3)], fill=c, width=1)
    img = img.crop((S2, S2, 2 * S2, 2 * S2)).resize((s, s), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32)
    rgb *= (0.8 + 0.35 * dome)[..., None]
    rgb = lerp(rgb, col('#a8b850'), smooth(0.75, 0.95, big)[..., None] * 0.2)
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

def berry_leaf(c, g, px, py, ln, a, depth):
    """One bilberry leaf: an oval with a fine toothed edge, lit on one half, a pale midrib. depth 0 = at the back
    (dark, shaded), 1 = at the front in the sun."""
    t = min(1.0, max(0.0, depth * 0.8 + g.random() * 0.35))
    base = lerp(col('#203a16'), col('#5c8a30'), t)
    base = base * (0.9 + 0.2 * g.random())
    w = ln * R(0.42, 0.55)
    pts = leaf_shape(px, py, ln, w, a, tip=0.05, n=12, serr=0.04)
    c.poly(pts, tuple(int(max(0, min(255, v))) for v in base) + (255,))
    # the lit half: a slimmer leaf on top, lighter and yellower
    ca, sa = math.cos(a), math.sin(a)
    hi = lerp(base, col('#9cbc58'), 0.2 + 0.2 * t)
    half = leaf_shape(px + sa * w * 0.18, py - ca * w * 0.18, ln * 0.92, w * 0.55, a, tip=0.05, n=10)
    c.poly(half, tuple(int(max(0, min(255, v))) for v in hi) + (255,))
    if ln > 0.04:
        c.line([(px, py), (px + ca * ln * 0.85, py + sa * ln * 0.85)], 1.1, tuple(int(v) for v in lerp(base, col('#c8dc8c'), 0.45)) + (255,))


def bilberry_sprig():
    """One bilberry sprig (2026-11-25, Anders: bilberry should be 3D, not pictures): a green zigzag twig up the
    middle with 7-9 oval leaves one by one, filling the tile; the 3D bush sets many of these round its stems."""
    c = Canvas()
    g = np.random.default_rng(97)
    pts = [(0.5, 0.0)]
    x, y, ang = 0.5, 0.0, math.pi / 2
    for i in range(8):
        ang = math.pi / 2 + (0.22 if i % 2 else -0.22) + R(-0.08, 0.08)
        x += math.cos(ang) * 0.115
        y += math.sin(ang) * 0.115
        pts.append((x, y))
    c.taper(pts, 6, 3, rgba('#4a7026'), rgba('#78a33c'))
    leaves = []
    for i in range(1, len(pts)):
        px, py = pts[i]
        side = 1 if i % 2 else -1
        a = math.pi / 2 + side * R(0.6, 1.2)
        ln = R(0.2, 0.27) * (1.05 - 0.25 * py)
        leaves.append((px, py, a, ln))
    leaves.append((pts[-1][0], pts[-1][1], math.pi / 2 + R(-0.2, 0.2), 0.16))
    for k, (px, py, a, ln) in enumerate(leaves):
        berry_leaf(c, g, px, py, ln, a, 0.35 + 0.6 * k / len(leaves))
    return c.done()


def bilberry():
    """Bilberry from the side (2026-11-25, Anders: every bush clear, leaves and not a sea of green): green angular
    zigzag stems branching up from the moss, small oval leaves set one by one along them, the ones at the back and
    low down dark in the shade, the front and top ones bright yellow-green in the sun, gaps between them."""
    c = Canvas()
    g = np.random.default_rng(91)
    stems = []

    def stem(x, y, ang, ln, w, depth):
        pts = [(x, y)]
        for i in range(7):
            ang += R(-0.35, 0.35)       # zigzag, angular
            x += math.cos(ang) * ln / 7
            y += math.sin(ang) * ln / 7
            pts.append((x, y))
        c.taper(pts, w, w * 0.55, mix('#3e6022', '#557a2c', g.random()), rgba('#6e9a38'))
        stems.append((pts, depth))
        if depth < 2:
            for k in range(2 if depth == 0 else 1):
                i = int(R(2, 6))
                side = 1 if g.random() < 0.5 else -1
                stem(pts[i][0], pts[i][1], ang + side * R(0.45, 1.0), ln * R(0.35, 0.55), w * 0.7, depth + 1)

    for k in range(17):
        x = 0.08 + 0.84 * k / 16 + R(-0.03, 0.03)
        top = 0.55 + 0.38 * math.sin(math.pi * x) * R(0.8, 1.05)
        stem(x, 0.0, math.pi / 2 + (x - 0.5) * 0.9 + R(-0.25, 0.25), top, 3.5, 0)
    leaves = []
    for pts, depth in stems:
        for i in range(1, len(pts)):
            px, py = pts[i]
            if py < 0.12:
                continue
            for side in (-1, 1):
                if g.random() < 0.3:
                    continue
                a = math.pi / 2 + side * R(0.5, 1.5)
                ln = R(0.04, 0.068) * (0.8 + 0.3 * py)
                mx = (pts[i - 1][0] + px) * 0.5 if side < 0 else px
                my = (pts[i - 1][1] + py) * 0.5 if side < 0 else py
                leaves.append((mx, my, a, ln, g.random()))
    g.shuffle(leaves)
    # back to front: half of them drawn first and dark (the far side of the bush), then the rest
    n = len(leaves)
    for k, (px, py, a, ln, r) in enumerate(leaves):
        front = k / n
        berry_leaf(c, g, px, py, ln, a, front * 0.65 + py * 0.45)
    for pts, depth in stems:
        if g.random() < 0.06:
            px, py = pts[int(R(2, 5))]
            r = R(0.013, 0.017)
            c.ellipse(px, py - 0.02, r, r, rgba('#1c2444'))
            c.ellipse(px - r * 0.3, py - 0.02 + r * 0.3, r * 0.35, r * 0.3, rgba('#8a9cc4', 200))
    return c.done()


def bilberry_top():
    """A bilberry bush from above: shoots spreading from the middle, each with its leaves one by one along it,
    lower shoots darker, the tips in the sun; a ragged edge with gaps (alpha), so bushes read one by one."""
    c = Canvas()
    g = np.random.default_rng(95)
    shoots = []
    for k in range(44):
        a = g.random() * 6.283
        r0 = R(0.0, 0.16)
        x, y = 0.5 + math.cos(a) * r0, 0.5 + math.sin(a) * r0
        ln = R(0.22, 0.4)
        pts = [(x, y)]
        for i in range(6):
            a += R(-0.3, 0.3)
            x += math.cos(a) * ln / 6
            y += math.sin(a) * ln / 6
            pts.append((x, y))
        shoots.append((pts, a, g.random()))
    shoots.sort(key=lambda s: s[2])
    for pts, a, h in shoots:
        c.taper(pts, 2.6, 1.4, rgba('#3a5a20'), rgba('#5e8a30'))
        for i in range(1, len(pts)):
            px, py = pts[i]
            for side in (-1, 1):
                if g.random() < 0.25:
                    continue
                d = (px - 0.5) ** 2 + (py - 0.5) ** 2
                if d > 0.47 ** 2:
                    continue
                la = a + side * R(0.7, 1.5)
                berry_leaf(c, g, px, py, R(0.04, 0.062), la, h * 0.55 + 0.3 * (i / len(pts)))
    for k in range(3):
        px, py = R(0.3, 0.7), R(0.3, 0.7)
        c.ellipse(px, py, 0.012, 0.012, rgba('#1c2444'))
    return c.done()


def heather():
    """Heather / ling from the side (2026-11-25, Anders' photo): a dense round bush of wiry brown stems, every
    shoot clad in tiny dark olive scale leaves, the upper third of most shoots a spike of small pink-mauve bells,
    lighter on top in the sun, darker and browner inside and low down."""
    c = Canvas()
    g = np.random.default_rng(121)
    shoots = []

    def stem(x, y, a, ln, w, depth):
        pts = [(x, y)]
        for i in range(8):
            a += R(-0.15, 0.15)
            x += math.cos(a) * ln / 8
            y += math.sin(a) * ln / 8
            pts.append((x, y))
        c.taper(pts, w, w * 0.45, rgba('#4a3828'), rgba('#5e4e34'))
        if depth >= 1:
            shoots.append(pts)
        if depth < 2:
            for k in range(4 if depth == 0 else 3):
                i = int(R(2, 7))
                stem(pts[i][0], pts[i][1], math.pi / 2 + (a - math.pi / 2) * 0.6 + (1 if g.random() < 0.5 else -1) * R(0.1, 0.45),
                     ln * R(0.45, 0.7), w * 0.6, depth + 1)

    for k in range(22):
        x = R(0.1, 0.9)
        top = 0.45 + 0.45 * math.sin(math.pi * x)
        stem(x, 0.0, math.pi / 2 + (x - 0.5) * 1.3 + R(-0.2, 0.2), top * R(0.5, 0.75), 3.0, 0)
    g.shuffle(shoots)
    n = len(shoots)
    for k, pts in enumerate(shoots):
        front = k / n
        flowering = g.random() < 0.62
        m = len(pts)
        for j in range(1, m):
            px, py = pts[j]
            up = j / m
            light = min(1.0, front * 0.5 + py * 0.6)
            if flowering and up > 0.62:
                # a spike of small bells all round the shoot
                for q in range(5):
                    a = math.pi / 2 + R(-1.9, 1.9)
                    d = R(0.004, 0.014)
                    cc = lerp(col('#6a3a62'), col('#c898bc'), min(1.0, light * 0.75 + g.random() * 0.3))
                    c.ellipse(px + math.cos(a) * d, py + math.sin(a) * d, R(0.004, 0.0065), R(0.005, 0.008),
                              tuple(int(v) for v in cc) + (255,))
            else:
                for q in range(9):
                    a = math.pi / 2 + R(-1.5, 1.5)
                    ln = R(0.01, 0.02)
                    cc = lerp(col('#1e2614'), col('#68743a'), min(1.0, light * 0.8 + g.random() * 0.3))
                    c.line([(px, py), (px + math.cos(a) * ln, py + math.sin(a) * ln)], 2.2, tuple(int(v) for v in cc) + (255,))
    return c.done()


def heather_top():
    """A heather bush from above: a dense round cushion of flower spikes, pink-mauve with dark olive leaves between,
    ragged edge (alpha)."""
    c = Canvas()
    g = np.random.default_rng(127)
    for k in range(2500):
        r = math.sqrt(g.random()) * 0.45
        a = g.random() * 6.283
        px, py = 0.5 + math.cos(a) * r, 0.5 + math.sin(a) * r
        la = g.random() * 6.283
        cc = lerp(col('#1a2212'), col('#56602e'), g.random())
        c.line([(px, py), (px + math.cos(la) * 0.02, py + math.sin(la) * 0.02)], 2.6, tuple(int(v) for v in cc) + (255,))
    for k in range(4200):
        r = math.sqrt(g.random()) * 0.46 * (0.85 + 0.15 * math.sin(g.random() * 6.283))
        a = g.random() * 6.283
        px, py = 0.5 + math.cos(a) * r, 0.5 + math.sin(a) * r
        depth = g.random()
        if g.random() < 0.5:
            cc = lerp(col('#5c3456'), col('#c49aba'), min(1.0, depth * 0.85 + (1 - r / 0.46) * 0.2))
            c.ellipse(px, py, R(0.005, 0.008), R(0.005, 0.008), tuple(int(v) for v in cc) + (255,))
        else:
            la = g.random() * 6.283
            ln = R(0.01, 0.02)
            cc = lerp(col('#222c16'), col('#66723a'), depth)
            c.line([(px, py), (px + math.cos(la) * ln, py + math.sin(la) * ln)], 2.0, tuple(int(v) for v in cc) + (255,))
    return c.done()


def slash():
    """Logging slash (hygge, Anders' photos): cut spruce and pine branches lying criss-cross, grey-brown wood, some
    with rust-red dead needles still on; for cards lying on the ground and in low heaps."""
    c = Canvas()
    g = np.random.default_rng(161)

    def branch(x, y, a, ln, w, depth, needles):
        pts = [(x, y)]
        for i in range(7):
            a += R(-0.15, 0.15)
            x += math.cos(a) * ln / 7
            y += math.sin(a) * ln / 7
            pts.append((x, y))
        c.taper(pts, w, w * 0.4, mix('#4c4036', '#7a6a58', g.random()), mix('#5a4c40', '#8a7a66', g.random()))
        # (2026-11-26, Anders: the pale sawn ends showed as white dots on every stick; the ends are the wood's own
        # colour now)
        if needles and depth >= 1:
            for i in range(1, len(pts)):
                px, py = pts[i]
                for q in range(9):
                    na = a + R(-1.6, 1.6)
                    nl = R(0.008, 0.02)
                    cc = mix('#6a4a30', '#a2704a', g.random()) if g.random() < 0.5 else mix('#4e4a3a', '#7a705c', g.random()) if g.random() < 0.7 else mix('#4a5a2c', '#6a7a3a', g.random())
                    c.line([(px, py), (px + math.cos(na) * nl, py + math.sin(na) * nl)], 1.3, cc)
        if depth < 2:
            for k in range(4):
                i = int(R(1, 6))
                side = 1 if g.random() < 0.5 else -1
                branch(pts[i][0], pts[i][1], a + side * R(0.5, 1.1), ln * R(0.3, 0.5), w * 0.55, depth + 1, needles)

    for k in range(14):
        a = g.random() * 6.283
        x, y = 0.5 - math.cos(a) * 0.42, 0.5 - math.sin(a) * 0.42
        branch(x, y, a + R(-0.3, 0.3), R(0.55, 0.85), R(9, 16), 0, g.random() < 0.65)
    return c.done()


def soil():
    """Churned clear-cut ground (scarified strips, wheel ruts): orange-brown mineral soil and black humus turned
    over, stones, roots and wood chips, ragged edges into the vegetation (alpha across u)."""
    s = IN
    n = fft_noise(s, 1.7, 341)
    hum = smooth(0.45, 0.6, fft_noise(s, 1.5, 342))
    rgb = lerp(col('#7a5a3a'), col('#a07850'), n[..., None])
    rgb = lerp(rgb, col('#3a2c22'), hum[..., None] * 0.75)
    rgb *= (0.82 + 0.3 * np.random.default_rng(343).random((s, s)))[..., None]
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(344)
    for _ in range(120):
        x, y, r = g.random() * s, g.random() * s, 2 + g.random() * 7
        c_ = tuple(int(v) for v in lerp(col('#6a665e'), col('#a8a296'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.ellipse([x + ox - r, y + oy - r * 0.75, x + ox + r, y + oy + r * 0.75], fill=c_)
    for _ in range(260):
        x, y = g.random() * s, g.random() * s
        a, ln = g.random() * 6.28, 8 + g.random() * 22
        c_ = tuple(int(v) for v in lerp(col('#4a3a2c'), col('#c8b08a'), g.random())) + (255,)
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                d.line([(x + ox, y + oy), (x + ox + math.cos(a) * ln, y + oy + math.sin(a) * ln)], fill=c_, width=1 + int(g.random() * 2))
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    rgb = np.asarray(img).astype(np.float32)[..., :3]
    xs = np.mgrid[0:s, 0:s][1].astype(np.float32)
    edge = np.minimum(xs, s - 1 - xs) / s
    rag = fft_noise(s, 1.3, 345) * 0.7 + fft_noise(s, 0.7, 346) * 0.3
    limit = 0.04 + 0.2 * rag
    alpha = smooth(limit - 0.015, limit + 0.015, edge) * 255
    return to_img(rgb, alpha)


def reindeer_lichen():
    """Reindeer lichen cushions seen close (2026-11-25, Anders: the flat white rings looked sick): a mass of tiny
    pale grey-green branching tips, dark gaps between them, a few yellower and greener spots (repeats; laid on low
    lumpy mounds)."""
    s = IN
    base = fft_noise(s, 1.5, 351)
    rgb = lerp(col('#5e6454'), col('#7c8270'), base[..., None])
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    g = np.random.default_rng(352)
    for _ in range(5200):
        x, y = g.random() * s, g.random() * s
        t = g.random()
        cc = lerp(col('#a8ae9a'), col('#e8eadc'), t)
        if g.random() < 0.12:
            cc = lerp(col('#b8b48a'), col('#d8d4a8'), t)
        c_ = tuple(int(v) for v in cc) + (255,)
        a = g.random() * 6.28
        ln = 3 + g.random() * 5
        for ox in (-s, 0, s):
            for oy in (-s, 0, s):
                x0, y0 = x + ox, y + oy
                for k in range(3):
                    x1, y1 = x0 + math.cos(a) * ln, y0 + math.sin(a) * ln
                    d.line([(x0, y0), (x1, y1)], fill=c_, width=2)
                    x0, y0 = x1, y1
                    a += (g.random() - 0.5) * 1.6
                d.ellipse([x0 - 1.5, y0 - 1.5, x0 + 1.5, y0 + 1.5], fill=c_)
    rgb = np.asarray(img).astype(np.float32)[..., :3]
    rgb *= (0.85 + 0.25 * fft_noise(s, 1.2, 353))[..., None]
    return to_img(rgb)


def granite():
    """Granite boulders (2026-11-25, second try after Anders' photos of real boulders: the first was far too dark and
    cracked): light grey with a fine salt-and-pepper grain, white quartz specks and veins, a faint pink feldspar
    cast, and big patches of pale grey-white crust lichen; hardly any cracks (repeats; each stone gets its own colour
    cast and moss, the shader blends two scales so no pattern shows)."""
    s = IN
    base = fft_noise(s, 1.8, 301)
    grain = fft_noise(s, 0.5, 309)
    rgb = lerp(col('#8c8881'), col('#b8b4aa'), base[..., None])
    rgb *= (0.8 + 0.4 * grain)[..., None]
    kind = fft_noise(s, 0.35, 312)
    kind2 = fft_noise(s, 1.2, 314)
    rgb = lerp(rgb, col('#e6e2da'), smooth(0.66, 0.78, kind)[..., None] * 0.55)          # quartz, white
    rgb = lerp(rgb, col('#b09486'), smooth(0.6, 0.8, kind2)[..., None] * 0.2)            # feldspar cast
    rgb = lerp(rgb, col('#4a4844'), smooth(0.2, 0.12, kind)[..., None] * 0.35)           # mica, small dark
    # one faint vein of quartz, no dark cracks
    v = band_noise(s, 2.0, 305)
    rgb = lerp(rgb, col('#ece8e0'), smooth(0.03, 0.0, np.abs(v - 0.5))[..., None] * 0.5)
    # crust lichen: big pale grey-white and grey-green patches, a few yellow-green map lichen
    rgb = lerp(rgb, col('#dcdcd0'), lichen_mask(s, 306, 0.22, 1.9)[..., None] * 0.6)
    rgb = lerp(rgb, col('#b8c0a8'), lichen_mask(s, 307, 0.12, 1.7)[..., None] * 0.55)
    rgb = lerp(rgb, col('#b4b46a'), lichen_mask(s, 308, 0.03, 1.2)[..., None] * 0.5)
    rgb = lerp(rgb, col('#3a3a34'), lichen_mask(s, 310, 0.02, 1.0)[..., None] * 0.35)
    return to_img(rgb)


def cliff_rock():
    """A cliff face (2026-11-25, second try after Anders' photos of real cliffs: lighter, far fewer cracks, no
    pattern): light grey gneiss with faint long bands, smooth broad planes, soft darker water streaks running down,
    a little rust, big patches of pale crust lichen. No joint lines; the shader blends two scales and the world's
    own patches over it, so no two parts of a cliff look the same."""
    s = IN
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32) / s
    warp = fft_noise(s, 2.2, 321) * 0.12 + fft_noise(s, 1.4, 322) * 0.03
    lay = (yy + warp) * 5.0
    band = np.sin(lay * 6.283) * 0.5 + 0.5
    big = fft_noise(s, 2.0, 323)
    rgb = lerp(col('#8a8780'), col('#b8b5ac'), (big * 0.75 + band * 0.25)[..., None])
    # a fine grain of light and dark minerals, as on the granite
    rgb *= (0.82 + 0.36 * fft_noise(s, 0.45, 330))[..., None]
    rgb = lerp(rgb, col('#b2a49c'), smooth(0.6, 0.85, fft_noise(s, 1.8, 324))[..., None] * 0.25)
    rgb *= (0.92 + 0.14 * fft_noise(s, 0.6, 326))[..., None]
    # gentle broad planes lit a little differently
    plane = fft_noise(s, 2.4, 318)
    rgb *= (0.9 + 0.2 * plane)[..., None]
    # streaks running down: soft and few
    stn = fft_noise(s, 1.5, 313, (0.05, 1.0))
    rgb = lerp(rgb, col('#5e5c57'), smooth(0.62, 0.85, stn)[..., None] * 0.4)
    rust = smooth(0.75, 0.9, fft_noise(s, 1.4, 319, (0.12, 1.0)))
    rgb = lerp(rgb, col('#9a7a5e'), rust[..., None] * 0.12)
    # one or two thin cracks, not a grid
    v = band_noise(s, 1.5, 314, (0.25, 1.0))
    crack = smooth(0.012, 0.0, np.abs(v - 0.5)) * smooth(0.45, 0.6, fft_noise(s, 1.8, 328))
    rgb = lerp(rgb, col('#3c3a36'), crack[..., None] * 0.55)
    rgb = lerp(rgb, col('#dedfd4'), lichen_mask(s, 316, 0.2, 1.9)[..., None] * 0.55)
    rgb = lerp(rgb, col('#b4bca4'), lichen_mask(s, 317, 0.1, 1.6)[..., None] * 0.5)
    rgb = lerp(rgb, col('#a8aa62'), lichen_mask(s, 329, 0.025, 1.2)[..., None] * 0.4)
    return to_img(rgb)


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
    ('moss sock', moss_sock, True),
    ('bilberry top', bilberry_top, False),
    ('lichen top', lichen_top, False),
    ('pine bark low 2', bark_pine_low2, True),
    ('slash', slash, False),
    ('heather top', heather_top, False),
    ('soil', soil, True),
    ('reindeer lichen', reindeer_lichen, True),
    ('bilberry sprig', bilberry_sprig, False),
]

# The sharp bark atlas (2026-11-25, Anders: up close the trees looked pixly): the bark pictures again at twice the
# size, 3 x 2 tiles of 1024 px (32 px padding), in TvAtlas order pine low, pine high, spruce, birch, beech, pine
# low 2. Loaded on High and Ultra only (TvAtlasLit _BarkHD); a far tree uses its smaller mip levels.
HD_TILES = [0, 1, 2, 3, 4, 35]


def hd_bark(out):
    global TILE, PAD, IN, U
    TILE, PAD, U = 1024, 32, 2
    IN = TILE - 2 * PAD
    global N
    atlas = Image.new('RGBA', (TILE * 3, TILE * 2), (0, 0, 0, 0))
    for j, i in enumerate(HD_TILES):
        name, fn, rep = TILES[i]
        img = fn()
        N = 3
        place(atlas, img, j, rep)
        print(j, name, flush=True)
    atlas.convert('RGB').save(out)


if __name__ == '__main__' and sys.argv[1] == '--hd':
    hd_bark(sys.argv[2])
elif __name__ == '__main__':
    out = sys.argv[1]
    prev = sys.argv[2] if len(sys.argv) > 2 else None
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None
    atlas = Image.new('RGBA', (TILE * N, TILE * N), (0, 0, 0, 0))
    for i, (name, fn, rep) in enumerate(TILES):
        if only and name not in only:
            continue
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
