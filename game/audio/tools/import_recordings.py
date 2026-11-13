"""Brings the team's own recordings (the DownhillRoguelite audio pack) into the project.

Usage: python audio/tools/import_recordings.py <unpacked pack folder>   (run from game/)

The pack holds Audio/SFX/Creatures/<creature>/*.wav, Audio/Voice/NPC/<npc>/<language>/*.wav and
audio_manifest.csv (clip, source recording, cut times, suggested use). This copies:
  - creature clips, untouched, to audio/creatures/ (the project's flat <creature>_<kind>_<n>.wav naming);
  - voice lines to audio/voice/npc_<npc>/<language>/, each brought to the same loudness by gain alone
    (no noise reduction, no pitch or time change), so one line doesn't whisper and the next shout;
  - the manifest to audio/original_recordings.csv with the project's paths, each clip's level and,
    for voice lines, the words (taken from the file names: check them against the recordings).
The raw Source/ recordings stay out of the repository.
"""
import csv
import math
import os
import re
import struct
import sys
import wave

VOICE_TARGET_RMS_DB = -21.0   # every voice line is brought to this loudness...
VOICE_PEAK_CEILING_DB = -1.5  # ...unless that would push its peak above this.
CLIP_LEVEL = 32767

# The words of each line, from its file name, with their Croatian letters.
WORDS = {
    "Opa": "Opa.",
    "NoviPutnik": "Novi putnik.",
    "VecSamMislioDaSuUkinuliOvuLiniju": "Već sam mislio da su ukinuli ovu liniju.",
    "KamoSteKrenuliMladiGospodine": "Kamo ste krenuli, mladi gospodine?",
    "GoreSeNeMoze": "Gore se ne može.",
    "DoljeSeNePreporucuje": "Dolje se ne preporučuje.",
    "OvdjeNemaSignala": "Ovdje nema signala.",
    "JaCekamVlak": "Ja čekam vlak.",
    "TrebaoJeDociUtorak": "Trebao je doći u utorak.",
    "KojiUtorak": "Koji utorak?",
    "ToJosUtvrdjujemo": "To još utvrđujemo.",
    "DobrodosliNaStajalisteBezPovratka": "Dobrodošli na stajalište bez povratka.",
    "ImeJePrivremeno": "Ime je privremeno.",
    "VecSedamdesetGodina": "Već sedamdeset godina.",
    "KartuNaPregledMolim": "Kartu na pregled, molim.",
    "AkoJeNemateMozemoSePraviti": "Ako je nemate, možemo se praviti.",
    "NeStojiteNaRubuPerona": "Ne stojite na rubu perona.",
    "PeronJeOtisaoPrijeTridesetGodina": "Peron je otišao prije trideset godina.",
    "Cudno": "Čudno.",
    "ViJosImateSjenu": "Vi još imate sjenu.",
    "Aha": "Aha.",
    "PrviPutSeSpustate": "Prvi put se spuštate?",
    "ToSeVidiPoKoljenima": "To se vidi po koljenima.",
}


def read(path):
    with wave.open(path, "rb") as clip:
        if clip.getsampwidth() != 2 or clip.getnchannels() != 1:
            raise SystemExit(f"{path}: expected 16-bit mono")
        rate = clip.getframerate()
        raw = clip.readframes(clip.getnframes())
    return rate, list(struct.unpack(f"<{len(raw) // 2}h", raw))


def write(path, rate, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as clip:
        clip.setnchannels(1)
        clip.setsampwidth(2)
        clip.setframerate(rate)
        clip.writeframes(struct.pack(f"<{len(samples)}h", *samples))


def db(value):
    return 20.0 * math.log10(max(value, 1e-6))


def levels(samples):
    peak = max(abs(s) for s in samples) / 32768.0
    rms = math.sqrt(sum(s * s for s in samples) / len(samples)) / 32768.0
    clipped = sum(1 for s in samples if abs(s) >= CLIP_LEVEL)
    return db(peak), db(rms), clipped


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    pack = sys.argv[1]
    rows = []
    with open(os.path.join(pack, "audio_manifest.csv"), encoding="utf-8-sig", newline="") as manifest:
        for row in csv.DictReader(manifest):
            source = os.path.join(pack, *row["path"].split("/"))
            parts = row["path"].split("/")
            name = parts[-1]
            rate, samples = read(source)
            words = ""
            gain_db = 0.0
            if parts[1] == "SFX":
                target = f"audio/creatures/{name}"
            else:
                npc = parts[3].lower()
                target = f"audio/voice/npc_{npc}/{parts[4]}/{name}"
                key = re.sub(r"^[A-Z]+_\d+_", "", os.path.splitext(name)[0])
                words = WORDS.get(key, "")
                peak_db, rms_db, _ = levels(samples)
                gain_db = min(VOICE_TARGET_RMS_DB - rms_db, VOICE_PEAK_CEILING_DB - peak_db)
                gain = 10.0 ** (gain_db / 20.0)
                samples = [max(-32768, min(32767, int(round(s * gain)))) for s in samples]
            write(target, rate, samples)
            peak_db, rms_db, clipped = levels(samples)
            rows.append({
                "path": target, "source": row["source"], "start_seconds": row["start_seconds"], "end_seconds": row["end_seconds"],
                "suggested_usage": row["suggested_usage"], "words": words, "seconds": f"{len(samples) / rate:.2f}",
                "gain_db": f"{gain_db:+.1f}", "peak_db": f"{peak_db:.1f}", "rms_db": f"{rms_db:.1f}", "clipped_samples": clipped,
            })
    with open("audio/original_recordings.csv", "w", encoding="utf-8", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=list(rows[0].keys()), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    print(f"{len(rows)} clips imported; {sum(1 for r in rows if r['clipped_samples'] > 0)} of them clip (recorded too hot).")


if __name__ == "__main__":
    main()
