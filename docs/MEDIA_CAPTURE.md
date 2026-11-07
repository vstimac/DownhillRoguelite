# Genuine gameplay media for the GitHub README

The character poster in `art/characters` is **concept art**, not captured gameplay. Do not present AI renders as screenshots.

## Take real gameplay screenshots

1. Run the **actual released Windows build** (currently v0.2.1-beta) or a locally tested game build.
2. Capture at **1920 × 1080 or 2560 × 1440**, ideally without editor/debug overlays. Use the game's `-- --promo` mode if running a source build that supports it.
3. Capture three distinctive moments:
   - A loaded character descending toward the next station (exterior gameplay)
   - The 3D backpack/inventory UI (actual game UI, no concept render)
   - A dramatic route/weather/waypoint moment with HUD
4. Save actual captures as `docs/media/gameplay-descent.png`, `docs/media/gameplay-backpack.png`, and `docs/media/gameplay-route.png`.

## Make a short real gameplay GIF

Record a **10–15 second** MP4 of an actual descent and use FFmpeg:

```bash
ffmpeg -i gameplay.mp4 -filter_complex "[0:v]fps=12,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" -loop 0 docs/media/gameplay-loop.gif
```

Aim for under about 10 MB so the repository's README loads comfortably. Avoid animated concept art masquerading as captured gameplay.

## Add the media to README

When the files exist and have been reviewed, replace the text-only screenshot placeholder with:

```markdown
## Gameplay (actual in-game capture)

![DownhillRoguelite descent gameplay](docs/media/gameplay-descent.png)

| Backpack management | Routes and weather |
| --- | --- |
| ![Real backpack UI](docs/media/gameplay-backpack.png) | ![Real route HUD](docs/media/gameplay-route.png) |

![Real descent footage](docs/media/gameplay-loop.gif)
```

Do not claim unrecorded gameplay previews already exist. Capture videos/images only from an actually working build, with no misleading edits.
