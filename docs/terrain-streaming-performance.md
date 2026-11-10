# Terrain streaming performance

Godot 4.7.2 .NET, `fix/gameplay-math-audit`. PC development is the current target; Galaxy Tab A9
export and device testing remain finishing work. These results are not a mobile performance guarantee.

## What caused the investigation

The full terrain test repeatedly exceeded its unchanged 16 ms vegetation limit, including a
25.6 ms sample. A 3 ms cooperative budget could stop only between iterator steps; one step
previously included batch preparation, buffer upload and scene insertion together.

Profiling also confirmed managed GC pauses inside measured plant work. An intermediate 18 ms
sample included 15.55 ms of GC pause. The timer still records the entire wall time: no pause is
subtracted and no threshold is relaxed. This identifies a contributor, not every possible native
upload, renderer or operating-system scheduling cost.

## Changes

- Smooth terrain uses shared grid vertices and triangle indices. A 64-cell chunk has 4,225
  vertices and the same 8,192 triangles. Expanding its indices reproduces the legacy ordered
  triangle corners exactly; winding, height collision, normals and colours are preserved.
- Position/normal/colour/index arrays occupy 267,304 bytes instead of 983,040 bytes, about 73%
  less raw mesh storage. The legacy flat-shaded path remains unindexed.
- After upload, a chunk retains only its plant list for incremental construction, rather than
  the complete generation object and its large mesh arrays. The temporary Godot argument array
  is disposed after upload. Caller-owned generated arrays are not cleared or modified.
- Plant batches store indices into the original plan instead of copying PlantData structs.
- Construction yields between allocation, transform preparation, buffer upload and insertion.
  Transform preparation yields every `PlantWorkItemsPerSlice` items, default 128.
- The runtime `PlantBuildBudgetMilliseconds` default is 1 ms; the loading-screen budget remains
  40 ms. These are cooperative limits, so a single native call can still overrun them.
- Cancellation releases the iterator, retained plant list and any detached trunk body.

Density and generated placements are unchanged. Trees retain individual trunk collision shapes
in one compound body; small detail receives no extra colliders or shadow passes. This change does
not reduce terrain resolution, the number of triangles or the amount of vegetation.

## Inspect and rerun

Build C#, then use the project's Godot .NET executable from the project directory:

```powershell
godot --headless --path . --fixed-fps 60 res://debug/tests/TerrainTests.tscn -- --profile-plants
godot --headless --path . --fixed-fps 60 res://debug/tests/ForestTerrainTests.tscn
godot --path . --fixed-fps 60 res://debug/tests/ForestTerrainTests.tscn
```

`--profile-plants` reports the largest iterator slice, managed allocation and GC-pause deltas
for slow plant steps. These diagnostics are enabled only by that argument. Main-thread allocation
counts exclude background generator and native allocations; a wall-time spike alone is not CPU proof.

Headless validation decodes the uploaded packed transform buffer once per batch because the dummy
renderer does not provide useful GetInstanceTransform results. The graphical test uses the actual
native accessor. Both compare all generated instance transforms, including rotation and scale.

For an interactive walk, open `debug/TestForestDescent.tscn` and press F6. See
[the forest guide](forest-descent.md) for checkpoints and the native benchmark command. Keep
viewport capture off while measuring performance: five-frame screenshot capture adds readback and
encoding work. Inspect images in a separate run.

## Validation

Focused results from separate runs on the development PC:

| Check | Result |
| --- | --- |
| TerrainTests, headless | 15 checks pass; slowest plant step 3.45 ms, below 16 ms |
| ForestTerrainTests, headless | 16 checks pass; worst shared plant frame 2.9 ms |
| ForestTerrainTests, native Windows | 16 checks pass; worst shared plant frame 11.8 ms |
| Generated-instance payload | All 2,584 instances match in both renderers; no missing or duplicate plants |
| Trunk/cancellation checks | 50 trunk shapes in one body; unfinished-build orphan delta zero |

Headless timers are not GPU frame-rate measurements. These samples do not establish sustained,
thermal or device performance, or universal freedom from GC and native-call spikes.

Logs are in `D:/Game dev/math-audit-artifacts`: `plant-final-budget-profile.log`,
`plant-final-forest-tests.log` and `plant-transform-native.log`. Final full-suite validation in
`plant-optimized-full-smoke.log` passed all **53 scenes / 715 checks**, with zero build warnings
or errors. Individual logs: `C:/Users/user/AppData/Local/Temp/tmp.02qC36RJoG`.
The actual forest was rendered and inspected (`plant-indexed-forest-view.log`, five-frame
capture in `plant-indexed-forest-view/20261109-231930-779-25864`). Ground surfaces and foliage
render correctly; the existing debug scene's crowded HUD/prototype artwork remains separate.

## Clean-checkout follow-up (2026-11-10)

The earlier 53-scene pass did not reproduce reliably in an isolated checkout: its plant peak
reached 29.8 ms. Further investigation found large numbers of finalizable per-frame Godot
bindings in animation and physics queries. The follow-up caches those parameter/key objects,
reuses owned queries and disposes temporary results. It also shares the remaining triangle
topology per generator and stores plant records in small blocks.

The corrected isolated candidate passes **54 scenes / 730 checks** with the same 16 ms limits.
Its plant peak is 12.5 ms, chunk upload 6.5 ms and shared forest construction 3.9 ms. See
[gc-frame-allocation-audit.md](gc-frame-allocation-audit.md) for evidence, exact ownership rules,
allocation regression results and remaining performance limits.

## Implementation files

Implementation: `terrain/TerrainChunk.cs`, `TerrainChunkData.cs`, `TerrainGenerator.cs` and
`TerrainSettings.cs`. Regression coverage: `debug/tests/ForestTerrainTests.cs`. This guide records
the investigation and limits; the existing 16 ms terrain assertion remains unchanged.
