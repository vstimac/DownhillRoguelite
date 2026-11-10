# Building Downhill Roguelite

For developers building from source. Players want [LINUX.md](LINUX.md) or the release page.

## Requirements

| What | Version | Notes |
| --- | --- | --- |
| Godot **.NET** (mono) | 4.7.2 | The standard (non-.NET) Godot can't build this project. |
| .NET SDK | 8 or newer | `global.json` accepts 8.x and any newer major. |
| Export templates | 4.7.2 .NET | Only to export (Editor > Manage Export Templates, or `scripts/build_linux.sh` in CI). |
| NobodyWho | 13.0.0 | Local language-model addon; its libraries aren't in git (see below). |
| curl, unzip | any | Used by the setup scripts. |

All pinned versions and checksums live in [`scripts/versions.sh`](../game/scripts/versions.sh).

The Godot project lives in the `game/` folder: run every command below from there (`cd game`).

## Linux: quick start

```bash
export GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64   # or put it on PATH as "godot"
bash scripts/setup_linux.sh            # add --with-model for the companion's 2.1 GB model
godot --path .                         # run
bash scripts/build_linux.sh            # export to build/linux/ and pack a .tar.gz
```

`scripts/check_assets.sh` prints what's present at any time: `[OK]`, `[WARN]` (missing, but the
game runs with a fallback) or `[FAIL]` (missing and needed).

## Windows

Install Godot 4.7.2 .NET and the .NET SDK, then from Git Bash: `bash scripts/fetch_nobodywho.sh
windows` and `dotnet build`. Export with the "Windows Desktop" preset (unchanged by the Linux
scripts). The smoke test: `bash .claude/skills/smoke-test/smoke.sh`.

## Assets that aren't in git

### NobodyWho libraries
Each prebuilt library is about 140 MB, over GitHub's 100 MB file limit. `scripts/fetch_nobodywho.sh`
downloads the official 13.0.0 release from <https://github.com/nobodywho-ooo/nobodywho/releases>,
copies only the libraries into `addons/nobodywho/` (the committed `nobodywho.gdextension` stays),
and compares their SHA-256 with the build this project was tested with.

Godot may print a `register-docs` warning when the addon loads. It comes from the addon itself and
is harmless; we don't modify addon files.

### The companion's model (optional)
`qwen2.5-3b-instruct-q4_k_m.gguf` (2.1 GB, Qwen Research License) from Hugging Face. In the editor
it is read from `../Models/` (next to the project folder); an exported game reads `Models/` next to
the executable, or `-- --llm-model=/path/to/file.gguf`. `scripts/fetch_model.sh [game-folder]`
downloads and verifies it. Without it the companion stays silent; nothing else changes.

### The Mixamo character
`art/characters/mixamo/carrier.fbx` (and the vault animation) are Mixamo assets. Adobe's terms allow
them inside the shipped game but not as raw files, so they are ignored by git and must not be
committed or uploaded anywhere. Without them the carrier falls back to the Quaternius mannequin
and everything else works. To get them, follow [art/characters/mixamo/README.md](../game/art/characters/mixamo/README.md)
(download from mixamo.com with your own Adobe account, FBX Binary, With Skin).

## Text-to-speech on Linux

The companion's live replies use Godot's `DisplayServer` text-to-speech, which on Linux needs
**speech-dispatcher** (`libspeechd.so.2`). The game checks for that library before asking for
voices and otherwise falls back to subtitles only, so a missing library no longer logs
"Parameter synth is null". `-- --no-tts` turns the voice off.

## Continuous integration

`.github/workflows/linux-build.yml` builds from a clean checkout on Ubuntu: it installs Godot .NET
and the Linux templates (verified against Godot's SHA512 sums), fetches NobodyWho, builds the C#,
runs the smoke test and the test scenes, exports, and uploads the `.tar.gz` as an artifact. It runs
on pull requests to `main`, on `v*` tags, and by hand (Actions > Linux build > Run workflow).
