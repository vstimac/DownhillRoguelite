# wildlife

Animals on the slopes (M9). Scenery, not enemies: kinematic capsule collisions, no combat.

- `WildlifeSpecies` (resource): model, scale and rotation (the packs differ), which clip is idle, walk and flee, speeds, shyness, group size, and habitat: circles, surfaces, steepest slope.
- `default_wildlife.tres`: all species. Limbo has cows, horses, sheep, llamas, rats and frogs; Lust horses and zebras; Gluttony pigs, rats and frogs; Greed llamas and rats; Wrath frogs and snakes; Heresy spiders; Violence snakes and wasps; Fraud snakes and spiders; Treachery rats; beyond the ninth, Kenney's cube pets.
- `WildlifeSpawner`: keeps up to 22 animals in a 25 to 70 m ring around the carrier, mostly ahead downhill, on ground found by a ray; removes animals far away or well behind; a fall (DownedChanged) scatters everything within 20 m; a new run clears them. Reads only GameEvents and RunState.
- `WildlifeAnimal`: grazes, wanders, turns to watch the carrier, bolts when approached (farther when the carrier sprints), keeps to the ground. Refreshes floor contact even while grazing, so removed terrain or displaced supporting bodies cannot leave it floating.

Debug: `debug/TestWildlife.tscn` lines up every species beside a 1.8 m pole; `WildlifeDebugOverlay` (F3) shows count, cost and state. Tests: `debug/tests/WildlifeTests.tscn`.

`debug/tests/WildlifePhysicsTests.tscn` checks support removal, displacement, resting and capsule contact on a 45-degree slope. The population test measures the capsule's bottom sphere against its actual support plane; a vertical ray below its origin gives misleading gaps on slopes and obstacle edges.

Population tests print their RNG seed. To replay the spawn selection, append `-- --wildlife-seed=8` when running `WildlifeTests.tscn`. `WildlifeSpawner.PopulationSeed` can also be set before its first tick. Normal gameplay still picks a random population seed.
