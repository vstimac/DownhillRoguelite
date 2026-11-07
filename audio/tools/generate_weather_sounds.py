"""Generates the weather ambience loops (original, synthesized noise; no recordings).

Writes audio/ambience/wind.wav and audio/ambience/rain.wav. Both are built in the frequency
domain (shaped noise through an inverse FFT), which makes them loop perfectly; gust swells and
raindrop clicks are placed periodically so the loop point is seamless too.

Run from the project folder:  python audio/tools/generate_weather_sounds.py
Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
SECONDS = 30
N = RATE * SECONDS


def shaped_noise(rng, shape):
    freqs = np.fft.rfftfreq(N, 1.0 / RATE)
    magnitude = shape(np.maximum(freqs, 1.0))
    phase = rng.uniform(0, 2 * np.pi, len(freqs))
    spectrum = magnitude * np.exp(1j * phase)
    spectrum[0] = 0
    signal = np.fft.irfft(spectrum, N)
    return signal / np.max(np.abs(signal))


def periodic_swell(rng, cycles, depth):
    """Slow volume swells with a whole number of cycles in the loop, so they loop too."""
    t = np.arange(N) / N
    swell = np.zeros(N)
    for c in cycles:
        swell += np.sin(2 * np.pi * c * t + rng.uniform(0, 2 * np.pi))
    swell = (swell - swell.min()) / (swell.max() - swell.min())
    return 1.0 - depth + depth * swell


def save(name, signal, peak):
    stereo = np.stack([signal, np.roll(signal, RATE // 50)])
    stereo = stereo / np.max(np.abs(stereo)) * peak
    data = (stereo.T * 32767).astype(np.int16)
    os.makedirs("audio/ambience", exist_ok=True)
    path = f"audio/ambience/{name}.wav"
    with wave.open(path, "wb") as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())
    print(f"{path}: {SECONDS} s")


rng = np.random.default_rng(3)

# Wind: deep rumble plus a whistling band, swelling in gusts.
rumble = shaped_noise(rng, lambda f: 1.0 / f ** 0.9 * np.exp(-f / 500.0))
whistle = shaped_noise(rng, lambda f: np.exp(-((f - 700.0) / 250.0) ** 2))
wind = (rumble * 0.8 + whistle * 0.25) * periodic_swell(rng, [1, 2, 3, 5], 0.7)
save("wind", wind, 0.8)

# Rain: a soft hiss plus many small drop clicks.
hiss = shaped_noise(rng, lambda f: np.exp(-((np.log(f) - np.log(3000.0)) / 0.9) ** 2))
drops = np.zeros(N)
click_length = int(RATE * 0.012)
click_t = np.arange(click_length) / RATE
for _ in range(SECONDS * 120):
    at = rng.integers(0, N)
    pitch = rng.uniform(1500.0, 5000.0)
    click = np.sin(2 * np.pi * pitch * click_t) * np.exp(-click_t * 400.0) * rng.uniform(0.1, 0.6)
    index = (at + np.arange(click_length)) % N
    drops[index] += click
rain = hiss * 0.5 + drops * 0.6
save("rain", rain, 0.7)
