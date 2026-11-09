# Terrain direction: Galaxy Tab A9

The goal is a convincing, readable mountain and forest at human scale. The splash art's lesson
is a carrier negotiating a steep world under a meaningful load, with destinations worth reaching.
It is a composition/gameplay reference, not a requirement to copy its illustration style.

## Decisions

- PC development and polish come first. Galaxy Tab A9 remains a design constraint; Android export,
  touch controls and on-device testing are finishing work, as directed by the user. Assume the 4 GB
  variant until confirmed. Sustained 30 FPS is the eventual Android target, not a measured result.
- Keep the deterministic generator, route queries and streamed collision as the starting point.
  Improve rendering and placement separately from navigation/terrain height mathematics.
- Do not replace the entire terrain subsystem before comparing one representative section.
  Terrain3D is the preferred addon candidate to evaluate if authoring/LOD needs justify migration.
  Its Android support remains described as experimental; test the actual tablet and chosen
  version before committing to it. HTerrain currently receives bug fixes rather than new features.
- Build for native rendering performance. No upscaling dependency or expensive real-time cloth,
  vegetation physics, or collider on every pebble/blade.
- Leave chatbot/model downloads out of the core release. Preserve recorded narration and useful
  scripted NPC interactions. Finish excluding the optional development extension/launcher model
  offer when preparing the next export; don't bundle the external 2 GB model.

## First terrain slice

1. Build a repeatable 150–250 m forest section on one fixed seed with the existing player,
   representative loaded backpack, wind and physical obstacles. Include an exposed shortcut,
   sheltered route and a visible valuable destination. Keep a clear passable route.
2. Remove the faceted ground lighting where appropriate with continuous, seam-consistent normals;
   keep the exact same collision heights. Blend dirt, rock, grass and scree at coherent scales.
   Use small reusable textures/materials rather than large unique textures per chunk.
3. Fit trees in metres: canopy above the player, plausible trunk diameter, mixed ages/heights,
   grouped forest and gaps. Initial references: young growth 2–5 m, mature trees 10–22 m,
   individual grass tufts about 0.08–0.45 m. These are tunable biome targets, not one universal forest.
   Existing Kenney trees are placeholders; resizing them does not make their silhouettes realistic.
   Select or author a small licensed set of suitable trees, rocks and grass before final art claims.
4. Add terrain detail at three scales: ground shading/pebbles; clumps, roots, stones; and collidable
   boulders, trunks and ledges. Small detail is batched and collision-free. Only readable obstacles
   big enough to affect feet/body use simple static collision and existing surface/grip rules.
5. Place hazards in coherent patches and clusters with visible safer passages. Walking through
   obvious scree, mud, roots or an exposed ridge should ask for route choice, pace or packing.
   Don't add hidden random tripping or make carrying useful cargo a pure penalty.
6. Use bounded nearby detail, per-chunk/model instancing, near/mid/far vegetation representations,
   limited shadows and small texture budgets. Split large foliage batches spatially enough for
   useful culling. Stream/generate work within measured frame and memory budgets.

## Device acceptance

Compare Compatibility and Mobile on the tablet, using the same route, camera and load. Record
frame-time distribution, streaming spikes, memory, draw calls and triangles. Test ten minutes
for thermal slowdown, then inspect grass overdraw, shadow cost and collision workload separately.
Desktop fixed-fps captures are visual checks, not performance evidence for Android. During finishing,
verify C# Android export, touch controls and device deployment; this project currently uses Godot
4.7.2 .NET with net9.0 configured for Android, but no Android export has been validated here.

## References checked

- Terrain3D platform status: https://github.com/TokisanGames/Terrain3D/blob/main/doc/docs/platforms.md
- HTerrain maintenance status: https://github.com/Zylann/godot_heightmap_plugin
- Godot Android export: https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_android.html

These are architecture/asset decisions and an implementation sequence. Terrain replacement,
new final vegetation art, on-device FPS and a new package size are not implemented or proven yet.
