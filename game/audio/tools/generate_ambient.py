"""Generates the game's original ambient music loops (no samples, no outside material).

Writes audio/music/descent_day.wav and audio/music/descent_night.wav: slow pad chords over a
low drone, sparse bell notes from a pentatonic scale and a long reverb. Each file loops
seamlessly (the reverb tail is folded back into the start). Deterministic for a seed.

Run from the project folder:  python audio/tools/generate_ambient.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
CHORD_SECONDS = 12.0


def midi(note):
    return 440.0 * 2.0 ** ((note - 69) / 12.0)


def pad(freqs, seconds, brightness, rng):
    """A soft detuned pad: a few harmonics per voice, slow tremolo, smooth envelope."""
    t = np.arange(int(seconds * RATE)) / RATE
    out = np.zeros_like(t)
    for f in freqs:
        for detune in (-0.12, 0.0, 0.12):
            freq = f * 2.0 ** (detune / 12.0)
            phase = rng.uniform(0, 2 * np.pi)
            for h in range(1, 5):
                amp = brightness ** (h - 1) / h
                out += amp * np.sin(2 * np.pi * freq * h * t + phase * h)
    tremolo = 1.0 + 0.15 * np.sin(2 * np.pi * 0.11 * t + rng.uniform(0, 6))
    attack = np.clip(t / (seconds * 0.35), 0, 1)
    release = np.clip((seconds - t) / (seconds * 0.35), 0, 1)
    envelope = np.sin(attack * np.pi / 2) * np.sin(release * np.pi / 2)
    return out * tremolo * envelope / (len(freqs) * 3)


def bell(freq, seconds, decay):
    t = np.arange(int(seconds * RATE)) / RATE
    tone = np.sin(2 * np.pi * freq * t) + 0.4 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t * 3)
    return tone * np.exp(-t * decay) * np.clip(t / 0.01, 0, 1)


def reverb(signal, seconds, rng):
    """Convolution with decaying stereo noise: a long, soft hall."""
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    channels = []
    for _ in range(2):
        impulse = rng.standard_normal(n) * np.exp(-t * (6.9 / seconds))
        # Darken the tail: a moving average acts as a gentle low-pass.
        impulse = np.convolve(impulse, np.ones(8) / 8, mode="same")
        impulse[0] = 0.0
        size = 1 << int(np.ceil(np.log2(len(signal) + n)))
        wet = np.fft.irfft(np.fft.rfft(signal, size) * np.fft.rfft(impulse, size), size)[: len(signal) + n]
        channels.append(wet / np.sqrt(n) * 4.0)
    return np.stack(channels)


def render(name, chords, drone, scale, bells_per_chord, brightness, bell_decay, seed):
    rng = np.random.default_rng(seed)
    length = len(chords) * CHORD_SECONDS
    tail = 8.0
    total = int((length + CHORD_SECONDS + tail) * RATE)
    dry = np.zeros(total)

    # Pads overlap: each chord lasts 1.5 chord lengths and fades into the next.
    for i, chord in enumerate(chords):
        start = int(i * CHORD_SECONDS * RATE)
        voice = pad([midi(n) for n in chord], CHORD_SECONDS * 1.5, brightness, rng)
        dry[start:start + len(voice)] += voice

    t = np.arange(total) / RATE
    dry += 0.35 * np.sin(2 * np.pi * midi(drone) * t) * (0.8 + 0.2 * np.sin(2 * np.pi * 0.05 * t))
    dry += 0.15 * np.sin(2 * np.pi * midi(drone + 7) * t)

    for i in range(len(chords)):
        for _ in range(bells_per_chord):
            at = int((i * CHORD_SECONDS + rng.uniform(0.5, CHORD_SECONDS - 0.5)) * RATE)
            note = bell(midi(rng.choice(scale)), 6.0, bell_decay) * rng.uniform(0.08, 0.16)
            dry[at:at + len(note)] += note[: total - at]

    wet = reverb(dry, 4.0, rng)[:, :total]
    stereo = np.stack([dry, dry]) * 0.55 + wet * 0.6

    # Fold everything past the loop length back onto the start so it loops seamlessly.
    loop = int(length * RATE)
    mixed = stereo[:, :loop].copy()
    overflow = stereo[:, loop:]
    mixed[:, : overflow.shape[1]] += overflow[:, :loop]

    mixed /= np.max(np.abs(mixed)) / 0.7
    data = (mixed.T * 32767).astype(np.int16)
    os.makedirs("audio/music", exist_ok=True)
    path = f"audio/music/{name}.wav"
    with wave.open(path, "wb") as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())
    print(f"{path}: {length:.0f} s")


# D dorian by day: open, a little hopeful. D minor by night: lower, slower, emptier.
render(
    "descent_day",
    chords=[[50, 57, 62, 65, 69], [46, 53, 58, 62, 69], [53, 57, 60, 64, 67], [48, 55, 62, 64, 67],
            [50, 57, 62, 65, 71], [43, 50, 58, 62, 65], [45, 52, 57, 61, 64], [50, 57, 62, 64, 69]],
    drone=38, scale=[74, 76, 77, 79, 81, 84, 86], bells_per_chord=3, brightness=0.45, bell_decay=0.9, seed=7)
render(
    "descent_night",
    chords=[[38, 50, 57, 62, 65], [38, 50, 55, 58, 62], [34, 46, 53, 58, 62], [33, 45, 52, 57, 61],
            [38, 50, 57, 60, 65], [36, 48, 55, 58, 63], [34, 46, 53, 57, 62], [33, 45, 52, 57, 64]],
    drone=26, scale=[62, 65, 67, 69, 72], bells_per_chord=1, brightness=0.3, bell_decay=0.6, seed=11)
