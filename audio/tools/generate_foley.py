"""Generates footsteps per surface and a campfire loop (original, synthesized; no recordings).

Writes audio/footsteps/<surface>_<n>.wav (grass, rock, scree, snow, mud, wood; 4 variants each)
and audio/ambience/fire.wav (a seamless crackling loop).

Run from the project folder:  python audio/tools/generate_foley.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
rng = np.random.default_rng(21)


def band_noise(n, low, high, tilt=0.0):
    spectrum = np.fft.rfft(rng.standard_normal(n))
    freqs = np.fft.rfftfreq(n, 1.0 / RATE)
    mask = (freqs >= low) & (freqs <= high)
    shaped = spectrum * mask * np.maximum(freqs, 1.0) ** tilt
    signal = np.fft.irfft(shaped, n)
    return signal / (np.max(np.abs(signal)) + 1e-9)


def env(n, attack, decay):
    t = np.arange(n) / RATE
    return np.clip(t / attack, 0, 1) * np.exp(-t * decay)


def tone(n, freq_from, freq_to, decay):
    t = np.arange(n) / RATE
    freq = np.linspace(freq_from, freq_to, n)
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    return np.sin(phase) * np.exp(-t * decay)


def grains(n, count, low, high, decay):
    out = np.zeros(n)
    length = int(RATE * 0.01)
    t = np.arange(length) / RATE
    for _ in range(count):
        at = int(rng.uniform(0, n * 0.7))
        pitch = rng.uniform(low, high)
        click = np.sin(2 * np.pi * pitch * t) * np.exp(-t * decay) * rng.uniform(0.3, 1.0)
        end = min(n, at + length)
        out[at:end] += click[: end - at]
    return out


def step(surface):
    n = int(RATE * 0.3)
    if surface == "grass":
        s = band_noise(n, 300, 3500, -0.3) * env(n, 0.01, 18)
    elif surface == "rock":
        s = tone(n, 140, 90, 45) * 0.8 + band_noise(n, 2000, 8000) * env(n, 0.001, 90) * 0.4
    elif surface == "scree":
        s = grains(n, 40, 1500, 6000, 300) * env(n, 0.005, 9) + band_noise(n, 500, 4000) * env(n, 0.005, 25) * 0.4
    elif surface == "snow":
        crunch = band_noise(n, 800, 5000) * (0.5 + 0.5 * np.sign(np.sin(np.arange(n) / RATE * 2 * np.pi * rng.uniform(60, 90))))
        s = crunch * env(n, 0.02, 12)
    elif surface == "mud":
        s = tone(n, 260, 70, 12) * 0.6 + band_noise(n, 100, 1200, -0.5) * env(n, 0.02, 14) * 0.7
    else:  # wood
        s = tone(n, 110, 85, 30) * 0.8 + tone(n, 420, 380, 60) * 0.35 + band_noise(n, 1500, 6000) * env(n, 0.001, 120) * 0.2
    fade = np.clip((n - np.arange(n)) / (RATE * 0.03), 0, 1)
    return s * fade


def save(path, signal, peak, channels=1):
    signal = signal / (np.max(np.abs(signal)) + 1e-9) * peak
    if channels == 2:
        signal = np.stack([signal, np.roll(signal, RATE // 40)]).T
    data = (signal * 32767).astype(np.int16)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as out:
        out.setnchannels(channels)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())


for surface in ["grass", "rock", "scree", "snow", "mud", "wood"]:
    for i in range(4):
        save(f"audio/footsteps/{surface}_{i}.wav", step(surface), 0.8)
print("footsteps: 24 files")

# Campfire: a soft roar plus pops and crackles, wrapped so it loops.
seconds = 20
n = RATE * seconds
roar_spectrum = np.fft.rfft(rng.standard_normal(n))
freqs = np.fft.rfftfreq(n, 1.0 / RATE)
roar = np.fft.irfft(roar_spectrum * np.exp(-freqs / 400.0), n)
roar /= np.max(np.abs(roar))
fire = roar * 0.25
for _ in range(seconds * 25):
    at = rng.integers(0, n)
    length = int(RATE * rng.uniform(0.003, 0.02))
    t = np.arange(length) / RATE
    pop = band_noise(length, 1000, 9000) * np.exp(-t * rng.uniform(150, 600)) * rng.uniform(0.1, 1.0) ** 2
    index = (at + np.arange(length)) % n
    fire[index] += pop
save("audio/ambience/fire.wav", fire, 0.7, channels=2)
print("audio/ambience/fire.wav: 20 s")
