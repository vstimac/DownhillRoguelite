# Downhill Roguelite on Linux

Beta. Tested on x86_64 desktops (Arch/CachyOS, Ubuntu). Feedback goes in the Discord.

## Run it

1. Unpack the archive anywhere you can write to.
2. Run `./DownhillRoguelite.x86_64` (if it won't start: `chmod +x DownhillRoguelite.x86_64`).

You need a GPU with Vulkan drivers (Mesa or the NVIDIA driver). Nothing else is required.

## Updates

From Beta 2.9 the game updates itself: when a new version is out, **Update** appears at the top of
the title menu. Pick it and the game downloads the update (a small one when it can), checks it,
installs it and starts again. Your `Models` folder and saves are left alone. The game folder must be
somewhere you can write to. Options → Gameplay → **Check for new versions** turns the check off.

Builds older than Beta 2.9 cannot update themselves: download the newest build once.

## The companion's voice (optional)

The companion walking beside you can talk back when you speak to them. That needs a language
model, which is too large to ship with the game:

1. Download `qwen2.5-3b-instruct-q4_k_m.gguf` (2.1 GB) from
   <https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf>
2. Put it in the `Models` folder next to the game.
3. Optional check: `sha256sum Models/qwen2.5-3b-instruct-q4_k_m.gguf` should print
   `626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d`.

Without the model the game plays normally; the companion just stays quiet. The model runs on your
graphics card (about 2.5 GB of video memory) and loads the first time you talk.

## Spoken replies

The narrator's lines are recorded and always play. The companion's live replies are read aloud by
your system's speech service, **speech-dispatcher**:

- Arch / CachyOS: `sudo pacman -S speech-dispatcher espeak-ng`
- Ubuntu / Debian: `sudo apt install speech-dispatcher espeak-ng`
- Fedora: `sudo dnf install speech-dispatcher espeak-ng`

If it isn't installed, replies are shown as subtitles only. To turn the system voice off even when it
is installed, start the game with `./DownhillRoguelite.x86_64 -- --no-tts`.

## Known issues

- A line like `register-docs` in the terminal comes from the language-model addon and is harmless.
- Wayland: the game runs through XWayland by default. If the window misbehaves, try
  `./DownhillRoguelite.x86_64 --display-driver x11`.
- Saves and options live in `~/.local/share/godot/app_userdata/Downhill Roguelite/`.
