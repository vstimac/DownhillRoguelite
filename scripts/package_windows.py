"""
Packs a Windows export into the zip players download, the same way every time.

    python scripts/package_windows.py [export_dir] [zip_path] [version]

Defaults: export_dir = ../Builds/DownhillRoguelite-playtest1 (the "Windows Desktop" preset's
export path), zip_path = ../Builds/DownhillRoguelite-playtest1-windows.zip.

What it does, and why:
- Checks the export is whole: the .exe, its DownhillRoguelite.pck and the data_ folder with the
  game's code beside it (not embedded, so later small updates can replace just what changed:
  scripts/make_update.py). Pass the release's tag as version to mark the folder as an update base.
- Adds the player README, the race launcher and the beta testers' Wallpaper folder from export/playtest/.
- Puts the Microsoft Visual C++ runtime DLLs next to the companion library
  (libnobodywho...dll needs them; a freshly installed Windows doesn't have them). Microsoft
  allows shipping these files next to the program ("app-local" deployment).
- Writes zip paths with forward slashes, as the zip format requires (Windows PowerShell's
  Compress-Archive writes backslashes, which other unzip tools turn into odd file names).
- Opens the finished zip and checks all of the above.
"""
import os
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOP = "DownhillRoguelite-playtest1"
EXE = "DownhillRoguelite.exe"
COMPANION = "libnobodywho-godot-x86_64-pc-windows-msvc-release.dll"
VC_RUNTIME = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll", "vcomp140.dll"]
EXTRA = ["README.txt", "Race - same mountain.bat", "Update game.bat", "update.ps1"]
PCK = "DownhillRoguelite.pck"
WALLPAPER = "Wallpaper"


def fail(message):
    print("FAIL  " + message)
    sys.exit(1)


def main():
    export_dir = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "..", "Builds", TOP))
    zip_path = os.path.abspath(sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "..", "Builds", TOP + "-windows.zip"))

    exe = os.path.join(export_dir, EXE)
    if not os.path.isfile(exe):
        fail(f"no {EXE} in {export_dir}; export the 'Windows Desktop' preset first")
    # The game's data and code sit next to the exe (not embedded), so updates can replace just what changed.
    if not os.path.isfile(os.path.join(export_dir, PCK)):
        fail(f"no {PCK} next to the exe; export with binary_format/embed_pck=false")
    data = [d for d in os.listdir(export_dir) if d.startswith("data_") and os.path.isdir(os.path.join(export_dir, d))]
    if not data:
        fail("no data_ folder next to the exe; export with dotnet/embed_build_outputs=false")
    # A full release is the base later updates are made against (scripts/make_update.py). Pass the
    # release's tag (e.g. v0.2.5-beta): update.ps1 compares it with the newest release's tag.
    version = sys.argv[3] if len(sys.argv) > 3 else ""
    if version:
        for name in ("base.txt", "version.txt"):
            with open(os.path.join(export_dir, name), "w", encoding="utf-8") as f:
                f.write(version + "\n")

    for name in EXTRA:
        shutil.copy2(os.path.join(ROOT, "export", "playtest", name), os.path.join(export_dir, name))
    wallpapers = os.path.join(ROOT, "export", "playtest", WALLPAPER)
    shutil.copytree(wallpapers, os.path.join(export_dir, WALLPAPER), dirs_exist_ok=True)
    wallpaper_files = sorted(os.listdir(wallpapers))

    if os.path.isfile(os.path.join(export_dir, COMPANION)):
        system32 = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32")
        for name in VC_RUNTIME:
            source = os.path.join(system32, name)
            if not os.path.isfile(source):
                fail(f"{name} not found in {system32}; install the Visual C++ 2015-2022 Redistributable (x64) on this PC")
            shutil.copy2(source, os.path.join(export_dir, name))

    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for folder, _, files in os.walk(export_dir):
            for name in sorted(files):
                if name.endswith(".log"):
                    continue
                full = os.path.join(folder, name)
                relative = os.path.relpath(full, export_dir).replace("\\", "/")
                archive.write(full, TOP + "/" + relative)

    # Check the result the way a player's unzip tool would see it.
    with zipfile.ZipFile(zip_path) as archive:
        names = [info.orig_filename for info in archive.infolist()]
    if any("\\" in name for name in names):
        fail("zip has backslash paths")
    wanted = [EXE, PCK] + EXTRA + [WALLPAPER + "/" + name for name in wallpaper_files]
    if any(name.endswith(COMPANION) for name in names):
        wanted += VC_RUNTIME
    for name in wanted:
        if TOP + "/" + name not in names:
            fail(f"zip is missing {name}")
    size = os.path.getsize(zip_path) / 1048576
    print(f"PASS  {zip_path} ({size:.0f} MB, {len(names)} files)")


if __name__ == "__main__":
    main()
