---
name: smoke-test
description: Build the C# project and run the Godot headless smoke test for DownhillRoguelite, then report results. Use after every code, scene or project-settings change, before every commit, and whenever the user asks to build, test, verify or smoke-test the project.
---

# Smoke test

Run the bundled script from the project root:

```bash
bash .claude/skills/smoke-test/smoke.sh
```

Pass a frame count to run longer, e.g. `bash .claude/skills/smoke-test/smoke.sh 600`. Use a longer run when the change involves timers, spawning or anything that only happens after a few seconds.

## What it checks

1. Godot is the .NET build and its version matches `Godot.NET.Sdk/<version>` in the `.csproj`. A mismatch breaks the C# build in the editor even if `dotnet build` passes.
2. `godot --headless --import` runs without errors. This also generates `.uid` files for new scripts and resources.
3. `dotnet build` finishes with 0 errors and 0 warnings. The project treats warnings as errors.
4. `godot --headless --path . --quit-after <frames>` prints no `ERROR`, `WARNING` or exceptions.
5. A short verbose run confirms the main scene, every autoload and the .NET runtime actually loaded. Silent output alone doesn't prove that.
6. It lists untracked `.uid` files that should be committed.

Godot is found through `$GODOT`, then `godot` on PATH, then a `Godot_v*_mono_win64` folder next to the project.

## Reporting

- On `RESULT: PASS`, say so in one line, and mention any untracked `.uid` files.
- On `RESULT: FAIL`, report each failure with its file and line, as CLAUDE.md requires. The script prints the matching lines; for more context, read the logs in the folder printed after `Logs:` (`import.log`, `build.log`, `smoke.log`, `verbose.log`).
- Fix the failures and run the script again before committing. Don't commit on a failing smoke test unless the user says to, and say so in the commit message if they do.

## Limits

Headless mode uses a dummy renderer. This test proves the project builds, loads and runs without errors. It doesn't prove anything looks right: shaders compile only partly, and lighting, shadows and materials aren't rendered. For visual changes, ask the user to open the scene and check it.
