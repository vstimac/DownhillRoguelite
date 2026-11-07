# Roguelite implementation status

Tracks the director's master plan (*The Descent — Master Narrative & Roguelite Implementation Plan*, 2026-11-07) against the actual code. Updated at the end of every phase. The code on the active branch is the source of truth; this file says what is really there.

## Phase 0 — Audit and baseline — 2026-11-07

- Status: DONE
- Active branch: `playtest-1` (based on 216d131). `main` is 34 commits behind and not used.
- Working tree at the start: dirty, not by this work. Another session is adding a character choice (`core/CarrierCharacter.cs`, `core/CarrierRoster.cs`, `player/CarrierModel.cs`, `player/RigFollower.cs`, `player/CarrierWornGear.cs`, `world_ui/StartScreen.cs`, `world_ui/OptionsPanel.cs`, plus `art/characters/`, `character.tscn`, `control.tscn`, `suited_character.glb`), and `core/GameOptions.cs`, `debug/DebugSceneControls.cs`, `inventory/world/ItemModels.cs`, `player/ChaseCamera.cs`, `player/Dive.cs` and `project.godot` have uncommitted edits. None of those were touched or committed by the roguelite work. The plan's `feat/roguelite-overhaul` branch was **not** created: switching branches over someone else's uncommitted work is unsafe, so the roguelite work is committed on `playtest-1` with only its own files.
- Baseline (`bash .claude/skills/smoke-test/smoke.sh`, with the other session's edits in the tree): Godot 4.7.2 .NET matches the SDK, `dotnet build` 0 warnings 0 errors, import clean, 300-frame headless run clean, autoloads and NobodyWho load, and all 20 test scenes pass (Asset 3, Balance 13, Conversation 12, Dive 4, Gear 28, Locomotion 36, Map 7, Narrator 4, Packing 13, Progression 7, Route 8, Run 5, Station 13, Survival 21, Terrain 15, Ui 13, Vitals 12, Weather 20, Wildlife 7). No pre-existing failures.

### What the checkout really has (before Phase 1)

| Area | State | Where |
| --- | --- | --- |
| Run loop: summit start, permadeath, new mountain per run, `-- --seed=N`, `-- --at-station=N` | Implemented | `core/RunState.cs`, `GameEvents.NewMountain` → `RespawnRequested` |
| Records across runs (runs, best depth, best station, total depth) | Implemented, unversioned `ConfigFile` | `core/RunRecords.cs`, `user://records.cfg`; headless runs never write it |
| Run summary in the air | Implemented (depth, stations, day, circle, cause, best) | `core/RunSummary.cs`, `world_ui/RunSummaryView.cs` |
| Score | Missing | — |
| Boons: ten, seeded per station, one per station | Implemented | `progression/BoonOfferings.cs`, `BoonStone.cs`, `BoonDefinition.cs`; defined as sub-resources in `debug/TestTerrain.tscn` |
| Trade-off/conditional boons, synergy resolver | Missing | — |
| Starting kit by best depth | Implemented | `progression/StartingKit.cs` |
| Meta unlocks, Witness Marks, save versioning | Missing | — |
| Physical inventory: 3D grid, COM, load → balance | Implemented | `inventory/` (`CarrierInventory`, `BackpackGrid`, `LoadCalculator`, `AutoPacker`), `GameEvents.LoadChanged` |
| Item instance identity | Missing: the inventory and events deal in `ItemDefinition` references and item ids; ordinary items of one kind share one cached resource | `inventory/CarrierInventory.cs` |
| Drop / throw / pick up again | Implemented; a dropped `WorldItem` keeps the `ItemDefinition` reference | `CarrierInventory.PutDown`, `inventory/world/WorldItem.cs` |
| Gear, pockets, wear, torn pack losing items | Implemented | `inventory/CarrierEquipment.cs` |
| Cargo category and `Fragile`/`Valuable` tags | Data only (ore sample is the one Cargo item); no cargo condition | `inventory/ItemCategory.cs`, `ItemTags.cs` |
| Contracts | Missing ("contracts wait for M14's gear" in `docs/survival-loop.md`) | — |
| Relics | Missing; narrator lines for `relic_found` exist but no trigger fires | — |
| Encounters off the route | Missing; routes (safe/hazardous/supply-rich) and supply caches exist | `terrain/TerrainRoute.cs`, `inventory/world/SupplySpawner.cs` |
| Station interactables | Bedroll (`map/SleepSpot`), boon stones, travellers (`narrative/StationVisitors`) | `IInteractable` + `IDescribed`, used through `player/PickupInteractor` (E) |
| Journal | Implemented as a session log of events (no lore sections) | `ui/JournalLog.cs`, journal page (J) |
| Narrator: 500 voiced lines, triggers, cooldowns | Implemented. A line needs a recorded `.wav`, so new CSV lines can't play until they are voiced; `NarratorTests` expects exactly 500 | `narrative/NarratorDirector.cs`, `narrative/data/metadata.csv` |
| Travellers at station fires, companion conversation (optional local model) | Implemented | `narrative/StationVisitors.cs`, `CompanionChat.cs` |
| Circles beyond nine | Implemented in the circle table and weather ("beyond") | `core/CircleTable.cs`, `core/circles.tres` |

### Plan for Phase 1 (decided from the audit)

- Reuse the boon pattern: a `ContractDirector` node in the main scene listens to `StationReached` and lays out an interactable board by the fire (like `BoonOfferings`); a delivery post at the destination station.
- Cargo identity: the inventory grants the cargo as its own copy of the item definition (`Resource.Duplicate()`), tagged with the contract's instance id. The copy keeps the item id, so icons, text and models still work; the reference is the identity, and it survives drop, throw and repacking because those keep the reference.
- Grant and hand-over go through `GameEvents` requests that the inventory answers, so the contract system never touches the inventory node.
- Narration: reuse existing voiced triggers (`cargo_picked_up` on accepting, `trade_completed` on delivery) instead of adding unvoiced lines.
