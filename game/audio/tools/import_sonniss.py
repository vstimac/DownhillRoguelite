"""Cuts the few sounds the game uses out of the Sonniss #GameAudioGDC 2026 bundle.

Usage (from game/, with numpy, scipy and soundfile installed):
    python -I audio/tools/import_sonniss.py "<bundle folder>"            # cut everything in CUTS
    python -I audio/tools/import_sonniss.py --measure <file> [<file> ...]  # print loudness of any WAV/OGG

The team's own recordings always come first (audio/original_recordings.csv); this only fills in
places that had no sound or a synthesized/library placeholder. Only the cut clips are written, as
44.1 kHz Ogg Vorbis, under audio/sonniss/; the bundle itself stays out of the repository.

Each clip is cut at fixed times, mixed to mono (one-shots, heard in 3D) or kept stereo (beds), sped
up or slowed down if asked (a plain resample: pitch and length together), given short fades (loops
get a crossfade of their tail into their head so they repeat without a click) and brought to a
loudness target, measured as integrated loudness (ITU-R BS.1770 K-weighting with the -70/-10 LU
gates), with a peak ceiling. Every clip is listed in ../docs/audio/sonniss_sources.csv: library,
original file, cut times, processing and the measured result. The licence lets us use and modify
the sounds in the game without credit; we list the sources anyway.
"""
import csv
import glob
import math
import os
import sys

import numpy as np
from scipy import ndimage, signal
import soundfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import riffwav  # noqa: E402

RATE = 44100
OUT_DIR = "audio/sonniss"
SOURCES_CSV = "../docs/audio/sonniss_sources.csv"
PEAK_CEILING_DB = -1.0
SHOT_LIMIT_DB = 6.0  # one-shots may be peak-limited this much to reach their loudness (loops: half)
LIMIT_WINDOW_S = 0.005
SHOT_QUALITY = 0.5   # Vorbis compression level (0 best .. 1 smallest)
LOOP_QUALITY = 0.62
BED_QUALITY = 0.8    # quiet stereo beds under the music: smallest files

# (output under audio/sonniss/, what it is for, source file glob, start s, end s, options)
# options: kind "shot" (mono one-shot, default) / "loop" (crossfaded loop) / "bed" (stereo loop);
#          lufs target, speed (resample factor: <1 lower and longer), lowpass Hz, xfade s, fade s,
#          limit: how many dB of peaks may be limited to reach the target (crackles and clicks need more).
CUTS = [
    # Sword and shield (were synthesized by generate_sword_sounds.py).
    ("combat/sword_swing_0.ogg", "sword swing", "*SWING SCRAPE Swift Melee Weapon Swing*", 0.0, 0.85, {"lufs": -17}),
    ("combat/sword_swing_1.ogg", "sword swing", "*Sword Slice Special Move Layered Swish*", 0.15, 1.0, {"lufs": -17, "fade": 0.25}),
    ("combat/sword_swing_2.ogg", "sword swing", "*SWING SCRAPE Swift Melee Weapon Swing*", 0.0, 0.85, {"lufs": -17, "speed": 0.88}),
    ("combat/sword_hit_0.ogg", "sword cuts into a foe", "*Sword Slide Cuts, Metallic, Impact*", 4.30, 4.85, {"lufs": -18}),
    ("combat/sword_hit_1.ogg", "sword cuts into a foe", "*Sword Slide Cuts, Metallic, Impact*", 10.78, 11.40, {"lufs": -18}),
    ("combat/sword_hit_2.ogg", "sword cuts into a foe", "*Hit Blood Spill Splat Wood Impact*", 0.0, 0.33, {"lufs": -18}),
    ("combat/shield_block_0.ogg", "a blow caught on the shield", "*Spear And Stick Impact, Wooden*", 2.55, 2.95, {"lufs": -20}),
    ("combat/shield_block_1.ogg", "a blow caught on the shield", "*Spear And Stick Impact, Wooden*", 0.62, 1.00, {"lufs": -20}),
    ("combat/shield_block_2.ogg", "a blow caught on the shield (metal ring)", "*METAL SWING HIT Weapon Swing To Metallic Body*", 0.05, 1.35, {"lufs": -21, "fade": 0.6}),
    ("combat/body_hit_0.ogg", "kick, a foe's blow landing", "*4 x Punch, Body*", 0.15, 0.55, {"lufs": -19}),
    ("combat/body_hit_1.ogg", "kick, a foe's blow landing", "*4 x Punch, Body*", 1.12, 1.60, {"lufs": -19}),
    ("combat/body_hit_2.ogg", "kick, a foe's blow landing", "*4 x Punch, Body*", 3.16, 3.60, {"lufs": -19}),
    # Beasts and enemies (were synthesized, or the jade glutton borrowed the lion's).
    ("creatures/wolf_growl_0.ogg", "she-wolf growl", "*Creature Werewolf Growl Menacing*", 0.15, 2.9, {"lufs": -15, "fade": 0.4}),
    ("creatures/wolf_growl_1.ogg", "she-wolf growl", "*Creature Werewolf Growl Menacing*", 2.9, 5.8, {"lufs": -15, "fade": 0.6}),
    ("creatures/lion_roar_0.ogg", "lion roar", "*Large Herbivore Roar 01*", 4.85, 9.6, {"lufs": -14, "fade": 1.2}),
    ("creatures/lion_roar_1.ogg", "lion roar", "*ANMLRept_T Rex*", 0.1, 4.2, {"lufs": -14, "fade": 1.0}),
    ("creatures/leopard_snarl_0.ogg", "leopard snarl", "*Raptor Flair*", 0.0, 1.7, {"lufs": -13, "fade": 0.4}),
    ("creatures/spider_shriek_0.ogg", "giant spider shriek", "*Insectoid Creature Tremble Attack*", 0.0, 2.3, {"lufs": -13, "fade": 0.5}),
    ("creatures/glutton_call_0.ogg", "jade glutton call (chewing)", "*Dinosaur Eating Meat 01*", 0.75, 3.8, {"limit": 10, "lufs": -15, "fade": 0.6}),
    ("creatures/glutton_call_1.ogg", "jade glutton call (chewing)", "*Dinosaur Eating Meat 01*", 6.6, 9.4, {"limit": 10, "lufs": -15, "fade": 0.6}),
    ("creatures/glutton_hurt_0.ogg", "jade glutton hurt", "*Ethereal Entity Grim Pain Long*", 0.0, 1.65, {"lufs": -14, "fade": 0.4}),
    ("creatures/glutton_hurt_1.ogg", "jade glutton hurt", "*Sea Beast Creature Pain Intense Yell*", 0.05, 2.2, {"lufs": -14, "fade": 0.6}),
    ("creatures/glutton_attack_0.ogg", "jade glutton attack cry", "*Orc Male Attack Long Heavy Hit*", 0.0, 2.5, {"lufs": -13, "fade": 0.5}),
    ("creatures/web_tear_0.ogg", "spider web torn", "*VelcroRip29*", 0.10, 0.41, {"lufs": -18}),
    ("creatures/web_snare_0.ogg", "caught in a web", "*VelcroSqueeze01*", 0.30, 1.6, {"lufs": -21, "fade": 0.4}),
    # Fire: station fires (were synthesized) and the torch (had no sound).
    ("fire/campfire_loop.ogg", "station fire", "24 Campfire, Dropping Fresh Pine Branches*", 30.0, 60.0, {"limit": 10, "kind": "loop", "lufs": -23, "xfade": 2.0}),
    ("fire/torch_loop.ogg", "torch burning in the hand", "*FIREBurn_Loop Elements Fire Crackling*", 0.0, 12.3, {"kind": "loop", "lufs": -24, "xfade": 1.0, "lowpass": 9000}),
    ("fire/torch_light.ogg", "torch catches", "*FIREWhsh_Whoosh Fire Deep Growl*", 0.12, 1.25, {"lufs": -20, "fade": 0.5, "lowpass": 7000}),
    ("fire/torch_snuff.ogg", "torch snuffed", "42 Campfire, Putting Out Fire, Water*", 0.0, 2.0, {"lufs": -22, "fade": 0.8, "trim": True}),
    # Weather (wind and rain were synthesized; there was no thunder).
    ("weather/wind_loop.ogg", "wind", "*Eye Of The Storm*", 40.0, 75.0, {"kind": "bed", "lufs": -23, "xfade": 3.0}),
    ("weather/rain_loop.ogg", "rain", "*StormAmbience11*", 60.0, 90.0, {"kind": "bed", "lufs": -16, "xfade": 3.0}),
    ("weather/thunder_0.ogg", "thunder, distant", "*THUN_Interior Thunder Rumble*", 2.1, 12.6, {"lufs": -17, "fade": 3.0}),
    ("weather/thunder_1.ogg", "thunder, a crash nearby", "*Texas Rain Thunder Initial Crash Boom*", 3.0, 11.0, {"lufs": -16, "fade": 3.0}),
    # The hand cart (had no sound).
    ("foley/cart_roll_loop.ogg", "hand cart rolling", "*WIR028*", 1.5, 13.5, {"kind": "loop", "lufs": -22, "xfade": 1.0, "speed": 0.75, "lowpass": 6000}),
    ("foley/cart_handle.ogg", "taking or letting go of the cart's handles", "*Handle_Movement15*", 0.0, 0.7, {"limit": 10, "lufs": -20}),
    # Pack, gear and items.
    ("foley/pack_open.ogg", "opening the pack", "*Inventory Open Flip Cloth Canvas Bag*", 0.03, 0.69, {"lufs": -21}),
    ("foley/cloth_0.ogg", "putting on or taking off clothes", "*ClothMovement24*", 0.05, 1.15, {"lufs": -23, "fade": 0.3}),
    ("foley/cloth_1.ogg", "putting on or taking off clothes", "*ClothMovement29*", 0.0, 1.3, {"lufs": -23, "fade": 0.3}),
    ("foley/mend_snip.ogg", "mending (scissors snip)", "*Hair Scissor, Snips 4*", 1.2, 1.95, {"limit": 10, "lufs": -23}),
    ("foley/page_turn.ogg", "reading a found text", "*Encyclopedia Glossy Page Turn Muted*", 0.0, 0.55, {"lufs": -22}),
    ("foley/launch_whoosh.ogg", "thrown through the air", "*Wind, Rush, Whoosh, Long x5*", 0.0, 1.05, {"lufs": -19, "fade": 0.3}),
    ("foley/music_box.ogg", "slept through the night", "*Music Box A 013*", 0.0, 2.8, {"lufs": -22, "fade": 0.8}),
    # Interface and stations.
    ("ui/achievement.ogg", "achievement unlocked", "*Kalimba 3 Up*", 0.0, 0.55, {"lufs": -16}),
    ("ui/station_bell.ogg", "a station reached (a distant bell)", "04 Church Bells, Near Distance*", 1.55, 8.0, {"lufs": -20, "fade": 2.5, "lowpass": 5000}),
    ("ui/run_end.ogg", "the end of a run", "*Transition Braam Slow Dark Creepy*", 0.0, 4.5, {"lufs": -21, "fade": 1.5}),
    # One ambience bed per circle (there were none).
    ("ambience/circle_0_limbo.ogg", "circle 1, Limbo: a high meadow, pipits and wind", "*Meadow Pipits calling many Insects*", 10.0, 42.0, {"kind": "bed", "lufs": -26, "xfade": 4.0}),
    ("ambience/circle_1_lust.ogg", "circle 2, Lust: a moaning wind that never stops", "*ChimneyWind05*", 13.5, 45.5, {"kind": "bed", "lufs": -26, "xfade": 4.0}),
    ("ambience/circle_2_gluttony.ogg", "circle 3, Gluttony: cold rain and hail", "*Rain Light Interior Hail Storm*", 10.0, 42.0, {"kind": "bed", "lufs": -26, "xfade": 4.0}),
    ("ambience/circle_3_greed.ogg", "circle 4, Greed: great weights dragged", "*Large Metal Box, Drag, Geofon*", 5.0, 37.0, {"kind": "bed", "lufs": -27, "xfade": 4.0, "lowpass": 4000}),
    ("ambience/circle_4_wrath.ogg", "circle 5, Wrath: the marsh of the Styx", "*Small Waves sloshing onto Rock Slabs*", 10.0, 42.0, {"kind": "bed", "lufs": -26, "xfade": 4.0}),
    ("ambience/circle_5_heresy.ogg", "circle 6, Heresy: burning tombs", "*Fire Crackling, Popping, Witch's Cauldron*", 20.0, 52.0, {"limit": 10, "kind": "bed", "lufs": -26, "xfade": 4.0, "lowpass": 9000}),
    ("ambience/circle_6_violence.ogg", "circle 7, Violence: the wood of the suicides", "*Dark Loop Mystic Forest Tonal*", 0.0, 32.0, {"kind": "bed", "lufs": -27, "xfade": 4.0}),
    ("ambience/circle_7_fraud.ogg", "circle 8, Fraud: the evil pits", "*Evil Spell Ambience*", 30.0, 62.0, {"kind": "bed", "lufs": -27, "xfade": 4.0}),
    ("ambience/circle_8_treachery.ogg", "circle 9, Treachery: the frozen lake", "*ice field cracking up*", 10.0, 42.0, {"kind": "bed", "lufs": -26, "xfade": 4.0}),
]


def k_weight(rate):
    """BS.1770 pre-filter (high shelf) and RLB high-pass as two biquads, for any sample rate."""
    # Coefficients as derived by pyloudnorm from the 48 kHz reference filters.
    g, q, fc = 3.99984385397, 0.7071752369554193, 1681.9744509555319
    a_db = 10 ** (g / 40.0)
    w0 = 2 * math.pi * fc / rate
    alpha = math.sin(w0) / (2 * q)
    cs = math.cos(w0)
    b0 = a_db * ((a_db + 1) + (a_db - 1) * cs + 2 * math.sqrt(a_db) * alpha)
    b1 = -2 * a_db * ((a_db - 1) + (a_db + 1) * cs)
    b2 = a_db * ((a_db + 1) + (a_db - 1) * cs - 2 * math.sqrt(a_db) * alpha)
    a0 = (a_db + 1) - (a_db - 1) * cs + 2 * math.sqrt(a_db) * alpha
    a1 = 2 * ((a_db - 1) - (a_db + 1) * cs)
    a2 = (a_db + 1) - (a_db - 1) * cs - 2 * math.sqrt(a_db) * alpha
    shelf = (np.array([b0, b1, b2]) / a0, np.array([1.0, a1 / a0, a2 / a0]))
    q, fc = 0.5003270373253953, 38.13547087613982
    w0 = 2 * math.pi * fc / rate
    alpha = math.sin(w0) / (2 * q)
    cs = math.cos(w0)
    a0 = 1 + alpha
    high = (np.array([(1 + cs) / 2, -(1 + cs), (1 + cs) / 2]) / a0, np.array([1.0, -2 * cs / a0, (1 - alpha) / a0]))
    return shelf, high


def loudness(samples, rate):
    """Integrated loudness in LUFS (mono counts as one channel); short clips use their whole length."""
    if samples.ndim == 1:
        samples = samples[:, None]
    (sb, sa), (hb, ha) = k_weight(rate)
    weighted = signal.lfilter(hb, ha, signal.lfilter(sb, sa, samples, axis=0), axis=0)
    block = int(0.4 * rate)
    step = int(0.1 * rate)
    if len(weighted) < block:
        power = (weighted ** 2).mean(axis=0).sum()
        return -0.691 + 10 * math.log10(max(power, 1e-12))
    powers = []
    for start in range(0, len(weighted) - block + 1, step):
        powers.append((weighted[start:start + block] ** 2).mean(axis=0).sum())
    powers = np.array(powers)
    levels = -0.691 + 10 * np.log10(np.maximum(powers, 1e-12))
    kept = powers[levels > -70.0]
    if len(kept) == 0:
        return -70.0
    relative = -0.691 + 10 * math.log10(kept.mean()) - 10.0
    kept = powers[(levels > -70.0) & (levels > relative)]
    return -0.691 + 10 * math.log10(kept.mean())


def limit(samples, ceiling):
    """A plain look-ahead peak limiter: the gain dips smoothly (over LIMIT_WINDOW_S) wherever a peak would pass the ceiling."""
    n = max(3, int(LIMIT_WINDOW_S * RATE)) | 1
    wanted = np.minimum(1.0, ceiling / np.maximum(np.abs(samples).max(axis=1), 1e-9))
    held = ndimage.minimum_filter1d(wanted, size=2 * n + 1, mode="nearest")
    smooth = ndimage.uniform_filter1d(held, size=n, mode="nearest")
    return samples * np.minimum(smooth, wanted)[:, None]


def write(path, samples, quality):
    """Writes Ogg Vorbis in blocks (libsndfile's Vorbis encoder overflows its stack on one long write)."""
    with soundfile.SoundFile(path, "w", RATE, samples.shape[1], format="OGG", subtype="VORBIS", compression_level=quality) as f:
        for start in range(0, len(samples), 8192):
            f.write(samples[start:start + 8192].astype(np.float32))


def find(bundle, pattern):
    hits = sorted(glob.glob(os.path.join(bundle, "**", pattern), recursive=True))
    hits = [h for h in hits if h.lower().endswith(".wav")]
    if len(hits) != 1:
        raise SystemExit(f"'{pattern}': expected one WAV, found {len(hits)}")
    return hits[0]


def fade(samples, seconds, rate, at_end):
    n = min(len(samples), max(1, int(seconds * rate)))
    ramp = np.sin(np.linspace(0.0, math.pi / 2, n)) ** 2
    if at_end:
        samples[-n:] *= ramp[::-1, None]
    else:
        samples[:n] *= ramp[:, None]


def cut(bundle, entry):
    out, use, pattern, start, end, options = entry
    kind = options.get("kind", "shot")
    path = find(bundle, pattern)
    data, rate = riffwav.read(path)
    xfade = options.get("xfade", 0.0) if kind != "shot" else 0.0
    a = int(start * rate)
    b = min(len(data), int((end + xfade) * rate))
    clip = data[a:b, :2].astype(np.float64)
    if data.shape[1] > 2:
        clip = data[a:b, :2].astype(np.float64)  # first pair of a multichannel file
    if kind in ("shot", "loop"):
        clip = clip.mean(axis=1, keepdims=True)
    elif clip.shape[1] == 1:
        clip = np.repeat(clip, 2, axis=1)
    clip -= clip.mean(axis=0)
    # Resample to the game's rate; a speed below 1 plays the cut slower and lower.
    speed = options.get("speed", 1.0)
    source_rate = rate * speed
    up, down = RATE, int(round(source_rate))
    common = math.gcd(up, down)
    clip = signal.resample_poly(clip, up // common, down // common, axis=0)
    if "lowpass" in options:
        sos = signal.butter(4, options["lowpass"], "low", fs=RATE, output="sos")
        clip = signal.sosfiltfilt(sos, clip, axis=0)
    if options.get("trim"):
        env = np.abs(clip).max(axis=1)
        above = np.nonzero(env > env.max() * 0.03)[0]
        if len(above):
            clip = clip[max(0, above[0] - int(0.01 * RATE)):]
    if kind == "shot":
        fade(clip, 0.003, RATE, False)
        fade(clip, options.get("fade", 0.03), RATE, True)
    else:
        n = int(xfade / speed * RATE)
        body, tail = clip[:-n].copy(), clip[-n:]
        ramp = np.linspace(0.0, 1.0, n)[:, None]
        body[:n] = body[:n] * np.sqrt(ramp) + tail * np.sqrt(1.0 - ramp)
        clip = body
    level = loudness(clip, RATE)
    gain = options.get("lufs", -18.0) - level
    peak = 20 * math.log10(max(np.abs(clip).max(), 1e-9))
    allowance = SHOT_LIMIT_DB
    if kind != "shot":
        allowance = SHOT_LIMIT_DB / 2
    allowance = options.get("limit", allowance)
    gain = min(gain, PEAK_CEILING_DB - peak + allowance)
    clip *= 10 ** (gain / 20.0)
    limited = gain > PEAK_CEILING_DB - peak
    if limited:
        clip = limit(clip, 10 ** (PEAK_CEILING_DB / 20.0))
    target = os.path.join(OUT_DIR, out)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    quality = SHOT_QUALITY
    if kind == "loop":
        quality = LOOP_QUALITY
    elif kind == "bed":
        quality = BED_QUALITY
    write(target, clip, quality)
    final = loudness(clip, RATE)
    final_peak = 20 * math.log10(max(np.abs(clip).max(), 1e-9))
    processing = [kind, f"{'mono' if clip.shape[1] == 1 else 'stereo'}"]
    if speed != 1.0:
        processing.append(f"speed x{speed}")
    if "lowpass" in options:
        processing.append(f"lowpass {options['lowpass']} Hz")
    if xfade:
        processing.append(f"crossfade {xfade} s")
    processing.append(f"gain {gain:+.1f} dB")
    if limited:
        processing.append(f"peaks limited {gain - (PEAK_CEILING_DB - peak):.1f} dB")
    rel = os.path.relpath(path, bundle).replace("\\", "/")
    library_folder, source_file = rel.split("/", 1)
    print(f"{out:36} {len(clip) / RATE:6.2f}s {final:6.1f} LUFS peak {final_peak:5.1f} dB  {os.path.getsize(target) / 1024:6.1f} KB")
    return {
        "file": f"res://{OUT_DIR}/{out}", "use": use, "library_folder": library_folder, "source_file": source_file,
        "start_s": f"{start:.2f}", "end_s": f"{end:.2f}", "processing": "; ".join(processing),
        "duration_s": f"{len(clip) / RATE:.2f}", "lufs": f"{final:.1f}", "peak_db": f"{final_peak:.1f}",
        "bytes": os.path.getsize(target),
    }


def measure(paths):
    for path in paths:
        if path.lower().endswith(".wav"):
            data, rate = riffwav.read(path)
        else:
            data, rate = soundfile.read(path, always_2d=True)
        peak = 20 * math.log10(max(np.abs(data).max(), 1e-9))
        print(f"{loudness(data.astype(np.float64), rate):6.1f} LUFS  peak {peak:5.1f}  {len(data) / rate:6.2f}s  {path}")


def main():
    if len(sys.argv) >= 3 and sys.argv[1] == "--measure":
        measure(sys.argv[2:])
        return
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    bundle = sys.argv[1]
    rows = [cut(bundle, entry) for entry in CUTS]
    os.makedirs(os.path.dirname(SOURCES_CSV), exist_ok=True)
    with open(SOURCES_CSV, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    total = sum(r["bytes"] for r in rows)
    print(f"{len(rows)} clips, {total / 1048576:.2f} MB -> {OUT_DIR}/, sources in {SOURCES_CSV}")


if __name__ == "__main__":
    main()
