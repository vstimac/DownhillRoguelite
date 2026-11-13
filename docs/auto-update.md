# Automatic updates

Play-testers stay on the newest release without downloading anything by hand.

## What the player sees

1. The game starts. The title screen asks GitHub for the release list in the background (10 s
   timeout; offline, nothing happens and nothing waits).
2. If there is a newer release for this game folder, **Update** appears at the top of the menu, with
   the release's name and the download size, and the corner says "new version out".
3. Picking it updates the game and starts it again. If anything fails, nothing in the game folder
   has changed and the old version stays.
   - **Windows:** the game closes and the updater window opens (`update.ps1`): it downloads, checks
     the file's SHA-256, unpacks to a temporary folder, installs, and starts the game.
   - **Linux:** the game does the same steps itself (`game/core/update/UpdateInstaller.cs`) and the
     menu shows the progress; no terminal, PowerShell or extra programs are needed. A running
     program's files can be replaced on Linux, so no separate updater is used.

Options → Gameplay → **Check for new versions** turns the check off. "Update game.bat" in the game
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
  python -I game/scripts/publish_release.py v0.2.9-beta 2.9 "Beta 2.9: what's new"
```

It builds the update against Beta 2.5's folder (`D:\Game dev\Builds\DownhillRoguelite-beta-2.5\...`,
never edit it), creates the release as a draft, uploads, compares GitHub's checksums with the local
files and only then publishes. Options: `--prerelease` (try the whole path first: start the game with
`-- --update-prerelease`), `--draft`, `--full <zip>` (also attach a whole build), `--notes <file>`.

Bump `StartScreen.VersionText` before building, so the corner shows the new number.

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
