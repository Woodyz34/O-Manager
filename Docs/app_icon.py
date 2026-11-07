#!/usr/bin/env python3
"""Draws the O-Manager game icon (the picture on the .exe, the taskbar and the window)
and the emblem the intro shows above the title.

  Assets/Art/Icon/OManagerIcon.png   1024x1024, set as the game's icon by Assets/Editor/AppIconSetup.cs
  Assets/Resources/Intro/emblem.png  the same picture, loaded by IntroScene.cs

The picture: a piece of orienteering map (dark forest green with brown contour lines),
the purple control circle from the course, and the white and orange control flag in the middle.

Run:  python3 Docs/app_icon.py
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZE = 1024
SS = 4                      # drawn 4x bigger, then scaled down for smooth edges
N = SIZE * SS

GREEN_TOP = np.array([0x2E, 0x6A, 0x45])
GREEN_BOTTOM = np.array([0x15, 0x30, 0x20])
CONTOUR = (0xC8, 0x8A, 0x3C)
PURPLE = (0xC2, 0x2E, 0x96)
WHITE = (0xFA, 0xF6, 0xEC)
ORANGE = (0xF0, 0x6A, 0x1A)
ORANGE_DARK = (0xB8, 0x45, 0x0C)
WHITE_DARK = (0xC9, 0xC3, 0xB5)
CREAM = (0xFB, 0xF4, 0xDA)


def contours():
    # A made-up hill landscape; its height lines are drawn like on the map
    n = 512
    y, x = np.mgrid[0:n, 0:n] / n
    rng = np.random.default_rng(11)
    h = np.zeros((n, n))
    for _ in range(9):
        cx, cy = rng.uniform(-0.1, 1.1, 2)
        r = rng.uniform(0.12, 0.35)
        h += rng.uniform(0.5, 1.2) * np.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (r * r))
    k = h * 9.0
    f = np.abs(k - np.round(k))
    line = np.clip(1.0 - f / 0.06, 0, 1)
    img = Image.fromarray((line * 255).astype(np.uint8)).resize((N, N), Image.BILINEAR)
    return img.filter(ImageFilter.GaussianBlur(SS * 0.6))


def rounded_mask(radius):
    m = Image.new("L", (N, N), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, N - 1, N - 1], radius=radius, fill=255)
    return m


def flag(draw, cx, cy, s):
    # The control flag seen a little from the side: the front face (white over orange along the diagonal)
    # and the narrower side face in shade
    half = s / 2
    l, r, t, b = cx - half, cx + half, cy - half, cy + half
    depth = s * 0.22
    # side face (to the right, going back)
    side = [(r, t), (r + depth, t - depth * 0.28), (r + depth, b - depth * 0.28), (r, b)]
    draw.polygon([side[0], side[1], side[3]], fill=WHITE_DARK)
    draw.polygon([side[1], side[2], side[3]], fill=ORANGE_DARK)
    # front face
    draw.polygon([(l, t), (r, t), (l, b)], fill=WHITE)
    draw.polygon([(r, t), (r, b), (l, b)], fill=ORANGE)


def picture(with_background=True):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    if with_background:
        g = np.linspace(0, 1, N)[:, None]
        bg = GREEN_TOP[None, None, :] * (1 - g[..., None]) + GREEN_BOTTOM[None, None, :] * g[..., None]
        bg = np.broadcast_to(bg, (N, N, 3)).astype(np.uint8)
        base = Image.fromarray(bg, "RGB").convert("RGBA")
        lines = contours()
        tint = Image.new("RGBA", (N, N), CONTOUR + (0,))
        tint.putalpha(lines.point(lambda v: int(v * 0.42)))
        base = Image.alpha_composite(base, tint)
        # soft light from the top left, darker towards the edges
        y, x = np.mgrid[0:N:8, 0:N:8] / N
        vign = np.clip(1.0 - 0.55 * ((x - 0.35) ** 2 + (y - 0.3) ** 2), 0.55, 1.0)
        v = Image.fromarray((vign * 255).astype(np.uint8)).resize((N, N), Image.BILINEAR)
        dark = Image.new("RGBA", (N, N), (0, 0, 0, 255))
        dark.putalpha(v.point(lambda p: 255 - p))
        base = Image.alpha_composite(base, dark)
        img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
        img.paste(base, (0, 0), rounded_mask(int(N * 0.2)))
        # thin cream edge
        edge = Image.new("RGBA", (N, N), (0, 0, 0, 0))
        ImageDraw.Draw(edge).rounded_rectangle([SS * 6, SS * 6, N - SS * 6, N - SS * 6], radius=int(N * 0.19),
                                               outline=CREAM + (70,), width=SS * 6)
        img = Image.alpha_composite(img, edge)

    c = N / 2
    # shadow under the circle and flag
    sh = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    R = N * 0.34
    w = N * 0.062
    off = N * 0.014
    sd.ellipse([c - R + off, c - R + off * 1.6, c + R + off, c + R + off * 1.6], outline=(0, 0, 0, 120), width=int(w))
    sd.rectangle([c - N * 0.15 + off, c - N * 0.15 + off * 1.6, c + N * 0.2 + off, c + N * 0.15 + off * 1.6], fill=(0, 0, 0, 110))
    sh = sh.filter(ImageFilter.GaussianBlur(N * 0.012))
    img = Image.alpha_composite(img, sh)

    d = ImageDraw.Draw(img)
    d.ellipse([c - R, c - R, c + R, c + R], outline=PURPLE + (255,), width=int(w))
    flag(d, c - N * 0.025, c + N * 0.012, N * 0.31)
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def save(img, rel):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print("wrote", rel)


if __name__ == "__main__":
    icon = picture()
    save(icon, "Assets/Art/Icon/OManagerIcon.png")
    save(icon.resize((512, 512), Image.LANCZOS), "Assets/Resources/Intro/emblem.png")
