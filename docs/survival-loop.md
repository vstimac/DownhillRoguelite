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

## Waypoints and the estimate (M7)

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

M1–M5 are built (movement, balance, packing, vitals and ragdoll falls, terrain). From here:

| Milestone | Scope |
| --- | --- |
| **M6 Survival core** (built) | Game clock and difficulty; item categories; hunger and thirst; eating and drinking; endurance stamina; empty starting pack; supplies scattered along the route; picking up with E; death restarts the run |
| **M7 Day, night and stations** | Visible day/night cycle (sun, sky, darkness); stages ending at stations (shelters); waypoints with time, stamina and nightfall estimates; sleeping skips the night and heals; night cold; packing only at stations |
| **M8 Weather** | Fog, rain, snow, wind, heat per circle and time; effects on visibility, grip, balance and needs |
| **M9 Wildlife** | The approved wildlife plan (animals per circle, fleeing, scattering on falls), reusing M6's world spawner |
| **M10 Routes** | Branching paths from each shelter's lookout; route types (safe, hazardous, supply-rich) |
| **M11 Narrator** | The voiced companion: the 500 lines triggered by game events, then NobodyWho for live conversation |
| **M12 Progression** | Run upgrades, meta unlocks (starting kits, packs), contracts, depth scaling |

Open decisions for the director: same mountain or a new one after each death, whether a light source is in the first starting kit, and the exact day lengths after play-testing.
