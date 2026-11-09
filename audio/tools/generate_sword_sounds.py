"""Generates the sword's sounds (original, synthesized; no recordings).

Writes audio/combat/:
- sword_swing_<n>: a swoosh, noise swept up and down through a band-pass, for each swing
- sword_hit_<n>: the blade landing on a beast (a dull thud under a short tearing noise)
- sword_block_<n>: a blow taken on the shield (wood knock with a metal ring from the boss)
- sword_draw_0 / sword_sheathe_0: steel sliding out of / into the scabbard
- sword_kick_0: a boot landing (low thump)

Run from the project folder:  python audio/tools/generate_sword_sounds.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
rng = np.random.default_rng(57)
OUT = os.path.join("audio", "combat")


def bandpass(signal, centre, width):
    """A moving band-pass: centre (Hz, per sample) and width as a share of the centre."""
    out = np.zeros_like(signal)
    low = 0.0
    band = 0.0
    for i, x in enumerate(signal):
        f = 2.0 * np.sin(np.pi * min(centre[i], RATE * 0.45) / RATE)
        q = width
        high = x - low - q * band
        band += f * high
        low += f * band
        out[i] = band
    return out


def swing(seconds, peak):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    shape = np.sin(np.pi * t / seconds) ** 2
    centre = 300.0 + peak * shape
    noise = rng.standard_normal(n)
    return bandpass(noise, centre, 0.6) * shape


def hit():
    n = int(RATE * 0.35)
    t = np.arange(n) / RATE
    thud = np.sin(2 * np.pi * (90.0 - 40.0 * t) * t) * np.exp(-t * 14.0)
    tear = bandpass(rng.standard_normal(n), np.full(n, 1800.0), 0.9) * np.exp(-t * 30.0)
    return thud + tear * 0.6


def block(f0):
    n = int(RATE * 0.6)
    t = np.arange(n) / RATE
    wood = sum(g * np.sin(2 * np.pi * f * t) * np.exp(-t * r) for f, g, r in [(f0, 1.0, 22.0), (f0 * 2.4, 0.5, 30.0)])
    ring = sum(g * np.sin(2 * np.pi * f * t) * np.exp(-t * r) for f, g, r in [(1210.0, 0.35, 6.0), (2730.0, 0.2, 8.0), (4120.0, 0.12, 10.0)])
    click = rng.standard_normal(n) * np.exp(-t * 250.0)
    return wood + ring + click * 0.5


def scrape(seconds, rising):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    if rising:
        centre = 2500.0 + 3500.0 * t / seconds
    else:
        centre = 6000.0 - 3500.0 * t / seconds
    env = np.minimum(1.0, t / 0.03) * np.exp(-t * 3.0)
    hiss = bandpass(rng.standard_normal(n), centre, 0.3) * env
    ring = np.sin(2 * np.pi * 3100.0 * t) * np.exp(-np.maximum(0.0, t - seconds * 0.8) * 25.0) * 0.15 * env
    return hiss + ring


def kick():
    n = int(RATE * 0.3)
    t = np.arange(n) / RATE
    return np.sin(2 * np.pi * (70.0 - 30.0 * t) * t) * np.exp(-t * 18.0) + rng.standard_normal(n) * np.exp(-t * 80.0) * 0.4


def write(name, signal, gain=0.9):
    signal = signal / (np.max(np.abs(signal)) + 1e-9) * gain
    pad = np.zeros(int(RATE * 0.03))
    data = (np.concatenate([signal, pad]) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())


def main():
    os.makedirs(OUT, exist_ok=True)
    for i, (seconds, peak) in enumerate([(0.32, 2400.0), (0.38, 1900.0), (0.28, 2900.0)]):
        write(f"sword_swing_{i}", swing(seconds, peak), 0.7)
    for i in range(2):
        write(f"sword_hit_{i}", hit())
    for i, f0 in enumerate([180.0, 205.0]):
        write(f"sword_block_{i}", block(f0))
    write("sword_draw_0", scrape(0.55, True), 0.6)
    write("sword_sheathe_0", scrape(0.5, False), 0.6)
    write("sword_kick_0", kick())


if __name__ == "__main__":
    main()
