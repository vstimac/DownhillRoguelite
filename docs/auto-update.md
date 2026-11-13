# Automatic updates

Play-testers stay on the newest release without downloading anything by hand.

## What the player sees

1. The game starts. The title screen asks GitHub for the release list in the background (10 s
   timeout; offline, nothing happens and nothing waits).
2. If there is a newer release for this game folder, **Update** appears at the top of the menu, with
   the release's name and the download size, and the corner says "new version out".
3. Picking it closes the game and opens the updater window: it downloads, checks the file's SHA-256,
   unpacks to a temporary folder, installs, and starts the game again. If anything fails, nothing
   in the game folder has changed and the old version starts.

Options → Gameplay → **Check for new versions** turns the check off. "Update game.bat" in the game
folder does the same by hand.

## How it decides (`game/core/update/UpdatePlanner.cs`)

Newest published release first; drafts are skipped, pre-releases too (unless `-- --update-prerelease`):

- a release with `DownhillRoguelite-update-<version>-for-<base>.zip`, where `<base>` is the folder's
  `base.txt`, decides: up to date if `<version>` equals `version.txt`, else that small update;
- otherwise a release with a Windows build (`*-windows.zip`, or the CI's `*-windows-full.zip`)
  decides: up to date if its tag is the folder's base or version, else the whole build. A new full
  build removes the old `patches/` folder and becomes the new base;
- a release with neither (Linux only, nothing attached yet, e.g. v0.2.8) is passed over.

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

### The CI's Windows build

`.github/workflows/windows-full-release.yml` attaches `DownhillRoguelite-windows-full.zip` to
published releases. It is built from git alone, so it lacks the licensed Mixamo character art
(`docs/BUILDING.md`) and `base.txt`; the updater prefers the team's own `*-windows.zip` and uses the
CI's only when a release has nothing else for that folder.

## Testing without GitHub

- `-- --update-feed=<file.json>` reads a release list from a file (the shape of GitHub's API), and a
  `browser_download_url` may be a local path.
- `-- --update-dir=<folder>` treats that folder as the game's, so the check also runs from the editor.
- `update.ps1 -GameDir <folder> -Feed <file.json> [-CheckOnly]` runs the updater on its own
  (exit 10 = update available).
