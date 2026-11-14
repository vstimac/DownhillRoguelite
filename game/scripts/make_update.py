"""
Builds a small update for players who already have the full game, instead of a whole new download.

    python scripts/make_update.py <base_dir> <version> [out_dir] [--platform windows|linux]

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

With --platform linux the same is done with the "Linux" preset against a Linux full release folder,
and the update is DownhillRoguelite-update-<version>-linux-for-<base>.tar.gz (the game installs it
itself: core/update/UpdateInstaller.cs).

Updates are cumulative since the base: each one replaces the last, so a player who skipped one
needs only the newest. When updates grow large, make a new full release (and a new base).
"""
import hashlib
import os
import shutil
import subprocess
import io
import sys
import tarfile
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PRESETS = {"windows": "Windows Desktop", "linux": "Linux"}
EXES = {"windows": "DownhillRoguelite.exe", "linux": "DownhillRoguelite.x86_64"}
PCK = "DownhillRoguelite.pck"
PATCH = "patches/update.pck"
# Player-made or per-install files that an update must never overwrite.
SKIP = {"version.txt", "base.txt"}
SKIP_DIRS = {"patches", "Models", "logs"}
# Files from export/playtest/ that the full release ships next to the exe and updates refresh.
PLAYER_SCRIPTS = ["update.ps1", "Update game.bat"]


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


def write_tar(tar_path, entries, version, exe):
    """A .tar.gz with Unix modes set by hand (Windows has no execute bit): the game binary is 755."""
    with tarfile.open(tar_path, "w:gz", compresslevel=6) as archive:
        for relative, path in entries:
            info = archive.gettarinfo(path, relative)
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            info.mode = 0o755 if relative == exe or relative.endswith(".sh") else 0o644
            with open(path, "rb") as f:
                archive.addfile(info, f)
        data = (version + "\n").encode("utf-8")
        info = tarfile.TarInfo("version.txt")
        info.size = len(data)
        info.mode = 0o644
        archive.addfile(info, io.BytesIO(data))


def main():
    argv = list(sys.argv[1:])
    platform = "windows"
    if "--platform" in argv:
        at = argv.index("--platform")
        if at + 1 >= len(argv) or argv[at + 1] not in PRESETS:
            fail("--platform takes windows or linux")
        platform = argv[at + 1]
        del argv[at:at + 2]
    if len(argv) < 2:
        print(__doc__)
        sys.exit(2)
    preset = PRESETS[platform]
    exe = EXES[platform]
    base_dir = os.path.abspath(argv[0])
    version = argv[1]
    out_dir = os.path.abspath(argv[2] if len(argv) > 2 else os.path.join(ROOT, "..", "..", "Builds"))
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
        godot("--export-release", preset, os.path.join(build, exe))
        print("...   exporting the patch (changed resources only)")
        godot("--export-patch", preset, os.path.join(build, PATCH), "--patches", base_pck)

        base_files = files_under(base_dir)
        changed = []
        for relative, path in sorted(files_under(build).items()):
            if relative.startswith("patches/"):
                continue
            old = base_files.get(relative)
            if old is None or digest(old) != digest(path):
                changed.append((relative, path))
        # The folder's own updater ("Update game.bat" runs it) travels with every update too.
        for name in PLAYER_SCRIPTS if platform == "windows" else []:
            source = os.path.join(ROOT, "export", "playtest", name)
            old = base_files.get(name)
            if os.path.isfile(source) and (old is None or digest(old) != digest(source)):
                changed.append((name, source))

        os.makedirs(out_dir, exist_ok=True)
        if platform == "linux":
            zip_path = os.path.join(out_dir, f"DownhillRoguelite-update-{version}-linux-for-{base_id}.tar.gz")
        else:
            zip_path = os.path.join(out_dir, f"DownhillRoguelite-update-{version}-for-{base_id}.zip")
        if os.path.exists(zip_path):
            os.remove(zip_path)
        if platform == "linux":
            write_tar(zip_path, [(PATCH, os.path.join(build, PATCH))] + changed, version, exe)
        else:
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
