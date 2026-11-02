#!/usr/bin/env python3
"""Generates the menu sounds in Assets/Resources/Sounds (no recordings, so no licences needed).

  ui_click   soft wooden click for buttons
  ui_toggle  lighter, higher click for toggles and dropdowns
  ui_tick    tiny tick for sliders and scroll bars
  startup    played once when the game starts: forest birds, the start clock's
             three short beeps and the long start beep, then a warm chord

Run:  python3 Docs/ui_sounds.py
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "Resources", "Sounds")
rng = np.random.default_rng(7)


def t(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def env(n, attack, decay):
    """Attack in seconds, then an exponential decay with the given time constant."""
    x = np.arange(n) / RATE
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(x - attack, 0) / decay)


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def place(buf, sound, at):
    i = int(at * RATE)
    end = min(len(buf), i + len(sound))
    buf[i:end] += sound[:end - i]


def save(name, x, peak=0.9):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    fade = min(len(x), int(0.004 * RATE))
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (x * 32767).astype("<i2")
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())


def click(pitch, length, body):
    n = int(length * RATE)
    x = t(length)
    noise = lowpass(rng.standard_normal(n), 5000) * env(n, 0.0005, 0.004)
    tone = np.sin(2 * np.pi * pitch * x) * env(n, 0.001, length / 4)
    low = np.sin(2 * np.pi * pitch / 2.7 * x) * env(n, 0.001, length / 6) * body
    return noise * 0.6 + tone * 0.5 + low * 0.5


def beep(freq, length, vol=1.0):
    n = int(length * RATE)
    x = t(length)
    shape = np.clip(x / 0.006, 0, 1) * np.clip((length - x) / 0.02, 0, 1)
    # a start clock beep: square-ish, slightly softened
    wave_ = np.sin(2 * np.pi * freq * x) + 0.25 * np.sin(2 * np.pi * 3 * freq * x) + 0.1 * np.sin(2 * np.pi * 5 * freq * x)
    return wave_ * shape * vol


def bird(f0, f1, length, vol):
    n = int(length * RATE)
    x = t(length)
    sweep = f0 + (f1 - f0) * (x / length) ** 0.6
    wobble = 1 + 0.04 * np.sin(2 * np.pi * 38 * x)
    phase = 2 * np.pi * np.cumsum(sweep * wobble) / RATE
    shape = np.sin(np.pi * np.clip(x / length, 0, 1)) ** 2
    return np.sin(phase) * shape * vol


def chord(freqs, length, vol):
    n = int(length * RATE)
    x = t(length)
    out = np.zeros(n)
    for k, f in enumerate(freqs):
        detune = 1 + 0.002 * (k - 1)
        out += np.sin(2 * np.pi * f * detune * x) + 0.3 * np.sin(2 * np.pi * 2 * f * x)
    shape = np.clip(x / 0.25, 0, 1) * np.exp(-np.maximum(x - 0.25, 0) / (length / 3))
    return lowpass(out * shape, 3500) * vol


def startup():
    total = 4.2
    buf = np.zeros(int(total * RATE))
    # forest: soft wind and a few birds
    wind = lowpass(rng.standard_normal(len(buf)), 400)
    wind *= 0.25 * (0.6 + 0.4 * np.sin(2 * np.pi * 0.3 * t(total)))
    buf += wind * np.clip(t(total) / 0.8, 0, 1) * np.clip((total - t(total)) / 1.2, 0, 1)
    for at, f0, f1, length in ((0.10, 3200, 4300, 0.09), (0.24, 3400, 4600, 0.08), (0.36, 4100, 3000, 0.12),
                               (1.05, 2800, 3900, 0.10), (1.18, 3000, 4200, 0.09), (3.3, 3600, 4800, 0.08),
                               (3.45, 3900, 2900, 0.11)):
        place(buf, bird(f0, f1, length, 0.18), at)
    # the start clock: three short beeps, then the long one
    for at in (0.6, 1.1, 1.6):
        place(buf, beep(1000, 0.12, 0.55), at)
    place(buf, beep(1000, 0.55, 0.7), 2.1)
    # off we go: a warm open chord
    place(buf, chord([196.0, 293.66, 392.0, 587.33], 2.0, 0.22), 2.15)
    return buf


if __name__ == "__main__":
    save("ui_click", click(1500, 0.07, 0.8), 0.8)
    save("ui_toggle", click(2300, 0.05, 0.4), 0.7)
    save("ui_tick", click(3200, 0.025, 0.1), 0.5)
    save("startup", startup(), 0.85)
    print("Wrote sounds to", OUT)
