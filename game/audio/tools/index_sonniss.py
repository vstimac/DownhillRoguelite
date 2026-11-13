"""Indexes the Sonniss #GameAudioGDC bundle into a CSV (no audio is copied).

Usage (from game/):
    python -I audio/tools/index_sonniss.py "<bundle folder>" ../docs/audio/sonniss_gdc2026_index.csv

One row per WAV: library folder, file, supplier and library name (from the bundle's filelist), the
UCS category of the file name (AERO, VOX, WIND, ...) or a guess from the words when the file has no
UCS prefix, duration, format, size, and a `status`:
  ok         - the WAV header and the file size agree and no sampled block is all zeros;
  truncated  - the file is shorter than its header says (still downloading?);
  holes      - some sampled blocks are all zero bytes (torrent pieces not downloaded yet?);
  unreadable - not a WAV we can parse.
Files the bundle's filelist names but that aren't on disk are listed as `missing`.
"""
import csv
import os
import re
import struct
import sys
import zipfile

SAMPLE_BLOCKS = 24        # blocks checked for torrent holes per file
SAMPLE_BLOCK_BYTES = 65536

# A guess for files without a UCS prefix: first matching word wins.
GUESSES = [
    ("UI", r"\b(ui|interface|button|click|menu|notification|infographic|hud)\b"),
    ("MAGIC", r"\b(magic|spell|enchant|astral)\b"),
    ("ICE", r"\bice\b"),
    ("FIRE", r"\b(fire|campfire|bonfire|flame|ember)\b"),
    ("FIREWORKS", r"\bfirework"),
    ("BELLS", r"\bbell"),
    ("WEAPONS", r"\b(sword|blade|melee|axe|mace|hammer|weapon|knife)\b"),
    ("IMPACTS", r"\b(impact|hit|punch|slam|boom)\b"),
    ("WHOOSH", r"\b(whoosh|swish|swoosh|pass)\b"),
    ("CREATURES", r"\b(creature|monster|zombie|ghoul|undead|growl|roar|beast)\b"),
    ("VOICES", r"\b(voice|vocal|male|female|announcer|character|elf|grunt|yell)\b"),
    ("AMBIENCE", r"\b(ambience|ambient|ambiance|room tone|walla|crowd|loop|drone)\b"),
    ("WIND", r"\bwind"),
    ("RAIN", r"\b(rain|storm|thunder)\b"),
    ("WATER", r"\b(water|lake|wave|sea|river|stream)\b"),
    ("FOLEY", r"\b(cloth|paper|foley|bag|lock|door|key|rope|carabiner)\b"),
]


def read_filelist(folder):
    """Maps file name -> (library, supplier, url) from the bundle's xlsx filelist, if there is one."""
    found = {}
    for name in os.listdir(folder):
        if not name.lower().endswith(".xlsx"):
            continue
        with zipfile.ZipFile(os.path.join(folder, name)) as book:
            sheet = book.read("xl/worksheets/sheet1.xml").decode("utf8")
        for row in re.findall(r"<row[^>]*>(.*?)</row>", sheet, re.S):
            cells = {}
            for column, value in re.findall(r'<c r="([A-Z]+)\d+"[^>]*>(.*?)</c>', row, re.S):
                text = re.sub(r"<[^>]+>", "", value)
                text = text.replace("&apos;", "'").replace("&amp;", "&").replace("&quot;", '"')
                cells[column] = text.replace("&lt;", "<").replace("&gt;", ">")
            if cells.get("A", "").lower().endswith(".wav"):
                found[cells["A"]] = (cells.get("B", ""), cells.get("C", ""), cells.get("D", ""))
    return found


def wav_info(path):
    """Returns (format, channels, rate, bits, data_offset, data_bytes) from a RIFF/RF64 header."""
    with open(path, "rb") as f:
        head = f.read(12)
        if len(head) < 12 or head[8:12] != b"WAVE" or head[0:4] not in (b"RIFF", b"RF64"):
            raise ValueError("not a WAV")
        fmt = None
        ds64_data = None
        while True:
            chunk = f.read(8)
            if len(chunk) < 8:
                break
            cid, size = chunk[0:4], struct.unpack("<I", chunk[4:8])[0]
            start = f.tell()
            if cid == b"ds64":
                body = f.read(min(size, 28))
                ds64_data = struct.unpack("<Q", body[8:16])[0]
            elif cid == b"fmt ":
                body = f.read(size)
                tag, channels, rate, _, align, bits = struct.unpack("<HHIIHH", body[:16])
                if tag == 0xFFFE and len(body) >= 26:
                    tag = struct.unpack("<H", body[24:26])[0]
                fmt = (tag, channels, rate, bits, align)
            elif cid == b"data":
                if size == 0xFFFFFFFF and ds64_data is not None:
                    size = ds64_data
                if fmt is None:
                    raise ValueError("data before fmt")
                return fmt, start, size
            f.seek(start + size + (size & 1))
    raise ValueError("no data chunk")


def holes(path, offset, size):
    """Counts sampled blocks of the data that are all zero bytes."""
    empty = 0
    if size <= SAMPLE_BLOCK_BYTES:
        return 0
    with open(path, "rb") as f:
        for i in range(SAMPLE_BLOCKS):
            f.seek(offset + (size - SAMPLE_BLOCK_BYTES) * i // (SAMPLE_BLOCKS - 1))
            block = f.read(SAMPLE_BLOCK_BYTES)
            if block and block.count(0) == len(block):
                empty += 1
    return empty


def category(name):
    match = re.match(r"([A-Z]{2,})([A-Z][a-z]+)?_", name)
    if match:
        return match.group(1), match.group(2) or ""
    lower = name.lower()
    for label, pattern in GUESSES:
        if re.search(pattern, lower):
            return label, "guess"
    return "MISC", "guess"


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    folder, out = sys.argv[1], sys.argv[2]
    filelist = read_filelist(folder)
    rows = []
    seen = set()
    for root, _, files in os.walk(folder):
        for name in sorted(files):
            if not name.lower().endswith(".wav"):
                continue
            path = os.path.join(root, name)
            seen.add(name)
            library_folder = os.path.relpath(root, folder).replace("\\", "/")
            library, supplier, url = filelist.get(name, ("", "", ""))
            cat, sub = category(name)
            row = {
                "library_folder": library_folder, "file": name, "supplier": supplier, "library": library,
                "category": cat, "subcategory": sub, "duration_s": "", "channels": "", "rate_hz": "",
                "bits": "", "format": "", "size_mb": f"{os.path.getsize(path) / 1048576:.2f}", "status": "ok", "url": url,
            }
            try:
                (tag, channels, rate, bits, align), offset, size = wav_info(path)
                actual = os.path.getsize(path) - offset
                frames = min(size, actual) // max(align, 1)
                row.update({"duration_s": f"{frames / rate:.2f}", "channels": channels, "rate_hz": rate, "bits": bits})
                if tag == 3:
                    row["format"] = "float"
                else:
                    row["format"] = "pcm"
                if actual < size:
                    row["status"] = "truncated"
                elif holes(path, offset, size) > 0:
                    row["status"] = "holes"
            except (ValueError, struct.error, OSError) as error:
                row["status"] = f"unreadable: {error}"
            rows.append(row)
    for name, (library, supplier, url) in sorted(filelist.items()):
        if name not in seen:
            cat, sub = category(name)
            rows.append({"library_folder": "", "file": name, "supplier": supplier, "library": library, "category": cat,
                         "subcategory": sub, "duration_s": "", "channels": "", "rate_hz": "", "bits": "", "format": "",
                         "size_mb": "", "status": "missing", "url": url})
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    counts = {}
    for row in rows:
        key = row["status"].split(":")[0]
        counts[key] = counts.get(key, 0) + 1
    print(f"{len(rows)} rows, {len({r['library_folder'] for r in rows if r['library_folder']})} libraries -> {out}")
    print("status:", counts)


if __name__ == "__main__":
    main()
