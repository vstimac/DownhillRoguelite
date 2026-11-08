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

## Phase 1 — Physical cargo contracts (Courier) — 2026-11-07

- Status: DONE (vertical slice: one Courier job, offer → accept → carry → deliver, with failure and reset paths)
- Active branch and commit: `playtest-1`, the commit after ffee45c titled "Cargo jobs: carry a sealed parcel down to the next station". Not pushed.
- Added: `core/ContractStatus.cs`, `core/ContractNote.cs`, `core/RunTelemetry.cs`, `progression/contracts/` (`ContractDefinition`, `ContractInstance`, `ContractKind`, `ContractResult`, `ContractResolver`, `ContractDirector`, `ContractBoard`, `DeliveryPost`, `resources/courier_sealed_parcel.tres`), `inventory/items/sealed_parcel.tres`, `debug/tests/ContractTests.tscn` + `.cs`.
- Edited: `core/GameEvents.cs` (cargo grant / hand-over requests and answers, `CarriedCargo`, `ContractOffered/Accepted/Resolved`, `ActiveContract`), `core/RunState.cs` (`Telemetry`, `ScoreOf`, score tuning exports), `core/RunSummary.cs` (deliveries, failures, cargo score, score; optional fields so old callers still compile), `inventory/ItemDefinition.cs` (`CargoInstanceId`, runtime only), `inventory/CarrierInventory.cs` (`GrantCargo`, `HandOverCargo`, `OwnsCargo`, publishes `CarriedCargo`), `debug/TestTerrain.tscn` (a `Contracts` node), `ui/JournalLog.cs`, `world_ui/RunSummaryView.cs`, `narrative/NarratorDirector.cs`, `docs/survival-loop.md`, `progression/README.md`.

### What the player can do now

1. Reach station 1 (or start there: `godot --path . -- --at-station=1`). Left of the fire stands a job board: "Job: A Parcel for the Next Fire · Sealed parcel · 3.5 kg · to Station 2 · pays 30". Looking at it shows the note and the parcel's size; E takes the job.
2. If the pack is full the board says so and nothing changes. Otherwise the parcel is packed (the auto-packer's spot, low and central), the load's weight and centre of mass change at once, a note and a journal line confirm it, and the companion may say a `cargo_picked_up` line. It can be repacked at a station like anything else: packed higher, the centre of mass rises (tested: 0.99 m → 1.31 m).
3. Drop or throw it and pick it up again: it's the same parcel. Away from the pack it can't be delivered.
4. At station 2 a delivery table with a lantern waits (left of the fire, a little uphill). E asks; E again within 4 s hands it over. The parcel leaves the pack, the job is credited once (+30), the companion may say a `trade_completed` line, the journal records it, and station 2's board offers the next job.
5. On death the run summary shows deliveries and a score (a point per 10 m, 20 per station, plus cargo pay). A job still carried fails ("never arrived"); the new mountain has no offers, no job and no parcel.

### Decisions recorded

- **Cargo identity:** the inventory packs a `Duplicate()` of the item definition per contract, tagged with the contract's instance id (`ItemDefinition.CargoInstanceId`). The copy keeps `ItemId`, so icons, models and text work unchanged; ordinary items still share their cached resource. Drop, throw, repack and pockets all keep the reference, so identity survives them. `CarriedItems` (ids) is unchanged; contract code reads the new `CarriedCargo` (instance ids).
- **No cross-system references:** the contract system never touches the inventory node. It sends `RequestCargoGrant` / `RequestCargoHandOver` and reads the synchronous `CargoGranted` / `CargoHandedOver` answers. The contract is accepted only after the inventory confirms the parcel is packed, and delivered only after it confirms removal.
- **Credit once:** `ContractResolver` never changes a final status; `RunTelemetry` counts each instance id once even if `ContractResolved` is published again (tested).
- **Offers:** every first-reached station offers one job (`OfferChance` 1, INITIAL TUNING), from the contract's own seeded stream (`SplitMix64.Mix(seed ^ salt, station)`), so contracts never shift terrain, boons or supplies. One open job at a time; other boards refuse with "You're already carrying a job".
- **Cargo:** a new 3.5 kg sealed parcel (3×3×2 cells, Cargo, Valuable, Kenney survival-kit box model) instead of the plan's 0.15 kg letter, which wouldn't move the centre of mass enough to feel. The letter, stone, glass, casket and map-tube jobs (CT_001–CT_005) come with Phase 2's condition and deadlines.
- **Narration:** no new CSV lines. A narrator line needs a recorded voice file and `NarratorTests` expects 500 lines, so Phase 1 reuses voiced triggers that fit (`cargo_picked_up`, `trade_completed`; `cargo_lost` already fires for torn-pack losses). The plan's new contract lines wait for voicing (Phase 7).
- **Score:** in-run only for now (`RunState.ScoreOf`, `RunSummary.Score`). Nothing new is written to `records.cfg`; that waits for Phase 6's versioned save.

### Tests

- New: `debug/tests/ContractTests.tscn`, 12 checks: definitions validated (broken ones refused); same mountain and station give the same job (checked with a second test contract over 12 stations); station 1 pins one job for station 2 on the ground by the fire; a full pack (36 ore samples) can't take it and nothing changes; taking it packs exactly one own copy, load 1.5 → 5.0 kg and the centre of mass moves; packed at the top the centre of mass rises 0.99 → 1.31 m; taking it twice, a second grant and delivery at the wrong station do nothing; drop → re-pickup keeps the same parcel, and delivery while it's on the ground is refused; at station 2 the first E only arms, the second removes that parcel and credits +30 once (repeated delivery and a repeated `ContractResolved` are ignored); station 2 then offers a job to station 3; cargo lost from a torn pack fails its job with no credit; dying with a job fails it, the summary counts 1 delivery and the score, and the new run has no offers, boards, post, job, cargo or counters.
- `bash .claude/skills/smoke-test/smoke.sh`: RESULT PASS. Build 0 warnings 0 errors, import, 300-frame headless run clean, all 21 test scenes pass, including BalanceTests 13/13 (the A-vs-B check is unchanged) and the other session's new CharacterTests 15/15. After a last small change (the HUD tooltip refreshes when the board or post changes state), the build, ContractTests, UiTests and the headless run were run again: all clean.
- Not done: no graphical render check (the director asked for few renders). The board and delivery table are primitives; their placement on the ground is checked by the test, not by eye.

### Known issues and risks

- Every first-reached station offers a job (`OfferChance` 1). Fine for the slice; Phase 2 should tune it.
- The tooltip and board show the job's pay as "score"; there is no score display during the run, only in the death summary.
- A job's delivery table is placed when it's taken, using the terrain height, and settled onto the physics ground when the destination is reached. If a station's terrace differs from the terrain height, it could float until then (the test found 0.00 m at station 2).
- The dropped parcel is an ordinary `WorldItem`: it stays where it was dropped for the run, and it is cleared with the scene on a new run like any dropped item.
- Score values (0.1 per metre, 20 per station, 30 per parcel) are INITIAL TUNING and untested in play.

### Next smallest playable step (Phase 2)

1. Cargo condition (0–100) on contract cargo only, damaged by hard landings and falls through the existing `Landed` / `DownedChanged` events, scaled by where it's packed; visible on the board/journal; a Preservation job (CT_003 The Glass Truth, ≥ 70) that fails below its threshold.
2. The other first contracts as resources (CT_001 letter, CT_002 stone as a Burden job, CT_004 casket, CT_005 map tube over two stations), with eligibility by station and circle.
3. Abandon at a board (E on the board of the active job), and a deadline in days for multi-station jobs.
4. A HUD line for the active job (the descent note), and the companion's conversation context mentioning it.
5. Tests: intact / damaged / broken deliveries, abandon, deadline, two seeds giving repeatable offers.

- Push/merge/release: NOT PERFORMED

## Phase 2 — Contract reliability and variants — 2026-11-08

- Status: DONE. Commit `de88c59` "Five new jobs, and cargo that can break" on `playtest-1`, not pushed.
- Added:
  - `inventory/items/` sealed_letter, carved_stone, glass_reliquary (upright), sacred_casket, map_tube.
  - `progression/contracts/resources/ct_001`–`ct_005`.
  - `debug/tests/ContractVariantTests.tscn` + `.cs`.
- Edited:
  - `ContractKind` (Preservation, Burden, Pilgrim).
  - `ContractDefinition` (Fragility, MinCondition, DeadlineDays, MinCircle/MaxCircle, circle-aware `EligibleAt`).
  - `ContractInstance` (Condition, DeadlineHours).
  - `ContractResolver` (`Accept` starts the deadline; `Deliver` pays in full at 90%+, in proportion below, and fails below MinCondition; `Damage`, `PayFor`, `IsLate`).
  - `ContractDirector` (knocks from `Landed` / `DownedChanged`, scaled by packing; break means fail; deadline check; `Abandon`; `CircleOf`).
  - `ContractBoard` (abandon on E then E at any board; terms show the condition required and the deadline).
  - `DeliveryPost` (condition, too-damaged warning).
  - `ContractNote` (Condition, MinCondition, DeadlineHours, Breakable; defaulted, so old callers compile).
  - `GameEvents` (`ContractUpdated`, `CargoExposure`).
  - `CarrierInventory` (publishes each cargo's exposure: 0 when centred with 2+ cells of padding on each axis, 1 when touching a face or in a pocket).
  - `DescentPanel` (job line), `CompanionChat.WithJob` (situation note), `JournalLog` (condition on delivery, abandoned text), `debug/TestTerrain.tscn` (all six contracts).

### Decisions recorded

- **Condition** lives on the contract instance, not the item, so ordinary items stay unchanged.
- **Knock size:**
  - A knock is `(landing speed − 6 m/s) × 5`, or 12 for a ragdoll fall.
  - It is multiplied by the definition's Fragility (0 for the stone, 2 for glass).
  - It is then multiplied by Lerp(0.4, 1.4, exposure): packed deep, the cargo takes 0.4 of the knock; against the outside, 1.4. All of these are INITIAL TUNING exports on `ContractDirector`.
- **Exposure is per axis,** relative to the best placement the pack allows. A centred item always scores 0 even when the pack is shallow. The auto-packer's low spot touches the bottom, so it is fully exposed. Fragile jobs therefore say "pack it deep inside (I)" when taken.
- **Abandon at any board, not just the original one.** Carriers can't walk uphill, so a job that could only be given up at its own board would trap them. The cargo is removed from the pack and left there. Telemetry counts it as failed (no new counter).
- **Deadlines use `WorldClock.TotalHours`,** checked once a second; sleeping counts.
- **Pilgrim has no extra penalty on failure** (as the plan says). There are no Favor tokens or caches yet: rewards are score only.

### Tests

- New `ContractVariantTests`, 9 checks:
  - The five jobs are valid and cover all four kinds.
  - Two seeds give repeatable, different offers, and station 1 offers only early jobs.
  - Intact delivery pays +30, damaged pays in proportion (+18 at 60%), too damaged fails, the stone can't break, and the deadline triggers between 71 h and 73 h.
  - Packed deep, a knock of 10 takes 8 condition; at the edge it takes 28.
  - A soft landing does nothing, while a hard landing and a fall each lower the condition, and breaking fails the job.
  - A too-damaged hand-over takes the cargo for nothing, and a parcel at 72% pays +22.
  - Abandoning at another station's board takes two presses of E, leaves the cargo and frees you for the next job.
  - The deadline fails the job after `AdvanceHours`.
  - The HUD and companion texts mention the job.
- `ContractTests` 12/12 (one check now reads "the board doesn't offer to take", since a taken job's board offers to give it up).
- `bash .claude/skills/smoke-test/smoke.sh`: RESULT PASS. All suites, headless run clean (this time including the other session's new title screen and options work).

### Known issues

- Tuning is unplayed, and glass is meant to be hard.
- Weather doesn't damage cargo yet (the plan mentions it for Preservation).
- The new cargo models are stand-ins from the survival and castle kits (a box for the letter, a log for the map tube).
- No new narrator lines (they need voicing).

### Next smallest playable step (Phase 3)

Relics as real items placed on side paths:
- One heavy relic and one fragile one, at seed-consistent spots (the lore placer's side-path positions are a ready hook).
- Discover, carry, drop and recover them; deliver them at a station.
- Persistent "seen" journal entries (the `LoreBook` pattern).
- `RelicTests`.

## World, map and routes (the director's requests) — 2026-11-08

- Status: DONE, four commits on `playtest-1`, not pushed: `9be4133` (map), `a6d850c` (routes), `25ced42` (found texts and path markers), `d51c91c` (set pieces and new art).
- **Map:** explored ground is painted once into tile textures (`map/ChartTiles.cs`, 32 cells per tile) instead of being drawn cell by cell. While the map is open the sheet stays still (it is anchored and only re-centres near the edge), and it redraws only when something changes (`MapChart.Version`, a move of 2+ px, a turn of 4°+). `MapTests` 9/9.
- **Routes:** besides the inner hazardous and supply branches, a stage may get an outer supply-rich path on either side (`OuterBranchChance` 0.45, offset 130–155 m). That gives up to five paths, with 15 of 30 stages having four or more (tested). The lookout sign lists up to five, signed "far left" / "far right". `RouteTests` 9/9.
- **Found texts (start of Phase 3's lore):**
  - 24 texts in `narrative/lore/*.tres` (`LoreEntry`): the plan's FRAG_001–010, plus 14 new ones, 9 of them tied to a circle.
  - `LorePlacer` stands at most one per stage, chosen by seed: always FRAG_001 in the first stage, about two thirds of later stages, and usually halfway along a side path. FRAG_006/007 share an exclusive group, so only one appears per run.
  - `LoreMark` shows the heading from afar and the words up close. E copies it into the journal (`GameEvents.LoreRead`).
  - `LoreBook` (`RunState.Lore`) remembers what was read across runs in a new `user://lore.cfg`; `records.cfg` is untouched. Unread texts are favoured.
- **Path markers:** `map/PathMarker.cs`, a signpost a little way down every path of a split. It shows the path's kind, its measured length and drop to where it rejoins, and a one-line note.
- **Set pieces:**
  - `environment/SetPiece*.cs` and 14 pieces in `environment/set_pieces/*.tres`: 11 wayside (ruins, wagon, banners, shrine, stall, iron fence and graves, siege wreck, fallen wall, watchpost, fence and lantern, dry fountain) and 3 landmarks (windmill, stone tower, broken tower).
  - `SetPiecePlacer` places 2–4 wayside pieces per stage, at least 6.5 m from every path and 30 m from stations, and a landmark 45–85 m off the path in about half the stages. Placement is by seed and favours each piece's circles. Solid parts get box colliders.
  - Stations also got a stall, a cart, a banner, a fence and a lamp.
- **New CC0 art** (downloaded with the director's go-ahead, credited in `art/CREDITS.md`): Kenney Fantasy Town Kit 2.0 and Castle Kit (full GLB sets), and 15 models of Quaternius's Medieval Village MegaKit with their textures cut to 512 px (3.5 MB instead of about 50 MB).
- **Tests:** new `LoreTests` 13/13 and `SetPieceTests` 6/6. Full smoke on the commit before the set pieces: RESULT PASS (22 suites). The full smoke after the set pieces shows FAILs only from another session's uncommitted start-screen and options work (`UiTests` layout and start-screen checks; `keyboard_get_keycode_from_physical` errors from the untracked `ui/menu/InputGlyphs.cs`). Every other suite passes.
- **Not done:** no render check of the set pieces (few renders asked for). Model heights were chosen from measured bounds, not by eye. Lore texts and landmarks are not shown on the map yet. No narrator line for reading a text (new lines need voicing).

## Continuation report — 2026-11-08

| Phase | State |
| --- | --- |
| 0 Audit and baseline | DONE |
| 1 Courier contract slice | DONE (one job type, tested end to end; no render check) |
| 2 Contract reliability and variants | DONE (condition, five jobs, abandon, deadlines, HUD and companion; tested; tuning unplayed) |
| 3 Relics and lore | PARTIAL (24 found texts, persistent lore book, journal; relics not started) |
| 4 Trade-off boons | NOT STARTED |
| 5 Encounters | PARTIAL (witness cairns and found texts on side paths; set pieces dress the paths; no choice encounters yet) |
| 6 Persistent journal, Witness Marks, sidegrades | NOT STARTED (no save changes yet; `records.cfg` untouched) |
| 7 Narrative integration | NOT STARTED (new lines need voicing) |
| 8 Circle content, beyond nine | NOT STARTED |
| 9 Polish and beta | NOT STARTED |
