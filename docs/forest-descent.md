# Forest terrain: first playable pass

PC development comes first. Android/Tab A9 export, controls and device testing remain finishing
work; memory, instancing and collider costs still guide this implementation.

## Test it

1. Open `debug/TestForestDescent.tscn` in Godot 4.7.2 .NET and press F6.
2. Choose **New Run**. The lab starts 120 m down the seeded route, in the first forest.
3. Walk the next 180 m. Five clearly visible, vaultable rocks alternate across the trail; steer
   around them or use the existing jump/vault action. Clear passages remain beside them.
4. Press **F7** for the next checkpoint, **F3** for terrain/render diagnostics, **F4** for weather,
   **I** for the existing inventory, or **Esc** for options. The pump workshop/delivery from the
   previous expedition remains available nearby.

The lab initially uses seed 1. A new roguelite run can still create a new mountain. Ordinary F5
keeps its original main scene. Ground rendering, first-biome foliage and detail improvements
also apply there; the hand-placed test obstacles and clearer forest lighting belong to this lab.

## Implemented

- Continuous per-vertex lighting normals and surface colours, including matching chunk borders.
  The generated ground positions, heightmap collision, route geometry and physical obstacle
  definitions are unchanged. `SmoothShading = false` retains the old faceted rendering mode.
- Two small reusable ground materials: forest floor at 3.2 m per tile and rock/gravel at 1.5 m.
  Four 1K colour/normal maps use mipmaps and VRAM compression. Surface colours, slope, macro
  variation and snow blend the maps; no displacement or additional terrain physics is introduced.
- Three original, baked procedural pine prototypes with branches/needle sprays and index LODs.
  Mature trees are 10–18 m; 15% of candidates use 2.5–5 m sapling proportions. Tree clusters and
  gaps vary deterministically. Tree art is still a prototype, not scanned/final production art.
- Three opaque grass shapes fitted to 0.12–0.4 m, with a subtle cosmetic breeze and distance
  height reduction. Other biomes retain their existing art and legacy size interpretation.
- Decorative 2.5–9 cm pebbles, without rigid bodies or colliders. They are not hidden trip hazards.
  Large rocks and trunks remain physical, with the existing footing/vault systems.
- Trees are grouped by mesh per chunk. Nearby ground detail is grouped by kind/model in 16 m
  patches, so a whole mountain is not one uncullable instance batch. Small detail casts no shadows
  and uses no alpha-blend distance fading. Individual trunk cylinders share one static compound
  body per chunk, with radii fitted to the new tree proportions and wood surface metadata.
- Foliage construction yields across frames, sharing a configurable 3 ms **cooperative** budget
  across chunks. A single engine/driver call or cold-start compilation can exceed that budget;
  this is not a hard guarantee that every frame takes 3 ms. Diagnostics retain actual peaks.
- Ground vegetation rests on the rendered triangle diagonal, rather than a bilinear approximation
  that could leave small plants floating. Ground collision itself is preserved.
- The lab keeps the existing sky/day/night/weather systems, with less dense daytime haze and
  lower ambient fill so nearby detail and the forest silhouette remain readable.

## Editing

Select `terrain/default_terrain.tres`, expand **Biomes**, then the first **Limbo** entry. Adjust
TreeDensity, TreeHeight, SaplingShare, GrassDensity and GrassScale. With GroundPlantsInMetres
enabled, GrassScale is a height range in metres. TerrainSettings also contains draw distances,
PlantBatchSize, PlantBuildBudgetMilliseconds and pebble sizes/density. Edit settings before
starting a run; the generator reads them on a background thread.

Select **Terrain** in the test scene for texture scale/strength and shader paths. Select
**WorldEnvironment** for the lab's environment resource. `tools/forest/generate_forest.gd`
rebuilds the six frozen foliage assets; its constants are source-art choices, not gameplay tuning.
Replacement tree scenes currently need one combined mesh: the existing ModelFit pipeline uses
the first mesh, so an arbitrary multi-mesh tree GLB is not automatically supported.

## Validation and measurement

`debug/tests/ForestTerrainTests.tscn` checks unchanged height/vertex/obstacle data, seamless
normals/colours, finite lighting normals, deterministic placements, model paths, metre fitting,
sapling/mature ranges, grounding against triangles, bounded detail counts, 1K mipmapped textures,
batched tree visuals/compound trunk collision, and incremental construction. Existing TerrainTests still
checks live ground collision, 300 m walking, route clearance and bounded streaming.
The food-theft check in BeastTests resets both actors to a short clear trail approach: it must test
a successful hit, rather than whether a random stalking orbit happens to have a tree in its way.
Roaming, charging, dodging and megaphone checks retain their separate assertions and real collisions.

Native graphical inspection is separate from headless validation. A repeatable short PC sample
can be recorded with the Godot console binary, without `--fixed-fps` or screenshots:

```
godot --path . res://debug/TestForestDescent.tscn -- --skip-start --seed=1 --weather=clear --hour=12 --capture-off --autowalk --walk-seconds=10 --give=ore_sample,water_flask,ration,rope --forest-benchmark=user://forest-benchmark.json
```

The lab waits 3 seconds, samples 12 seconds, writes median/P95/worst frame time, draw calls,
triangles, pixel dimensions, load and streaming peaks, then exits. This is a short native PC
sample, not a sustained benchmark or an Android result. Startup streaming peaks are retained
in the reported counters. Performance also depends on camera, resolution, weather and background
software; a single average FPS does not establish a finished optimization pass.

Local validation, 2026-11-09: the required smoke script passed build (zero warnings/errors), import,
main-scene/runtime checks and all 49 test scenes (590 checks). Forest tests measured 50 trunk shapes
in one body and three visual batches; the full terrain streaming test retained a 14.6 ms plant peak.
Native captures every five rendered frames were inspected with no engine errors in the render log.

The 12-second native sample on an RTX 3060 Laptop GPU, Forward+, **2208 x 1380 actual pixels**,
seed 1 and 11.3 kg load measured median 10.965 ms, P95 13.813 ms and worst 34.823 ms. It averaged
851 draw calls and 811k triangles. The retained native plant-construction peak was 20.476 ms,
including startup: individual engine calls can still exceed the cooperative budget. These peaks
remain optimization work; this is neither a mobile result nor a locked frame-rate guarantee.

## Files

- Terrain: `terrain/TerrainSettings.cs`, `Biome.cs`, `PlantData.cs`, `PlantKind.cs`,
  `TerrainGenerator.cs`, `TerrainChunk.cs`, `TerrainStreamer.cs`, `terrain_ground.gdshader`,
  `forest_grass.gdshader`, `forest_environment.tres`.
- Art and source: `art/environment/forest/`, `art/environment/ground/`,
  `tools/forest/generate_forest.gd`. Texture credits/licensing/hashes live beside the maps.
- Test scene/diagnostics: `debug/TestForestDescent.tscn`, `ForestDescentLab.cs`,
  `TerrainDebugOverlay.cs`, `debug/tests/ForestTerrainTests.cs` and its scene,
  `debug/tests/BeastTests.cs` (food-hit fixture).
- Direction: this document and `docs/mobile-terrain-plan.md`. The companion editing guide is
  `docs/player-models-and-clothes.md`; it documents the existing player pipeline without changing it.

## Remaining

Review the forest from ground level while carrying cargo, in rain and darkness. Refine pine
silhouettes, add more species/understory and improve the trail/terrain material transitions as
needed. The test rocks are authored lab content; generated route obstacle profiles still need
their own design/balance pass. Existing character clothing/walking/backpack issues are separate
and remain on the agreed backlog. No terrain addon migration, Android export, sustained device
performance claim, final vegetation-art claim or exported package size is included in this step.
