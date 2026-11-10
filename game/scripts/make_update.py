"""
Builds a small update for players who already have the full game, instead of a whole new download.

    python scripts/make_update.py <base_dir> <version> [out_dir]

base_dir  the full game folder exactly as released (its DownhillRoguelite.pck and data folder are
          the reference; keep it untouched after a full release)
version   the new version shown to players, e.g. 2.5.1
out_dir   where to write the update zip (default: Builds next to the repository)

What it does:
1. Exports the current project with the "Windows Desktop" preset to a scratch folder (full build).
2. Exports patches/update.pck with Godot's --export-patch against the base's DownhillRoguelite.pck:
   only resources that changed since the full release (scenes, art, sounds, settings).
3. Adds every other file of the new build that differs from the base (the game's code is a DLL in
   the data folder, so code changes ship as that one file).
4. Zips it as DownhillRoguelite-update-<version>-for-<base>.zip with version.txt; players extract
   it over the game folder, or run "Update game.bat" which does that from the GitHub release.

Updates are cumulative since the base: each one replaces the last, so a player who skipped one
needs only the newest. When updates grow large, make a new full release (and a new base).
"""
import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PRESET = "Windows Desktop"
EXE = "DownhillRoguelite.exe"
PCK = "DownhillRoguelite.pck"
PATCH = "patches/update.pck"
# Player-made or per-install files that an update must never overwrite.
SKIP = {"version.txt", "base.txt"}
SKIP_DIRS = {"patches", "Models", "logs"}


def fail(message):
    print("FAIL  " + message)
    sys.exit(1)


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def files_under(folder):
    found = {}
    for current, dirs, files in os.walk(folder):
        relative_dir = os.path.relpath(current, folder)
        top = relative_dir.replace("\\", "/").split("/")[0]
        if top in SKIP_DIRS:
            dirs[:] = []
            continue
        for name in files:
            relative = os.path.normpath(os.path.join(relative_dir, name)).replace("\\", "/")
            if relative in SKIP or relative == PCK or name.endswith(".log"):
                continue
            found[relative] = os.path.join(current, name)
    return found


def godot(*args):
    command = [os.environ.get("GODOT", "godot"), "--headless", "--path", ROOT] + list(args)
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        print(result.stdout[-3000:])
        print(result.stderr[-3000:])
        fail("godot " + " ".join(args) + f" exited with {result.returncode}")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    base_dir = os.path.abspath(sys.argv[1])
    version = sys.argv[2]
    out_dir = os.path.abspath(sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, "..", "..", "Builds"))
    base_pck = os.path.join(base_dir, PCK)
    if not os.path.isfile(base_pck):
        fail(f"no {PCK} in {base_dir}: the base must be a full release exported with embed_pck=false")
    base_id = "unknown"
    base_txt = os.path.join(base_dir, "base.txt")
    if os.path.isfile(base_txt):
        base_id = open(base_txt, encoding="utf-8").read().strip()

    scratch = tempfile.mkdtemp(prefix="dr-update-")
    try:
        build = os.path.join(scratch, "build")
        os.makedirs(os.path.join(build, "patches"))
        print("...   exporting the full build (for the code and libraries)")
        godot("--export-release", PRESET, os.path.join(build, EXE))
        print("...   exporting the patch (changed resources only)")
        godot("--export-patch", PRESET, os.path.join(build, PATCH), "--patches", base_pck)

        base_files = files_under(base_dir)
        changed = []
        for relative, path in sorted(files_under(build).items()):
            if relative.startswith("patches/"):
                continue
            old = base_files.get(relative)
            if old is None or digest(old) != digest(path):
                changed.append((relative, path))

        os.makedirs(out_dir, exist_ok=True)
        zip_path = os.path.join(out_dir, f"DownhillRoguelite-update-{version}-for-{base_id}.zip")
        if os.path.exists(zip_path):
            os.remove(zip_path)
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            archive.write(os.path.join(build, PATCH), PATCH)
            for relative, path in changed:
                archive.write(path, relative)
            archive.writestr("version.txt", version + "\n")

        patch_mb = os.path.getsize(os.path.join(build, PATCH)) / 1048576
        size_mb = os.path.getsize(zip_path) / 1048576
        print(f"      patch {patch_mb:.1f} MB; other changed files: {len(changed)}")
        for relative, _ in changed:
            print("        " + relative)
        print(f"PASS  {zip_path} ({size_mb:.1f} MB)")
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


if __name__ == "__main__":
    main()
