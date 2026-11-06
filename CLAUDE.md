# Downhill Roguelite — Claude Code instructions

Third-person descent roguelite in Godot 4.6+ (.NET) with C#. Full plan: see the "Downhill Roguelite — Development Plan" doc.
Current milestone: M0 Project setup (update this line as we progress).

## Build and run
- Build C#: `dotnet build`
- Run the game: `godot --path .` (Godot .NET binary must be on PATH as `godot`)
- Headless smoke test: `godot --headless --path . --quit-after 300` — check the output for errors
- After every change: build, then run the smoke test, and report errors with file and line

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

## Workflow
- Work one milestone at a time; propose scope in plan mode before large changes.
- Commit after each working step with a clear message. Never push without asking.
- Never edit files in .godot/ (generated cache).
