# Streaming resources and descent motion checks

Worktree: `downhill-roguelite-(copy)`, branch `fix/gameplay-math-audit`, Godot 4.7.2 .NET.
Main and Claude's release worktree remain unchanged.

## Why this step exists

The preceding full suite logged `Handle is not initialized` in Godot's
`SwapGCHandleForType`, through `PackedScene.Instantiate` in `SetPieceNode.Build`.
The same descent test reported 317,784.8 m displacement in 30 simulated seconds, while its
assertion accepted any finite distance greater than the minimum. That result did not prove
healthy gameplay. The old log did not contain the seed, so the exact run cannot be replayed.

## Changes

- `environment/SetPiecePlacer.cs` keeps strong managed `PackedScene` references for its finite
  configured model catalog. Streaming and `Forget()` reuse those wrappers; exiting the placer
  releases the cache. This removes repeated transient wrapper loads. It is lifetime hardening,
  not proof of the original native failure's cause.
- `environment/SetPieceNode.cs` accepts an optional model factory. Existing direct callers keep
  their source scene wrappers for the lifetime of that built piece. Invalid scenes or roots
  produce path-specific diagnostics; native errors remain visible. Placement and collision
  calculations are unchanged.
- `TraceModelInstantiation` on the placer prints each exact model path before instantiation.
  The catalog is established when loaded. Changing a resource to reference a new model path
  while that placer is alive requires reloading the placer/catalog.
- `debug/tests/DescentPlaytestTests.cs` logs seed, character, positions, physics ticks, falls,
  teleports, peak velocity and the largest position step. It rejects implausible unannounced
  movement using controller speed limits with generous correction allowances, without clamping
  the character or changing reported distance. Declared teleports are counted separately; the
  first-station mode still requires zero teleports.
- `debug/tests/SetPieceTests.cs` unloads objects, forces managed collection, checks source wrapper
  survival, then rebuilds complete pieces across three stage ranges.
- New `debug/tests/RagdollMotionTests.cs/.tscn` checks actual native falls and recovery at the
  origin, with a rotated carrier, and with a translated world. It checks finite motion, bounded
  displacement, physical-hips tracking and recovery drift. No ragdoll/controller physics was changed.

## Reproduce

Build C#, then run the following scenes with `--headless --path . --fixed-fps 60`:

1. `res://debug/tests/DescentPlaytestTests.tscn -- --seed=1` (replace seed to replay a mountain).
2. `res://debug/tests/SetPieceTests.tscn` for unload/collection/rebuild checks.
3. `res://debug/tests/RagdollMotionTests.tscn` for physical world-space invariants.

For interactive inspection, use the existing playground. The new regression scenes are automated
tests that exit when finished. F9 in a normal graphical session enables five-frame viewport capture.

## Validation

Clean build: zero warnings/errors. SetPieceTests: 10 passing checks, including eight distinct model
wrappers surviving collection and complete reconstruction in three stage ranges. RagdollMotionTests:
15 checks pass; controlled 2 m/s falls travel about 0.99 m, largest body step about 0.146 m,
physical-hips tracking error zero in the sampled frames, and no post-recovery horizontal drift.

Six independent 30-second descent replays passed:

| Seed | Downhill displacement | Largest step | Falls | Teleports |
| --- | --- | --- | --- | --- |
| 1 | 129.3 m | 0.085 m | 0 | 0 |
| 2 | 88.2 m | 0.170 m | 1 | 0 |
| 3 | 132.3 m | 0.082 m | 0 | 0 |
| 4 | 131.9 m | 0.081 m | 0 | 0 |
| 5 | 129.6 m | 0.084 m | 0 | 0 |
| 6 | 128.9 m | 0.082 m | 0 | 0 |

No engine errors appeared in these focused logs. They establish these runs, not universal freedom
from the earlier native failure. The 317 km anomaly was not reproduced and its cause remains
unconfirmed; future occurrences now include replayable diagnostics and fail the movement check.

Artifacts: `D:/Game dev/math-audit-artifacts/descent-diagnostics-seed1.log` through `seed6.log`,
`streaming-gc-tests.log` and `ragdoll-motion-initial.log`. The final full suite at
`streaming-motion-final-smoke.log` passed 52 test scenes / 696 checks, including the improved
descent check on random seed 10941887575154480644 (129.9 m, zero teleports, largest step 0.522 m).
No resource-handle error appeared. Its only failure was the existing vegetation timing check
at `debug/tests/TerrainTests.cs:272`: 25.6 ms against 16 ms. Individual logs are in
`C:/Users/user/AppData/Local/Temp/tmp.ru2YquTrze`. No threshold was weakened. The full suite is
was not green, so these changes were kept uncommitted until the terrain work completed.

Subsequent readiness validation passed all **53 scenes / 715 checks**, including the unchanged
16 ms terrain assertion. Log: `plant-optimized-full-smoke.log`; individual logs:
`C:/Users/user/AppData/Local/Temp/tmp.02qC36RJoG`. See
[terrain-streaming-performance.md](terrain-streaming-performance.md) for the allocation/scheduling fix.
