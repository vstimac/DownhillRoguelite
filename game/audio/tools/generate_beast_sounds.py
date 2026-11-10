"""Generates the night beasts' voices (original, synthesized; no recordings).

Writes audio/creatures/<beast>_<kind>_<n>.wav:
- she_wolf: growl (the warning before a charge), snarl (the charge, stealing food), howl (stalking, far off)
- leopard: growl, snarl, chuff (stalking)
- lion: growl, snarl, roar (stalking)

A voice is a buzzing source (a sawtooth with jitter, the vocal folds) shaped by formant filters
(the throat and mouth) plus breath noise, with a rough tremolo for growls. Lower and slower for
bigger cats. Run from the project folder:  python audio/tools/generate_beast_sounds.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
rng = np.random.default_rng(77)
OUT = os.path.join("audio", "creatures")


def jitter(n, amount, speed):
    """Slow random wobble, ±amount, changing about `speed` times a second."""
    points = max(4, int(n / RATE * speed) + 2)
    knots = rng.uniform(-amount, amount, points)
    return np.interp(np.linspace(0, points - 1, n), np.arange(points), knots)


def source(n, f0, f1, wobble, roughness):
    """A sawtooth sliding from f0 to f1 Hz with pitch jitter and a growly flutter."""
    freq = np.linspace(f0, f1, n) * (1.0 + jitter(n, wobble, 9.0))
    phase = np.cumsum(freq) / RATE
    saw = 2.0 * (phase % 1.0) - 1.0
    t = np.arange(n) / RATE
    flutter = 1.0 + roughness * np.sin(2 * np.pi * (28.0 + jitter(n, 8.0, 5.0)) * t)
    return saw * flutter


def formant(signal, centres, widths, gains):
    """Shapes `signal` with resonances (FFT band peaks): the throat and the open mouth."""
    n = len(signal)
    spectrum = np.fft.rfft(signal)
    freqs = np.fft.rfftfreq(n, 1.0 / RATE)
    shape = np.full_like(freqs, 0.04)
    for centre, width, gain in zip(centres, widths, gains):
        shape += gain * np.exp(-0.5 * ((freqs - centre) / width) ** 2)
    return np.fft.irfft(spectrum * shape, n)


def breath(n, low, high):
    spectrum = np.fft.rfft(rng.standard_normal(n))
    freqs = np.fft.rfftfreq(n, 1.0 / RATE)
    signal = np.fft.irfft(spectrum * ((freqs >= low) & (freqs <= high)), n)
    return signal / (np.max(np.abs(signal)) + 1e-9)


def envelope(n, attack, release, swell=0.0):
    t = np.arange(n) / RATE
    length = n / RATE
    rise = np.clip(t / attack, 0, 1)
    fall = np.clip((length - t) / release, 0, 1)
    middle = 1.0 + swell * np.sin(np.pi * np.clip(t / length, 0, 1))
    return rise * fall * middle


def voice(seconds, f0, f1, formants, wobble, roughness, breathiness, attack, release, swell=0.0):
    n = int(RATE * seconds)
    centres, widths, gains = formants
    tone = formant(source(n, f0, f1, wobble, roughness), centres, widths, gains)
    tone /= np.max(np.abs(tone)) + 1e-9
    air = breath(n, 300, 5000) * breathiness
    return (tone + air) * envelope(n, attack, release, swell)


def write(name, signal, gain=0.9):
    signal = signal / (np.max(np.abs(signal)) + 1e-9) * gain
    pad = np.zeros(int(RATE * 0.05))
    data = (np.concatenate([signal, pad]) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())


# Formants: (centres Hz, widths Hz, gains). A wolf's throat is smaller and higher than a lion's.
WOLF_GROWL = ([380, 900, 2300], [120, 220, 500], [1.0, 0.6, 0.25])
WOLF_HOWL = ([600, 1150, 2600], [150, 200, 400], [1.0, 0.7, 0.2])
LEOPARD = ([300, 750, 1900], [110, 200, 450], [1.0, 0.55, 0.25])
LION = ([220, 560, 1500], [90, 160, 400], [1.0, 0.6, 0.3])


def growl(formants, f0, seconds, roughness=0.7):
    return voice(seconds, f0, f0 * 0.92, formants, 0.06, roughness, 0.35, 0.25, 0.35, swell=0.3)


def snarl(formants, f0):
    # A sharp rise then a bark-like burst: the attack.
    first = voice(0.35, f0 * 1.6, f0 * 2.3, formants, 0.08, 0.5, 0.6, 0.02, 0.1)
    second = voice(0.55, f0 * 2.1, f0 * 1.2, formants, 0.1, 0.8, 0.5, 0.01, 0.25)
    return np.concatenate([first, np.zeros(int(RATE * 0.05)), second])


def howl(f0):
    # Rises, holds with a slow vibrato, falls away: heard from far off while she stalks.
    n = int(RATE * 3.2)
    t = np.arange(n) / RATE
    contour = np.interp(t, [0, 0.5, 2.2, 3.2], [f0 * 0.7, f0, f0 * 1.05, f0 * 0.6])
    vibrato = 1.0 + 0.015 * np.sin(2 * np.pi * 5.0 * t)
    phase = np.cumsum(contour * vibrato) / RATE
    tone = np.sin(2 * np.pi * phase) + 0.35 * np.sin(4 * np.pi * phase) + 0.15 * np.sin(6 * np.pi * phase)
    centres, widths, gains = WOLF_HOWL
    tone = formant(tone, centres, widths, gains)
    tone /= np.max(np.abs(tone)) + 1e-9
    return (tone + breath(n, 800, 4000) * 0.08) * envelope(n, 0.4, 0.9)


def roar(f0):
    return voice(2.4, f0 * 1.4, f0 * 0.8, LION, 0.08, 0.9, 0.5, 0.15, 0.9, swell=0.6)


def chuff(f0):
    # A leopard's sawing call: a few hoarse breaths in and out.
    parts = []
    for i in range(5):
        parts.append(voice(0.22, f0 * 1.3, f0, LEOPARD, 0.1, 0.9, 0.9, 0.02, 0.12))
        parts.append(np.zeros(int(RATE * rng.uniform(0.12, 0.2))))
    return np.concatenate(parts)


def main():
    os.makedirs(OUT, exist_ok=True)
    for i, (f0, length) in enumerate([(92, 1.6), (84, 1.9), (100, 1.4)]):
        write(f"she_wolf_growl_{i}", growl(WOLF_GROWL, f0, length))
    for i, f0 in enumerate([150, 165]):
        write(f"she_wolf_snarl_{i}", snarl(WOLF_GROWL, f0))
    for i, f0 in enumerate([420, 470]):
        write(f"she_wolf_howl_{i}", howl(f0), 0.8)
    for i, (f0, length) in enumerate([(70, 1.7), (64, 2.0)]):
        write(f"leopard_growl_{i}", growl(LEOPARD, f0, length))
    for i, f0 in enumerate([120, 132]):
        write(f"leopard_snarl_{i}", snarl(LEOPARD, f0))
    write("leopard_chuff_0", chuff(95))
    for i, (f0, length) in enumerate([(52, 2.0), (48, 2.3)]):
        write(f"lion_growl_{i}", growl(LION, f0, length, 0.9))
    for i, f0 in enumerate([95, 104]):
        write(f"lion_snarl_{i}", snarl(LION, f0))
    write("lion_roar_0", roar(70))
    print("wrote", len(os.listdir(OUT)), "files to", OUT)


if __name__ == "__main__":
    main()
