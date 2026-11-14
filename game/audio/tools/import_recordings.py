"""Brings the team's own recordings (the DownhillRoguelite audio pack) into the project.

Usage: python audio/tools/import_recordings.py <unpacked pack folder>   (run from game/)

The pack holds Audio/SFX/Creatures/<creature>/*.wav, Audio/Voice/NPC/<npc>/<language>/[<kind>/<category>/]*.wav
and audio_manifest.csv (clip, source recording, cut times and a suggested use or a review note; both the
first pack's columns, start_seconds/end_seconds/suggested_usage, and the later packs' start_s/end_s/review
are read). This copies:
  - creature clips, untouched, to audio/creatures/ (the project's flat <creature>_<kind>_<n>.wav naming);
  - voice lines to audio/voice/npc_<npc>/<language>/, each brought to the same speaking loudness (speech_db,
    measured on the spoken frames only, the pauses left out) by gain alone
    (no noise reduction, no pitch or time change), so one line doesn't whisper and the next shout;
    breaths and sighs (a Nonverbal/ folder) to a quieter level, so a sigh isn't as loud as a sentence;
  - the manifest into audio/original_recordings.csv with the project's paths, the category (the folder
    a clip came in, e.g. kratke_reakcije_i_barks), each clip's level and, for voice lines, the words
    (from the file names, or from words.csv next to this script: path,words, written after listening).
    Rows of other packs already in the CSV are kept; importing a pack again replaces its own rows.
The raw Source/ and Originals/ recordings stay out of the repository.
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
NONVERBAL_TARGET_RMS_DB = -27.0  # breaths, sighs and other sounds without words sit this much lower.
CSV_PATH = "audio/original_recordings.csv"
WORDS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "words.csv")
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


def speech_level(samples, rate):
    """
    How loud the speaking is, without the pauses: the RMS of the 50 ms frames above -50 dBFS that are
    also within 10 dB of their own average (gated like broadcast loudness, BS.1770). A clip with two
    sentences and a long breath between them would otherwise be measured too quiet and pushed too loud.
    """
    size = max(1, rate // 20)
    energies = []
    for start in range(0, len(samples) - size + 1, size):
        frame = samples[start:start + size]
        energies.append(sum(s * s for s in frame) / len(frame) / (32768.0 * 32768.0))
    loud = [e for e in energies if db(math.sqrt(e)) > -50.0]
    if not loud:
        return levels(samples)[1]
    gate = db(math.sqrt(sum(loud) / len(loud))) - 10.0
    voiced = [e for e in loud if db(math.sqrt(e)) > gate]
    return db(math.sqrt(sum(voiced) / len(voiced)))


def column(row, *names):
    """The first of these columns the manifest fills (packs name them differently), or empty."""
    for name in names:
        if row.get(name):
            return row[name].strip()
    return ""


def known_words():
    """Words written down after listening (words.csv: path,words), keyed by the project path."""
    if not os.path.exists(WORDS_FILE):
        return {}
    with open(WORDS_FILE, encoding="utf-8-sig", newline="") as table:
        return {row["path"].strip(): row["words"].strip() for row in csv.DictReader(table) if row.get("words")}


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    pack = sys.argv[1]
    written = known_words()
    rows = []
    with open(os.path.join(pack, "audio_manifest.csv"), encoding="utf-8-sig", newline="") as manifest:
        for row in csv.DictReader(manifest):
            source = os.path.join(pack, *row["path"].split("/"))
            parts = row["path"].split("/")
            name = parts[-1]
            rate, samples = read(source)
            words = ""
            gain_db = 0.0
            category = ""
            usage = column(row, "suggested_usage")
            if parts[1] == "SFX":
                target = f"audio/creatures/{name}"
            else:
                npc = parts[3].lower()
                target = f"audio/voice/npc_{npc}/{parts[4]}/{name}"
                if len(parts) > 6:
                    category = parts[-2]
                if not usage:
                    usage = os.path.splitext(row["source"])[0]
                key = re.sub(r"^[A-Z]+_\d+_", "", os.path.splitext(name)[0])
                words = written.get(target, WORDS.get(key, ""))
                target_rms = VOICE_TARGET_RMS_DB
                if "Nonverbal" in parts:
                    target_rms = NONVERBAL_TARGET_RMS_DB
                peak_db, _, _ = levels(samples)
                gain_db = min(target_rms - speech_level(samples, rate), VOICE_PEAK_CEILING_DB - peak_db)
                gain = 10.0 ** (gain_db / 20.0)
                samples = [max(-32768, min(32767, int(round(s * gain)))) for s in samples]
            write(target, rate, samples)
            peak_db, rms_db, clipped = levels(samples)
            speech_db = ""
            if parts[1] != "SFX":
                speech_db = f"{speech_level(samples, rate):.1f}"
            rows.append({
                "path": target, "source": row["source"],
                "start_seconds": column(row, "start_seconds", "start_s"), "end_seconds": column(row, "end_seconds", "end_s"),
                "suggested_usage": usage, "category": category, "words": words, "seconds": f"{len(samples) / rate:.2f}",
                "gain_db": f"{gain_db:+.1f}", "peak_db": f"{peak_db:.1f}", "rms_db": f"{rms_db:.1f}", "speech_db": speech_db, "clipped_samples": clipped,
            })
    imported = {r["path"] for r in rows}
    kept = []
    if os.path.exists(CSV_PATH):
        with open(CSV_PATH, encoding="utf-8", newline="") as old:
            kept = [r for r in csv.DictReader(old) if r["path"] not in imported]
    with open(CSV_PATH, "w", encoding="utf-8", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=list(rows[0].keys()), restval="", extrasaction="ignore", lineterminator="\n")
        writer.writeheader()
        writer.writerows(kept + rows)
    hot = sum(1 for r in rows if int(r["clipped_samples"]) > 0)
    print(f"{len(rows)} clips imported, {len(kept)} from other packs kept; {hot} of the new ones clip (recorded too hot).")


if __name__ == "__main__":
    main()
