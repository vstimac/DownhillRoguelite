# Survival loop and run structure

Decided 2026-11-06 from the director's brief. Builds on `docs/narrative.md` (the endless descent), `docs/inventory-design.md` (packing is balance) and `docs/balance-model.md`.

> Each day you descend one stage, scavenging food, water and supplies on the way, and reach the next shelter before night falls.

These are proven survival-genre mechanics (needs, scavenging, a day clock, shelter by nightfall), built on our own systems. We borrow mechanics, never another game's content.

## The day

- A run is a sequence of **stages**. Each stage is one day's descent and ends at a **shelter**, a station on the way down into the next ring.
- A 24-hour in-game clock runs from dawn. The real length of a day depends on difficulty (table below).
- **Spend the night at a shelter.** Sleeping there skips the night, saves the run state and starts the next stage at dawn.
- **Caught out at night:** darkness falls (visibility drops to a few metres without a light source), the cold drains stamina and then health, and bad weather gets worse. You *can* keep descending in the dark, but you mostly can't see the ground you're falling onto. Lights (lantern, torch, fire) are gear you can find later.
- Weather (fog, rain, snow, wind, heat) varies by circle and time of day and changes visibility, grip, balance and needs.

## Stamina: endurance

Added from the director's follow-up: stamina is the resource that decides whether you reach the station.

- **Walking drains stamina**, not just sprinting: more with a heavier load (+2% per kg), going uphill, and on steep descents; careful braking steps cost a little less. Sprinting drains fast on top.
- **It only recovers while standing still**, after a short pause. Resting costs time on the clock.
- **At zero you can't walk.** You stand until stamina is back to 30, which takes several seconds (twice as long when thirsty).
- Hunger lowers the maximum and thirst halves recovery, so food and water are part of the same budget.
- All of it uses one formula (`CarrierStamina.WalkDrainPerSecond`), which the waypoint estimates (M7) use too.

## Stations and nights (built in M7)

- A **station** stands on the route every 600 m of descent (tunable), on a gently tilted terrace with a fire, a lean-to, a bedroll and a light beacon visible from far away. Inside its shelter (9 m) you can pack (Tab) and, from 16:00 or at night, sleep (E on the bedroll).
- **Sleeping** skips to dawn of the next day: 40% of maximum health back, full stamina, and hunger and thirst drain at a third of the waking rate for the hours slept.
- **Night** (20:00–06:00, an hour of twilight each side): the light fades to faint moonlight and fog closes in. Outside a shelter the cold drains health and halves stamina recovery, scaled by darkness and the difficulty's night harshness.
- Debug: T skips an hour.

## Waypoints and the estimate (built in M7)

Waypoints toward the next station show, in the world, the distance, the estimated time including rests, the stamina it will take and whether you'd arrive before nightfall. That's what lets you decide between picking up the supplies over there and descending faster. The estimate uses the carrier's current load, needs and stamina, and the route's slope.

## Death

Death restarts the run from the beginning: back to the start, empty pack, full needs, dawn of day 1, and every supply back in the world. Same mountain for now; a new mountain each run is a one-line switch once the director decides.

## Needs

- **Hunger** and **thirst** run from 100 (fine) to 0 and drain over in-game hours. Thirst drains faster than hunger. Sprinting, heavy loads and heat drain faster; sleeping drains slower.
- **Thirsty** (below 25): stamina recovers at half speed. Controls: **F** eats, **Q** drinks, **E** picks up.
- **Hungry** (below 25): maximum stamina drops.
- **Empty** (at 0): health drains slowly. Health still doesn't regenerate during the day; sleeping in a shelter heals some.
- Eating or drinking uses one item from the pack: the food or water item that best fits the need. It is consumed and the pack gets lighter, which also changes balance.

## Items and the empty pack

Every item has a **category**:

| Category | Examples | Role |
| --- | --- | --- |
| Food | ration, dried fruit, food bag | Restores hunger when eaten |
| Water | flask, water skin, water bottle | Restores thirst when drunk |
| Supply | rope, med kit, stove, tent, cookware | Gear and tools (uses come with later systems) |
| Cargo | ore sample, relics, contract goods | Carried for value, trade and score |

- **A run starts with an empty pack** (meta progression can unlock starting kits later).
- **Supplies are scattered through the world** along the way down, denser near the walkable path and toward the next shelter. Deeper stages are leaner.
- Walk up to an item and press **E** to pick it up. It goes into the pack if there's room (the auto-packer finds a spot); if there isn't, it stays on the ground. Repacking happens at shelters.
- The tension the game is about: **food and water are heavy, packing them badly ruins your balance, and you need enough to reach the next shelter.**

## Difficulty

| | Easy | Normal | Hard |
| --- | --- | --- | --- |
| Real minutes per in-game day | 48 | 36 | 24 |
| Hunger / thirst drain | ×0.7 | ×1 | ×1.4 |
| Supplies in the world | ×1.4 | ×1 | ×0.7 |
| Night harshness (cold damage, darkness) | mild | full | full, plus no starting light |

Values live in `DifficultySettings` resources and are tuned by play-testing.

## Revised roadmap

## Weather (built in M8)

- **Circles** are 1,800 m of depth each (three stations; `core/circles.tres`). Limbo, Lust, Gluttony, Greed, Wrath, Heresy, Violence, Fraud, Treachery, then "beyond".
- Weather comes in **fronts** of 5 in-game hours that blend into each other over the last 1.5 hours. Each circle has a profile (`environment/weather/default_weather.tres`) weighting the fronts: Limbo mostly fog, Lust wind, Gluttony rain, Greed mixed, Wrath fog and rain, Heresy and Violence heat (Violence windy), Fraud fog, Treachery snow and wind, beyond everything at full strength. Near a circle's end its weather blends into the next circle's.
- The seed decides the fronts, so the same mountain has the same weather. **Night** makes fog, rain, snow and wind up to 40–60% worse and cools heat to a third.
- **Effects:**
  - **Visibility:** fog and the mist of rain or snow thicken the fog and dim the sun.
  - **Grip:** rain costs up to 35% grip on every surface, snow 45%. Snow also slows walking by up to 30%, and cairn estimates include that.
  - **Balance:** wind pushes body and pack with gusts. A bigger, higher pack catches more, so lean into it.
  - **Needs:** rain, snow and strong wind chill you even by day: they add to night cold and slow stamina recovery (estimates include that too). Heat doubles thirst and slows recovery a little. Shelter keeps the chill off.
- Debug: F4 cycles forced weather; `-- --weather=rain` forces a front for render checks.

## Routes (built in M10)

- Each stage (station to station) offers a choice. Paths split about 110 m below a station and rejoin about 110 m above the next.
  - **Safe** (the main path) is always there. It winds gently through the branch zone like a real trail, stays on firm ground (grass and snow) and is marked by cairns. Supplies are ordinary.
  - Every stage adds one or two branches that swing 45–70 m to either side (both, more often than not), and each side may fork once more into an **outer** supply-rich path 130–155 m out, the long way round: up to five paths a stage. The lookout sign lists them all ("far left", "left", "ahead", "right", "far right"):
    - **Hazardous:** a straight line, about 4 % shorter, but narrow and rough, on scree and snow (lower grip). Boulders crowd it. It holds 1.5× the supplies, plus caches of three items together.
    - **Supply-rich:** winds more and is about 10 % longer, with patches of boggy mud. It holds 4× the supplies.
- A **lookout sign** stands just below each station (and at the start) with one line per path: which way it goes, distance, time with rests, stamina and arrival before or after dark. The estimator follows whichever path you're on, so cairns on every path show that path's figures.
- About 60 m past the split, the path you're on counts as your choice (`GameEvents.RouteChosen`). The narrator comments on the options and on a safe or hazardous choice.
- Settings: `terrain/default_terrain.tres` (Routes group) and `inventory/world/default_supplies.tres`. Tests: `debug/tests/RouteTests.tscn`.

## Permadeath and new mountains (M12, first part built)

- **Every run starts on the summit,** the highest point of its mountain. Nothing near the start rises above it, and behind it a 60° cliff drops into the abyss: the only way is down. Falling into the abyss ends the run. Climbing behind the summit doesn't count as descent.
- **Death is permanent.** The run ends, and its summary is written in the air: depth, stations, day, circle, cause, and the best depth of any run. It goes into the records (`user://records.cfg`: runs, best depth, best station, total descent). After a few seconds a **new mountain** rises: a new random seed, new terrain, routes, stations, weather, supplies and wildlife. The carrier stands on its summit with an empty pack. R gives up the run the same way.
- A stone on the summit names the run and the mountain's seed. `-- --seed=N` replays a mountain.
- **Boons:** the first time you reach a station in a run, three glowing offerings stand by its fire, picked by the mountain's seed. Choose one with E; it lasts for the rest of the run, and the other two go dark. There are ten so far:
  - Steady Legs: balance
  - Iron Lungs: stamina recovery
  - Deep Reserves: maximum stamina
  - Light Step: walking cost
  - Strong Back: load cost
  - Lean Belly: hunger and thirst
  - Thick Blood: cold
  - Sure Footing: grip
  - Long Stride: speed
  - Deep Sleep: healing in sleep

  They live in `RunState.Modifiers`, and each system reads its own value.
- **Deeper is harder:** every circle down drains hunger and thirst 8 % faster and makes night cold and weather chill 10 % harsher (per difficulty, `core/difficulty/*.tres`). Supplies were already leaner with depth.
- **What death can't take: the starting kit.** Your best depth ever unlocks gear every new run starts with: a water flask at 300 m, a ration at 600 m, dried fruit at 1,200 m, a water skin at 1,800 m and a med kit at 3,000 m. The summit stone lists what you carry.
- Debug: `-- --at-station=N` starts at station N. Tests: `RunTests`, `ProgressionTests`.
- **Contracts** (roguelite Phase 1, Courier): the first time you reach a station, a job board by its fire may offer one job, picked by the mountain's seed: carry a real item (a 3.5 kg sealed parcel so far) down to the next station. E takes it if it fits in the pack; it is packed like anything else, so its weight and where you pack it change your balance. One job at a time. At the destination a delivery table takes it: E, then E again to hand it over. A delivery adds to the run's score (a point per 10 m, 20 per station, plus the job's pay), shown in the run summary. Cargo lost from a torn pack, or still carried when you die, fails the job. A new run has no jobs. Code: `progression/contracts/`; tests: `ContractTests`. Status of the wider roguelite plan: `docs/roguelite-implementation-status.md`.

M1–M5 are built (movement, balance, packing, vitals and ragdoll falls, terrain). From here:

| Milestone | Scope |
| --- | --- |
| **M6 Survival core** (built) | Game clock and difficulty; item categories; hunger and thirst; eating and drinking; endurance stamina; empty starting pack; supplies scattered along the route; picking up with E; death restarts the run |
| **M7 Day, night and stations** (built) | Visible day/night cycle (sun, sky, darkness); a station every 600 m of depth on a level terrace with fire, lean-to, bedroll and beacon; stone cairns every 120 m showing the estimate (distance, time with rests, stamina, arrival against nightfall); sleeping from 16:00 skips to dawn, heals 40% and refills stamina; night cold outside a shelter; packing only at stations |
| **M8 Weather** (built) | Fog, rain, snow, wind, heat per circle and time; effects on visibility, grip, balance and needs |
| **M9 Wildlife** (built) | Animals per circle (Quaternius farm animals and creatures, Kenney cube pets beyond the ninth), grazing, wandering, watching, fleeing, scattering on falls; population kept around the carrier; see wildlife/README.md |
| **M10 Routes** (built) | Branching paths from each shelter's lookout; route types (safe, hazardous, supply-rich) |
| **M11 Narrator** (voiced lines built for the demo; conversation built, branch `m11-conversation`) | The voiced companion: the 500 lines triggered by game events, then NobodyWho for live conversation (Enter: talk with the companion, a local model, context-aware replies; see narrative/README.md). Next: NPCs at camps |
| **M12 Progression** (built, contracts wait for M14) | Summit start and a new mountain every run, permadeath with records; boons at stations; depth scaling; starting-kit unlocks |
| **M13 Map and compass** (built) | A map held up in the world with a compass on top; waypoints that show as markers in the world; drawing with found paper and pencil. See docs/gear-and-map.md |
| **M14 Gear and durability** (built, branch `m14-gear`) | Backpack, pockets and clothing slots (all 3D grids, not lists); gear found in the world with durability and no rarity; mending with found materials (sewing kit and others). See docs/gear-and-map.md |

Open decisions for the director: same mountain or a new one after each death, whether a light source is in the first starting kit, and the exact day lengths after play-testing.
