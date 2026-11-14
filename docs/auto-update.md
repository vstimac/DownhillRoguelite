# Automatic updates

Play-testers stay on the newest release without downloading anything by hand.

## What the player sees

1. The game starts. The title screen asks GitHub for the release list in the background (10 s
   timeout; offline, nothing happens and nothing waits).
2. If there is a newer release for this game folder, a card opens in the top right corner (the only
   place that tells): the release's name, the day it came out, the download size, and **Update now**
   (focused) or **Later**. Later (or Esc / B) folds it into a small **Update** tab in the same corner;
   the tab opens the card again. The footer shows the installed version with its release date
   ("Beta 3.0 · 14 Nov 2026"), because most players go by the date, not the number.
3. Picking it updates the game and starts it again. If anything fails, nothing in the game folder
   has changed and the old version stays.
   - **Windows:** the game closes and the updater window opens (`update.ps1`): it downloads, checks
     the file's SHA-256, unpacks to a temporary folder, installs, and starts the game.
   - **Linux:** the game does the same steps itself (`game/core/update/UpdateInstaller.cs`) and the
     card shows the progress; no terminal, PowerShell or extra programs are needed. A running
     program's files can be replaced on Linux, so no separate updater is used.

Options > Gameplay > **Check for new versions** turns the check off. "Update game.bat" in the game
folder does the same by hand.

## How it decides (`game/core/update/UpdatePlanner.cs`)

Newest published release first; drafts are skipped, pre-releases too (unless `-- --update-prerelease`):

- a release with `DownhillRoguelite-update-<version>-for-<base>.zip`, where `<base>` is the folder's
  `base.txt`, decides: up to date if `<version>` equals `version.txt`, else that small update;
- otherwise a release with a Windows build (`*-windows.zip`, or the CI's `*-windows-full.zip`)
  decides: up to date if its tag is the folder's base or version, else the whole build. A new full
  build removes the old `patches/` folder and becomes the new base;
- a release with neither (only the other system's files, or nothing attached yet, e.g. v0.2.8) is
  passed over.

Linux folders follow the same rules with their own files on the same release:

| | Windows | Linux |
|---|---|---|
| small update | `DownhillRoguelite-update-<version>-for-<base>.zip` | `DownhillRoguelite-update-<version>-linux-for-<base>.tar.gz` |
| whole build | `*-windows.zip` | `*-linux*.tar.gz` |

Each system has its own base (`base.txt` in its folder), so a release should carry both small
updates; a release with only Windows files leaves Linux players where they are, and the other way
round. Linux builds before Beta 2.9 were one file with no `base.txt` and cannot update themselves:
those players download a new build once.

The same rules are in `game/export/playtest/update.ps1` (PowerShell) and are used by the "Let's
Play" launcher (`game/tools/launcher`, which compiles `UpdatePlanner.cs` in). Keep them in step;
`debug/tests/UpdateTests.tscn` checks both.

The updater script is built into the game's code (an embedded resource of the C# project), so each
update also brings the newest updater, and `make_update.py` adds the newest `update.ps1` and
`Update game.bat` to every update zip.

## Publishing a release players get by themselves

From the release worktree, with the commit pushed:

```bash
GODOT="D:/Game dev/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe" \
  python -I game/scripts/publish_release.py v0.3.1-beta 3.1 "Beta 3.1: what's new"
```

It builds the updates against the Beta 3.0 base folders (`D:\Game dev\Builds\release-3.0\windows\DownhillRoguelite-playtest1`
and `...\release-3.0\linux\stage`; never edit them), creates the release as a draft, uploads, compares GitHub's checksums with the local
files and only then publishes. Options: `--prerelease` (try the whole path first: start the game with
`-- --update-prerelease`), `--draft`, `--full <zip>` (also attach a whole build), `--notes <file>`.

Bump `game/core/GameVersion.cs` (`Name` and `Released`) before building, so the footer and the About
page show the new number and its date. Small updates replace the code, so they carry the new date too.

### Major and minor versions

- A **major version** (3.0, 4.0, ...) is a full release: whole Windows and Linux builds
  (`--full`, `--linux-full`; `publish_release.py` refuses a `.0` version without them), plus the small
  updates for the previous base, so its players still download only a few MB. Folders on any older
  base find no update for themselves and take the whole build. Afterwards the new builds are the
  bases: point `BASE` and `LINUX_BASE` in `publish_release.py` at their folders, keep those folders
  untouched, and move the README's download links to that release.
- A **minor version** (3.1, 3.2, ...) is a small update on the newest base.

### What a normal release carries

Two small files: the Windows update and the Linux update, both made against the current base
(Beta 3.0 for both systems). The README's download buttons point at the base release's whole builds
(`releases/download/v0.3.0-beta/...`), not at the newest release: a new player downloads the base
once and the game updates itself on first start. Whole builds are uploaded again only when a new
base is made (updates have grown large); then move the README's two links to that release.

### Linux files for a release

The Linux preset exports the data (`DownhillRoguelite.pck`) and the code (`data_*` folder) next to
the binary, like Windows, so a small update can replace just what changed.

```bash
# A new full Linux build, marked as the base for later small updates (keep a copy of stage/ untouched):
GODOT="<console exe>" bash game/scripts/build_linux.sh "D:/Game dev/Builds/DownhillRoguelite-linux-2.9" v0.2.9-beta
# Later releases: add the small Linux update made against that base, in the same publish call:
python -I game/scripts/publish_release.py v0.3.0-beta 3.0 "Beta 3.0: ..." --linux-base "D:/Game dev/Builds/DownhillRoguelite-linux-2.9/stage"
```

`--linux-full <tar.gz>` attaches a whole Linux build (a new base), `--linux-update <tar.gz>` one
already made with `make_update.py <base> <version> --platform linux`.

### The CI's Windows build

`.github/workflows/windows-full-release.yml` attaches `DownhillRoguelite-windows-full.zip` to
published releases. It is built from git alone, so it lacks the licensed Mixamo character art
(`docs/BUILDING.md`) and `base.txt`; the updater prefers the team's own `*-windows.zip` and uses the
CI's only when a release has nothing else for that folder.

## Testing without GitHub

- `-- --update-feed=<file.json>` reads a release list from a file (the shape of GitHub's API), and a
  `browser_download_url` may be a local path.
- `-- --update-dir=<folder>` treats that folder as the game's, so the check also runs from the editor.
- `-- --update-platform=linux` plans and installs as a Linux folder would (the in-game installer runs
  on any system; `debug/tests/UpdateTests.tscn` installs local `.tar.gz` files with it).
- `update.ps1 -GameDir <folder> -Feed <file.json> [-CheckOnly]` runs the updater on its own
  (exit 10 = update available).

## Startup changes require a full build

Autoloads are registered before `PatchLoader` mounts resource patches, so a patch can't add an
autoload to an older full release: its `project.binary` still lists the old ones. Co-op's
`NetSession` is handled in code instead: `PatchLoader` (the first autoload, and from the new DLL)
adds it to `/root` when the folder's project settings have no `autoload/NetSession`. Beta 3.0 is a
full release and also ships a small update for the 2.9 base; 2.5 folders (whose settings lack 2.9's
autoloads, inputs and shader globals) take the whole build. Test hosting from an actually upgraded
folder (old exe + update, `-- --host`).

`make_update.py` checks this: it refuses a small update when `project.godot` differs from the base's
tag in any line but those `PatchLoader` makes up for (`ALLOWED_SETTINGS`). Make a full release
instead, or pass `--allow-settings` when you are sure the change doesn't matter at startup.
