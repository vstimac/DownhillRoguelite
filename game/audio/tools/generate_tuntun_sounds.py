"""Generates Tuntun's sounds (original, synthesized; no recordings).

Writes audio/creatures/:
- tuntun_knock_<n>: a hollow wooden knock, one per footstep (the log's "tung")
- tuntun_call_0: "tung tung tung" — three knocks, rising, when he appears
- tuntun_bonk_<n>: the bat landing on the carrier

A knock is a few damped resonances of a hollow log (a low body mode and two shell modes) struck by
a short noise click. Run from the project folder:  python audio/tools/generate_tuntun_sounds.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
rng = np.random.default_rng(31)
OUT = os.path.join("audio", "creatures")


def knock(f0, seconds=0.32, decay=14.0, click=0.5):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    modes = [(f0, 1.0, decay), (f0 * 2.31, 0.45, decay * 1.6), (f0 * 3.9, 0.2, decay * 2.4)]
    tone = sum(gain * np.sin(2 * np.pi * freq * t) * np.exp(-t * rate) for freq, gain, rate in modes)
    hit = rng.standard_normal(n) * np.exp(-t * 300.0) * click
    return tone + hit


def bonk(f0):
    # A duller, heavier knock with a thud underneath: wood on a body.
    n = int(RATE * 0.45)
    t = np.arange(n) / RATE
    thud = np.sin(2 * np.pi * 70.0 * t) * np.exp(-t * 18.0)
    return knock(f0, 0.45, 20.0, 0.9) * 0.8 + thud


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
    for i, f0 in enumerate([300, 330, 285]):
        write(f"tuntun_knock_{i}", knock(f0), 0.8)
    gap = np.zeros(int(RATE * 0.12))
    write("tuntun_call_0", np.concatenate([knock(280), gap, knock(300), gap, knock(340, 0.5)]))
    for i, f0 in enumerate([210, 230]):
        write(f"tuntun_bonk_{i}", bonk(f0))
    print("wrote Tuntun's sounds to", OUT)


if __name__ == "__main__":
    main()
