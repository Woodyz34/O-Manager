#!/usr/bin/env python3
"""Makes the intro's sound, Assets/Resources/Intro/intro_sound.wav (no recordings, so no licences needed).

It follows the intro film (Docs/blender_intro.py): a forest morning with birds, footsteps that start
fast and slow down as the runner reaches the control, a rising swell, and at 4.9 s (the moment the
O-MANAGER title lands) a deep hit with the beep of the SI unit, then a warm chord that fades out.

Run:  python3 Docs/intro_sound.py
"""
import os
import sys
import wave

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui_sounds import RATE, beep, bird, chord, env, lowpass, place, t  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Intro", "intro_sound.wav")
TITLE_AT = 4.9
TOTAL = 7.6
rng = np.random.default_rng(3)


def step(vol):
    # A foot on soft forest ground: a dull thump and a little crunch of twigs and needles
    length = 0.16
    n = int(length * RATE)
    x = t(length)
    thump = np.sin(2 * np.pi * (70 + 40 * np.exp(-x / 0.02)) * x) * env(n, 0.002, 0.035)
    crunch = lowpass(rng.standard_normal(n), 2500) * env(n, 0.001, 0.025)
    return (thump * 0.9 + crunch * 0.5) * vol


def hit():
    # The deep hit when the title lands
    length = 2.2
    n = int(length * RATE)
    x = t(length)
    sub = np.sin(2 * np.pi * (42 + 60 * np.exp(-x / 0.05)) * x) * env(n, 0.003, 0.55)
    body = lowpass(rng.standard_normal(n), 900) * env(n, 0.001, 0.12)
    air = lowpass(rng.standard_normal(n), 7000) * env(n, 0.001, 0.4) * 0.25
    return sub * 1.0 + body * 0.6 + air


def swell(length):
    # Rising noise and a low tone climbing towards the hit
    n = int(length * RATE)
    x = t(length)
    k = x / length
    noise = rng.standard_normal(n)
    # brighter as it rises: mix a dark and a bright version
    dark = lowpass(noise, 600)
    bright = lowpass(noise, 4000)
    nz = dark * (1 - k) + bright * k
    f = 55 + 55 * k ** 2
    tone = np.sin(2 * np.pi * np.cumsum(f) / RATE) + 0.4 * np.sin(2 * np.pi * np.cumsum(2 * f) / RATE)
    shape = k ** 2.2
    return (nz * 0.35 + tone * 0.45) * shape


def main():
    buf = np.zeros(int(TOTAL * RATE))
    tt = t(TOTAL)

    # forest bed: wind and birds, quieter once the title is there
    wind = lowpass(rng.standard_normal(len(buf)), 350)
    wind *= 0.22 * (0.7 + 0.3 * np.sin(2 * np.pi * 0.4 * tt))
    buf += wind * np.clip(tt / 0.6, 0, 1) * np.clip((TOTAL - tt) / 1.5, 0, 1)
    for at, f0, f1, length in ((0.30, 3300, 4400, 0.09), (0.45, 3500, 4700, 0.08), (1.4, 4100, 3000, 0.12),
                               (2.2, 2900, 4000, 0.10), (2.33, 3100, 4300, 0.09), (3.6, 3700, 4900, 0.08),
                               (5.9, 3600, 4700, 0.09), (6.05, 3900, 3000, 0.11)):
        place(buf, bird(f0, f1, length, 0.12), at)

    # footsteps: the camera slows like 1 - (1 - t)^2.6, so the steps get further apart until it stops
    at = 0.15
    while at < 4.55:
        run = min(1.0, at / (0.86 * 6.0))
        speed = (1 - run) ** 1.6
        gap = 0.27 / max(0.35, speed)
        place(buf, step(0.55 * (0.5 + 0.5 * speed) * rng.uniform(0.85, 1.1)), at)
        at += gap * rng.uniform(0.95, 1.05)

    # breath of tension before the title
    place(buf, swell(1.6), TITLE_AT - 1.6)

    # the title: hit + the SI unit's beep (high and short) + a warm open chord
    place(buf, hit(), TITLE_AT)
    place(buf, beep(2730, 0.16, 0.5), TITLE_AT + 0.02)
    place(buf, chord([146.83, 220.0, 293.66, 440.0, 587.33], 2.6, 0.35), TITLE_AT + 0.05)

    x = buf / (np.max(np.abs(buf)) + 1e-9) * 0.9
    fade = int(0.4 * RATE)
    x[-fade:] *= np.linspace(1, 0, fade)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with wave.open(OUT, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", OUT)


if __name__ == "__main__":
    main()
