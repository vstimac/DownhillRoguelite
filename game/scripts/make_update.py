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
   only resources that changed since the full release (scenes, art, sounds, settings). Then it leaves
   out imported models, textures and sounds whose source and .import haven't changed in git since the
   base's tag (base.txt): a fresh checkout re-imports them byte-differently, which made Godot ship
   every model again (49 MB instead of a few). --keep-reimports skips this.
3. Adds every other file of the new build that differs from the base (the game's code is a DLL in
   the data folder, so code changes ship as that one file).
4. Zips it as DownhillRoguelite-update-<version>-for-<base>.zip with version.txt; players extract
   it over the game folder, or run "Update game.bat" which does that from the GitHub release.

With --platform linux the same is done with the "Linux" preset against a Linux full release folder,
and the update is DownhillRoguelite-update-<version>-linux-for-<base>.tar.gz (the game installs it
itself: core/update/UpdateInstaller.cs).

Updates are cumulative since the base: each one replaces the last, so a player who skipped one
needs only the newest. When updates grow large, make a new full release (and a new base).

A small update can't change the game's startup settings: autoloads, input actions, shader globals and
physics settings come from the base's project.binary, which a patch never replaces. So it refuses when
project.godot differs from the base's tag (base.txt) in any line but the few that PatchLoader makes
up for itself (ALLOWED_SETTINGS). Such a change needs a full release; --allow-settings overrides.

Versions: a major version (3.0, 4.0, ...) is a full release and the new base; the minor versions
after it (3.1, 3.2, ...) are small updates on that base.
"""
import hashlib
import os
import shutil
import re
import struct
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


IMPORTED = ".godot/imported/"
IMPORTED_REF = re.compile(r'res://(\.godot/imported/[^"\s,\]]+)')


def read_pck(path):
    """A Godot 4 .pck's header and directory: (header fields, [(name bytes, offset, size, md5, flags)])."""
    with open(path, "rb") as f:
        head = f.read(104)
        if head[:4] != b"GDPC":
            fail(f"{path} is not a Godot pack")
        version, major, minor, patch, flags, file_base, dir_offset = struct.unpack_from("<IIIIIQQ", head, 4)
        if version < 3 or flags & 1:
            fail(f"{path}: pack format {version} (flags {flags}) is not one this script can trim")
        f.seek(dir_offset)
        count, = struct.unpack("<I", f.read(4))
        entries = []
        for _ in range(count):
            length, = struct.unpack("<I", f.read(4))
            name = f.read(length)
            offset, size = struct.unpack("<QQ", f.read(16))
            md5 = f.read(16)
            entry_flags, = struct.unpack("<I", f.read(4))
            entries.append((name, offset, size, md5, entry_flags))
    return (version, major, minor, patch, flags, file_base), entries


def entry_path(name):
    return name.rstrip(b"\0").decode("utf-8", errors="replace").removeprefix("res://")


def write_pck(source, target, header, entries):
    """Writes the entries (offsets relative to the file base, as Godot 4 writes them) into a new pack."""
    version, major, minor, patch, flags, file_base = header
    with open(source, "rb") as src, open(target, "wb") as out:
        out.write(b"\0" * file_base)
        placed = []
        for name, offset, size, md5, entry_flags in entries:
            at = out.tell() - file_base
            src.seek(file_base + offset)
            remaining = size
            while remaining > 0:
                chunk = src.read(min(remaining, 1 << 20))
                out.write(chunk)
                remaining -= len(chunk)
            out.write(b"\0" * ((16 - out.tell() % 16) % 16))
            placed.append((name, at, size, md5, entry_flags))
        dir_offset = out.tell()
        out.write(struct.pack("<I", len(placed)))
        for name, at, size, md5, entry_flags in placed:
            out.write(struct.pack("<I", len(name)) + name + struct.pack("<QQ", at, size) + md5 + struct.pack("<I", entry_flags))
        out.seek(0)
        out.write(b"GDPC" + struct.pack("<IIIIIQQ", version, major, minor, patch, flags, file_base, dir_offset))


# project.godot lines a small update may add or change, because core/PatchLoader.cs makes up for them
# at startup on an older base.
ALLOWED_SETTINGS = {
    'NetSession="*res://network/NetSession.cs"',
}


def settings_guard(base_id):
    """Fails when project.godot changed since the base in a way a small update can't carry."""
    old = subprocess.run(["git", "-C", ROOT, "show", f"{base_id}:./project.godot"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if old.returncode != 0:
        known = subprocess.run(["git", "-C", ROOT, "rev-parse", "--verify", "--quiet", f"{base_id}^{{commit}}"], capture_output=True, text=True)
        if known.returncode == 0:
            fail(f"can't read project.godot as it was in {base_id}, so a small update can't be checked; make a full release, or pass --allow-settings")
        print(f"      (not checking settings: git doesn't know {base_id})")
        return
    with open(os.path.join(ROOT, "project.godot"), encoding="utf-8") as f:
        new_lines = [line.rstrip() for line in f]
    old_lines = [line.rstrip() for line in old.stdout.splitlines()]
    removed = [line for line in old_lines if line and line not in new_lines]
    added = [line for line in new_lines if line and line not in old_lines and line not in ALLOWED_SETTINGS]
    if not removed and not added:
        return
    for line in removed[:12]:
        print(f"      - {line}")
    for line in added[:12]:
        print(f"      + {line}")
    fail(f"project.godot changed since {base_id} in startup settings a small update can't carry; "
         "make a full release (a new base), or pass --allow-settings if you are sure")


def git_lines(*args):
    result = subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        return None
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def removal_guard(patch_path, base_id):
    """
    Fails when the patch would delete a resource from players' games whose source is only missing
    from this checkout (licensed art kept out of git, like the Mixamo carrier) rather than deleted
    in git since the base: such an update would take the character away from everyone.
    """
    header, entries = read_pck(patch_path)
    deleted = set(git_lines("diff", "--name-only", "--diff-filter=D", "--relative", base_id) or [])
    missing = []
    for entry in entries:
        path = entry_path(entry[0])
        if not entry[4] & 2 or not path.endswith(".import"):
            continue
        source = path[:-len(".import")]
        if not os.path.exists(os.path.join(ROOT, source)) and source not in deleted:
            missing.append(source)
    if missing:
        for source in missing[:12]:
            print(f"      would delete {source}")
        fail("the update would delete resources that are only missing from this checkout (git didn't delete them); "
             "copy them in (e.g. art/characters/mixamo from a full checkout) and build again")


def trim_patch(patch_path, base_pck, base_id):
    """
    Drops imported resources (models, textures, sounds) whose source and import settings haven't changed
    since the base release. Godot's --export-patch compares the imported bytes, and a fresh checkout
    re-imports everything, so without this the patch carries every model again (tens of MB) although
    the base the player already has holds the same thing under the same path.
    """
    changed = git_lines("diff", "--name-only", "--relative", base_id)
    untracked = git_lines("ls-files", "--others", "--exclude-standard")
    if changed is None or untracked is None:
        print(f"      (not trimming: git doesn't know {base_id})")
        return
    changed = set(changed) | set(untracked)
    source_of = {}
    for current, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in (".godot", "bin", "obj", ".git")]
        for name in files:
            if not name.endswith(".import"):
                continue
            path = os.path.join(current, name)
            source = os.path.relpath(path, ROOT).replace("\\", "/")[:-len(".import")]
            with open(path, encoding="utf-8", errors="replace") as f:
                for imported in IMPORTED_REF.findall(f.read()):
                    source_of[imported] = source
    _, base_entries = read_pck(base_pck)
    in_base = {entry_path(name) for name, _, _, _, _ in base_entries}
    header, entries = read_pck(patch_path)
    kept, dropped, dropped_bytes = [], 0, 0
    for entry in entries:
        path = entry_path(entry[0])
        source = source_of.get(path)
        unchanged = (path.startswith(IMPORTED) and entry[4] == 0 and path in in_base and source is not None
                     and source not in changed and source + ".import" not in changed)
        if unchanged:
            dropped += 1
            dropped_bytes += entry[2]
        else:
            kept.append(entry)
    if dropped == 0:
        return
    trimmed = patch_path + ".trimmed"
    write_pck(patch_path, trimmed, header, kept)
    os.replace(trimmed, patch_path)
    print(f"      left out {dropped} re-imported but unchanged resources ({dropped_bytes / 1048576:.1f} MB)")


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
    keep_reimports = "--keep-reimports" in argv
    if keep_reimports:
        argv.remove("--keep-reimports")
    allow_settings = "--allow-settings" in argv
    if allow_settings:
        argv.remove("--allow-settings")
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

    if not allow_settings:
        settings_guard(base_id)

    scratch = tempfile.mkdtemp(prefix="dr-update-")
    try:
        build = os.path.join(scratch, "build")
        os.makedirs(os.path.join(build, "patches"))
        print("...   exporting the full build (for the code and libraries)")
        godot("--export-release", preset, os.path.join(build, exe))
        print("...   exporting the patch (changed resources only)")
        godot("--export-patch", preset, os.path.join(build, PATCH), "--patches", base_pck)
        if not keep_reimports:
            trim_patch(os.path.join(build, PATCH), base_pck, base_id)
        removal_guard(os.path.join(build, PATCH), base_id)

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
