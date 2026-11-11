"""Generates the giant spider's voice (original, synthesized; no recordings).

Writes audio/creatures/spider_<kind>_<n>.wav:
- hiss: the warning before it lunges (a long breathy hiss that swells)
- shriek: the lunge (a harsh, rising screech over a burst of hiss)
- chitter: while it stalks, far off (bursts of dry clicks, like mandibles)
Also audio/creatures/web_tear_<n>.wav: a web torn as the carrier walks into it (threads snapping
over a soft swish).

Hiss is band-limited noise; the shriek a buzzing sawtooth through high formants; the chitter a
string of very short noise clicks in irregular bursts. Run from the project folder:
python audio/tools/generate_spider_sounds.py   Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
rng = np.random.default_rng(1307)
OUT = os.path.join("audio", "creatures")


def write(name, signal, gain=0.85):
    signal = signal / (np.max(np.abs(signal)) + 1e-9) * gain
    pad = np.zeros(int(RATE * 0.05))
    data = (np.concatenate([signal, pad]) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())


def band(signal, low, high):
    """Keeps only low..high Hz (a soft-edged mask in the spectrum)."""
    spectrum = np.fft.rfft(signal)
    freqs = np.fft.rfftfreq(len(signal), 1.0 / RATE)
    mask = np.clip((freqs - low * 0.8) / (low * 0.4 + 1e-9), 0, 1) * np.clip((high * 1.2 - freqs) / (high * 0.4 + 1e-9), 0, 1)
    return np.fft.irfft(spectrum * mask, len(signal))


def envelope(n, attack, release, swell=0.0):
    t = np.arange(n) / RATE
    length = n / RATE
    env = np.minimum(1.0, t / max(attack, 1e-4)) * np.minimum(1.0, (length - t) / max(release, 1e-4))
    return np.clip(env, 0, 1) * (1.0 + swell * t / length)


def hiss(seconds, low, high):
    n = int(RATE * seconds)
    noise = band(rng.standard_normal(n), low, high)
    # A slow flutter, as the breath catches.
    t = np.arange(n) / RATE
    flutter = 1.0 + 0.25 * np.sin(2 * np.pi * rng.uniform(5, 9) * t)
    return noise * flutter * envelope(n, 0.12, 0.3, swell=0.6)


def shriek(f0):
    n = int(RATE * 0.6)
    t = np.arange(n) / RATE
    contour = np.interp(t, [0, 0.15, 0.6], [f0, f0 * 1.8, f0 * 1.3])
    phase = np.cumsum(contour * (1.0 + 0.03 * rng.standard_normal(n))) / RATE
    saw = 2.0 * (phase - np.floor(phase + 0.5))
    harsh = band(saw, 1800, 6500) + band(saw, 900, 1400) * 0.5
    harsh /= np.max(np.abs(harsh)) + 1e-9
    burst = hiss(0.6, 2500, 9000)
    burst /= np.max(np.abs(burst)) + 1e-9
    return (harsh * 0.8 + burst * 0.5) * envelope(n, 0.01, 0.25)


def chitter():
    n = int(RATE * 1.6)
    out = np.zeros(n)
    at = int(RATE * 0.05)
    while at < n - RATE * 0.05:
        clicks = rng.integers(4, 9)
        for _ in range(clicks):
            length = int(RATE * rng.uniform(0.004, 0.009))
            click = band(rng.standard_normal(length * 4), 1500, 5500)[:length] * np.exp(-np.arange(length) / (length * 0.3))
            if at + length < n:
                out[at:at + length] += click * rng.uniform(0.6, 1.0)
            at += int(RATE * rng.uniform(0.025, 0.06))
        at += int(RATE * rng.uniform(0.18, 0.35))
    return out


def web_tear():
    n = int(RATE * 0.45)
    t = np.arange(n) / RATE
    # The swish of the legs through it: soft, airy noise that rises and falls.
    swish = band(rng.standard_normal(n), 1200, 6000) * np.sin(np.pi * t / t[-1]) ** 2 * 0.35
    out = swish
    # Threads snapping: a handful of tiny bright ticks, close together at the start.
    for _ in range(rng.integers(6, 11)):
        at = int(RATE * rng.uniform(0.0, 0.25) ** 1.5 * 1.6)
        length = int(RATE * rng.uniform(0.002, 0.005))
        tick = band(rng.standard_normal(length * 4), 3000, 9000)[:length] * np.exp(-np.arange(length) / (length * 0.25))
        if at + length < n:
            out[at:at + length] += tick * rng.uniform(0.5, 1.0)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    write("spider_hiss_0", hiss(1.3, 2000, 8000))
    write("spider_hiss_1", hiss(1.6, 1600, 7000))
    write("spider_shriek_0", shriek(420))
    write("spider_shriek_1", shriek(380))
    write("spider_chitter_0", chitter(), 0.7)
    write("spider_chitter_1", chitter(), 0.7)
    write("web_tear_0", web_tear(), 0.6)
    write("web_tear_1", web_tear(), 0.6)
    print("wrote the spider's voice to", OUT)


if __name__ == "__main__":
    main()
