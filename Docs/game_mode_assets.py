#!/usr/bin/env python3
"""Generates the pictures and sounds for the Bears and Move a control game modes (no licences needed).

  Assets/Resources/Modes/Bear.png        a cartoon bear standing up and roaring
  Assets/Resources/Sounds/bear_roar.wav  growl and roar when a runner finds a bear
  Assets/Resources/Sounds/control_whoosh.wav  whoosh and thump when a moved control flies to its new place

Run:  python3 Docs/game_mode_assets.py   (needs numpy and Pillow)
"""
import math
import os
import wave

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PICTURES = os.path.join(ROOT, "Assets", "Resources", "Modes")
SOUNDS = os.path.join(ROOT, "Assets", "Resources", "Sounds")
RATE = 44100
rng = np.random.default_rng(11)


# ---------------------------------------------------------------- picture

def bear(size=512, ss=4):
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = S / 512.0

    def P(*v):
        return [x * s for x in v]

    outline = (59, 35, 18, 255)
    fur = (122, 74, 38, 255)
    fur_dark = (98, 58, 29, 255)
    light = (210, 160, 105, 255)
    w = int(9 * s)

    def ellipse(box, fill, width=w):
        d.ellipse(P(*box), fill=fill, outline=outline, width=width)

    def rotated_ellipse(cx, cy, rx, ry, angle, fill):
        layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ld.ellipse(P(cx - rx, cy - ry, cx + rx, cy + ry), fill=fill, outline=outline, width=w)
        layer = layer.rotate(angle, center=(cx * s, cy * s), resample=Image.BICUBIC)
        img.alpha_composite(layer)

    # shadow on the ground
    d.ellipse(P(120, 452, 392, 500), fill=(0, 0, 0, 70))
    # arms raised up and out, each with a paw and claws at the top
    for side in (-1, 1):
        cx, cy, ry, tilt = 256 + side * 140, 200, 92, 30
        rotated_ellipse(cx, cy, 44, ry, -side * tilt, fur_dark)
        tipx = cx + side * (ry - 12) * math.sin(math.radians(tilt))
        tipy = cy - (ry - 12) * math.cos(math.radians(tilt))
        ellipse((tipx - 34, tipy - 30, tipx + 34, tipy + 30), light, int(6 * s))
        for k in range(3):
            a = math.radians(-side * tilt + (k - 1) * 32)
            bx, by = tipx + 30 * math.sin(a) * 1.0, tipy - 28 * math.cos(a)
            ox, oy = 20 * math.sin(a), -20 * math.cos(a)
            px, py = -oy * 0.35, ox * 0.35
            d.polygon(P(bx + px, by + py, bx - px, by - py, bx + ox, by + oy), fill=(250, 245, 230, 255), outline=outline)
    # legs
    ellipse((150, 380, 238, 480), fur_dark)
    ellipse((274, 380, 362, 480), fur_dark)
    ellipse((160, 440, 232, 482), light, int(6 * s))
    ellipse((280, 440, 352, 482), light, int(6 * s))
    # body and belly
    ellipse((130, 200, 382, 450), fur)
    ellipse((180, 260, 332, 430), light, int(6 * s))
    # ears
    for cx in (162, 350):
        ellipse((cx - 42, 30, cx + 42, 114), fur)
        ellipse((cx - 22, 52, cx + 22, 96), light, int(5 * s))
    # head
    ellipse((136, 50, 376, 262), fur)
    # muzzle with an open, roaring mouth
    ellipse((192, 150, 320, 252), light)
    d.ellipse(P(214, 196, 298, 250), fill=(120, 24, 30, 255), outline=outline, width=int(6 * s))
    d.ellipse(P(232, 226, 280, 250), fill=(214, 84, 96, 255))
    for tx in (226, 286):
        d.polygon(P(tx - 9, 200, tx + 9, 200, tx, 222), fill=(255, 255, 255, 255))
    d.polygon(P(226, 248, 240, 248, 233, 232), fill=(255, 255, 255, 255))
    d.polygon(P(272, 248, 286, 248, 279, 232), fill=(255, 255, 255, 255))
    # nose
    d.ellipse(P(232, 162, 280, 192), fill=(40, 24, 14, 255))
    d.ellipse(P(242, 167, 256, 176), fill=(255, 255, 255, 140))
    # eyes with angry brows
    for cx, lean in ((206, 1), (306, -1)):
        d.ellipse(P(cx - 20, 112, cx + 20, 150), fill=(255, 255, 255, 255), outline=outline, width=int(5 * s))
        d.ellipse(P(cx - 9 + lean * 3, 124, cx + 9 + lean * 3, 146), fill=(25, 18, 12, 255))
        d.ellipse(P(cx - 3 + lean * 3, 127, cx + 3 + lean * 3, 133), fill=(255, 255, 255, 255))
        d.line(P(cx - 26, 100 - lean * 10, cx + 26, 100 + lean * 10), fill=outline, width=int(10 * s))
    return img.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- sounds

def t(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def save(name, x, peak=0.9):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    data = (x * 32767).astype(np.int16)
    os.makedirs(SOUNDS, exist_ok=True)
    with wave.open(os.path.join(SOUNDS, name + ".wav"), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(data.tobytes())


def roar():
    x = t(1.6)
    # pitch rises, then falls: a growl that turns into a roar
    pitch = 70 + 55 * np.sin(np.clip(x / 1.6, 0, 1) * np.pi) ** 1.5
    phase = 2 * np.pi * np.cumsum(pitch) / RATE
    voice = sum(np.sin(phase * k + rng.uniform(0, 6)) / k ** 0.7 for k in range(1, 14))
    # rough, rattling throat
    rattle = 1 + 0.6 * np.sin(2 * np.pi * 31 * x) * (rng.random(len(x)) * 0.5 + 0.5)
    breath = lowpass(rng.normal(0, 1, len(x)), 1400) * 2.5
    sound = voice * rattle + breath
    sound = lowpass(sound, 2200)
    env = np.clip(x / 0.25, 0, 1) * np.clip((1.6 - x) / 0.5, 0, 1)
    return np.tanh(sound * env * 0.8)


def whoosh():
    x = t(1.3)
    noise = rng.normal(0, 1, len(x))
    # a swept band of noise, loud in the middle, then a soft thump when the control lands
    out = np.zeros_like(x)
    centre = 400 + 2600 * np.sin(np.clip(x / 1.1, 0, 1) * np.pi)
    low = lowpass(noise, 300)
    for i in range(0, len(x), 2048):
        seg = slice(i, min(len(x), i + 2048))
        c = float(np.mean(centre[seg]))
        out[seg] = lowpass(noise[seg], c)[: len(x[seg])] - lowpass(noise[seg], c * 0.35)[: len(x[seg])]
    env = np.sin(np.clip(x / 1.1, 0, 1) * np.pi) ** 2
    land = np.zeros_like(x)
    i = int(1.05 * RATE)
    k = np.arange(len(x) - i) / RATE
    land[i:] = np.sin(2 * np.pi * (90 - 40 * k) * k) * np.exp(-k / 0.08) * 1.6 + low[i:] * np.exp(-k / 0.05)
    return out * env * 3 + land


if __name__ == "__main__":
    os.makedirs(PICTURES, exist_ok=True)
    bear().save(os.path.join(PICTURES, "Bear.png"))
    save("bear_roar", roar())
    save("control_whoosh", whoosh(), 0.8)
    print("done")
