# Downhill Roguelite — Claude Code instructions

Third-person descent roguelite in Godot 4.6+ (.NET) with C#. Full plan: see the "Downhill Roguelite — Development Plan" doc.
Story and setting: docs/narrative.md (endless descent through Dante's nine circles toward an unreachable godhood).
Current milestone: M8 weather built (fronts per circle, grip, wind on balance, chill and heat); a demo build added a sky shader, ambient music, the voiced narrator (first stage of M11) and a pause menu (branch `demo`). M9 wildlife merged to main; M10 routes and M12 progression (summit start, permadeath, new mountain per run, boons, depth scaling, starting kits) built on branch `m12-progression` (on top of `m10-routes`; neither merged). M13 map and compass built on branch `m13-map` (on top of `m12-progression`); the journal UI on `ui-journal`; M14 gear and durability (clothing slots, pockets, wear, mending at stations) on `m14-gear` (on top of `ui-journal`). M11 conversation (talk with the companion through NobodyWho and a local model in `D:\Game dev\Models\`; narrative/README.md) on `m11-conversation` (on top of `m14-gear`). Playtest 1 (2026-11-07, 18:00) built from `m11-npcs` (the newest branch, pushed; everything above plus travellers at station fires, the map's pan/legend/station arrow, drop, stats, footprints, directional voices and subtitles, draggable UI); public pre-release `v0.1-playtest1` on GitHub (Windows + Linux beta; the model is linked, not attached). Pushes and releases post to the director's Discord through a GitHub webhook, so write commit messages for players to read. Next: walking and IK polish (after the playtest's feedback), clothing models. `-- --promo` renders clean screenshots. Merge and export only when the director says so. Design: docs/survival-loop.md. The director play-tested M7 and approved it; M8 waits for a play-test; M1–M6 still waiting for detailed sign-off (update this line as we progress).
This folder (`downhill-roguelite-(copy)`) is the active project; `D:\Game dev\DownhillRoguelite` is the older copy without the addon.

## Build and run
- Build C#: `dotnet build`
- Run the game: `godot --path .` (Godot .NET binary must be on PATH as `godot`)
- Headless smoke test: `godot --headless --path . --quit-after 300` — check the output for errors
- Build and smoke-test after major changes only (new systems, scene or project-settings changes, anything touching several files), not after every small edit; report errors with file and line
- All of the above in one go: `bash .claude/skills/smoke-test/smoke.sh` (the `smoke-test` skill) — run it before committing a major change
- Visual check (headless can't render): `godot --path . --resolution 1280x720 --write-movie <scratch dir>/frame.png --quit-after 30`, then look at the last frame
- Hand-written `.tscn` `Transform3D(...)` lists the basis row by row (x.x, y.x, z.x, x.y, ...), not as column vectors. Verify any orientation with a visual check.

## Code style (C#)
- Use explicit if/else blocks, never the ternary `?:` operator. Flag it if you slip.
- Nullable types `?` and null-coalescing `??` are fine — don't rewrite them.
- One class per file; file name = class name; PascalCase public members.
- Tunable values go in `[Export]` fields or Resource files, never magic numbers in logic.

## Architecture rules
- Folders: core/, player/, balance/, inventory/, terrain/, map/, progression/, world_ui/, ui/, narrative/, environment/, wildlife/, debug/, art/, audio/
- Systems communicate through C# events / signals and the RunState autoload — no direct cross-system references.
- Every gameplay system gets a debug scene in debug/ and a debug overlay (COM, foot targets, forces).
- UI: the director's journal UI (Don't Starve-inspired, hand-drawn parchment) lives in ui/: HUD (survival dials, descent note, compass, equipment, hotbar, tooltip, notes) and pages (inventory I, map M, journal J). In-world labels (cairns, signs, boons) stay 3D. UI is presentation only: it reads GameEvents/RunState and asks for actions through events.
- Inventory is a snapped 3D cell grid with 90° rotations, not free placement; carried cargo is calculated, not simulated. Read docs/inventory-design.md before any inventory or balance work.
- Balance: read docs/balance-model.md before changing balance/; keep debug/tests/BalanceTests passing (the A-vs-B test is the core check).

## Workflow
- Work one milestone at a time; propose scope in plan mode before large changes.
- Commit after each working step with a clear message. Never push without asking.
- Never edit files in .godot/ (generated cache).
- addons/nobodywho (local LLM GDExtension) is installed; its binaries aren't in git (see addons/README.md). Don't edit addon files.
