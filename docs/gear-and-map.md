# Gear, durability, map and compass

Decided 2026-11-07 from the director's brief. Builds on `docs/inventory-design.md` (the snapped 3D grid, packing is balance) and keeps its rule: no flat UI during runs. Scheduled as M13 (map and compass) and M14 (gear and durability), after M12 progression.

## M13 Map and compass

- **The map is an object in the world.** The carrier holds it up in front of the camera (a key toggles it), as a sheet of paper with the mountain drawn on it, not a screen overlay. A **compass** sits on top of it, its needle pointing to the real north of the world.
- **What the map shows:** the stretch of mountain the carrier has seen, the stations and paths as they are discovered, the carrier's position. It is a hand-drawn chart, not a satellite view: unexplored ground stays blank.
- **Waypoints:** the carrier can put marks on the map, and each mark appears in the world while walking, as a marker (a small flag and light) that can be seen from far away and through fog.
- **Drawing:** with **paper and a pencil** found in the world, the carrier can draw freely on the map (lines, notes, crosses). Without them, only the basic marks are possible. Pencils wear down; paper runs out.
- The compass also works on its own, held in the hand, when the carrier has no map.
- Narrator hooks: `map_viewed` already exists in the 500 lines.

## M14 Gear and durability

Inspired by survival games' equipment slots (for example Project Zomboid), but the containers are physical 3D grids you pack by hand, not lists.

- **Equipment slots on the body:** backpack (on the back), and clothing: hat, torso (shirt or jacket), body or legs, cape or coat, gloves, boots (exact list to settle while building).
- **Containers are 3D grids:** the backpack is the grid that exists today. Pockets are small grids on clothing (a jacket with two 1 × 1 × 1 pockets, trousers with one). Everything stored keeps its real position and mass, so it still counts for balance.
- **Clothing does something:** warmth against night cold and weather chill, rain protection, pockets for capacity. A cape catches wind (balance).
- **Found, not made:** every item is scattered in the world (supplies, caches on hazardous paths, abandoned cargo). There is no crafting, chopping or mining.
- **Durability, no rarity:** each item has hit points. Falls, scree and weather wear gear down; a broken backpack spills or loses capacity, torn clothing loses warmth.
- **Mending with what you find:** repair materials restore durability, each for some kinds of gear. A sewing kit mends the backpack and clothing; others (patches, cord, glue, wax) to be listed while building. Mending happens at stations, while packing.
- The starting kit from M12's unlocks becomes real gear here.

## Open questions

- The exact clothing slots, and whether two pieces can share a slot (shirt under jacket).
- Whether the map is held with one hand while walking (slower, can't brace) or only while standing.
