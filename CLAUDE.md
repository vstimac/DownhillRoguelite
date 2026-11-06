# Downhill Roguelite — Claude Code instructions

Third-person descent roguelite in Godot 4.6+ (.NET) with C#. Full plan: see the "Downhill Roguelite — Development Plan" doc.
Current milestone: M4 sprint/stamina, jump, health from ragdoll impacts — built on branch wip/m4-vitals-ragdoll, tests passing, not yet merged to main. M1–M4 all waiting for the director's play-test sign-off (update this line as we progress).
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
- Folders: core/, player/, balance/, inventory/, terrain/, map/, progression/, world_ui/, debug/, art/, audio/
- Systems communicate through C# events / signals and the RunState autoload — no direct cross-system references.
- Every gameplay system gets a debug scene in debug/ and a debug overlay (COM, foot targets, forces).
- No flat UI during runs: interfaces are 3D objects in the world (debug overlays excepted).
- Inventory is a snapped 3D cell grid with 90° rotations, not free placement; carried cargo is calculated, not simulated. Read docs/inventory-design.md before any inventory or balance work.
- Balance: read docs/balance-model.md before changing balance/; keep debug/tests/BalanceTests passing (the A-vs-B test is the core check).

## Workflow
- Work one milestone at a time; propose scope in plan mode before large changes.
- Commit after each working step with a clear message. Never push without asking.
- Never edit files in .godot/ (generated cache).
- addons/nobodywho (local LLM GDExtension) is installed; its binaries aren't in git (see addons/README.md). Don't edit addon files.
