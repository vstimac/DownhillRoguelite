# Character stats and abilities

Each origin has its own gameplay profile, separate from clothing, carried weight and run boons.
Choose an origin in **F5 → Character → Confirm appearance → New Run**. Press **H** during play,
or click the parchment ability button above the hotbar. It shows readiness, active time and cooldown.

## Initial tuning

Values are percentages of the existing player settings, before needs, equipment and boons.
At the default 100 health/100 stamina settings, the first two columns also give maximum capacities.
These are fictional gameplay tradeoffs and starting values for playtesting.

| Origin | Health | Stamina | Speed | Recovery | Hunger/thirst drain | Ability |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| The Carrier | 100% | 100% | 100% | 100% | 100% | **Restore 40% stamina:** instantly restore 40% of effective maximum stamina; 75s cooldown. |
| Vučić | 110% | 90% | 105% | 90% | 110% | **Iron Resolve:** for 8s, stamina costs ×0.65 and physical impact damage ×0.80; 60s cooldown. |
| Plenki | 90% | 120% | 95% | 110% | 90% | **Measured Pace:** for 10s, stamina costs ×0.50, +35% cold resistance and speed ×0.90; 70s cooldown. |

Plenki's profile is prepared, but his model is missing. The existing roster keeps him unavailable;
there is no selectable, unimplemented actor. The Carrier is available when its local FBX is installed.
Vučić uses the existing GLB. Selecting a missing model falls back through the existing roster.

## Runtime rules and integration

- `RunState.Character` owns the selected origin, active time and cooldown for the run. Selecting
  an appearance for a future run does not change a live origin or reset its ability.
- Character factors are read by the existing health, stamina, needs, movement and impact paths.
  They are not repeatedly added to mutable boon or equipment values. Health and stamina
  capacity changes preserve their current fractions; normal respawn still refills them.
- Recovery, movement drain and fixed costs such as jumping use the same current profile.
  Character speed combines with existing weather, injuries and boon speed. Route estimates
  read these same current speed/stamina values; they do not predict future ability expiration.
- Needs drain applies while awake and asleep. Character cold resistance combines with boon
  resistance, capped at 90%, then existing warm clothing protection applies. Impact resistance
  multiplies the existing clothing protections in `PlayerRagdoll`; it does not reduce starvation,
  dehydration or other non-impact health damage.
- Cooldown starts on activation. Timers use gameplay seconds, freeze when paused or dead,
  and reset on a new run. Packing, conversations, downed state and death block activation.
  Rejected activation consumes nothing. Restore 40% stamina cannot exceed the effective stamina cap.
- The selected origin persists through the existing options file. Active effects and cooldowns
  are run state, not a new save format; clothing snapshots still save carried items/equipment only.
- Five-frame playtest capture now records live origin, saved selection, active duration and cooldown
  alongside the screenshots, so appearance and ability changes can be traced.

## Test it in Godot

1. Stop the current game with **F8**, then use **Build** to compile C# in the Godot .NET editor.
2. Open **`debug/TestCharacterAbilities.tscn`** and press **F6**.
3. Click **The Carrier**, then **Spend 70% stamina**, then press **H**. Stamina gains 40% of its
   current effective maximum, and the ability starts its 75-second cooldown.
4. Click **Vučić**. The live capacities become 110 health and 90 stamina. Use **H** and move/jump:
   Iron Resolve lasts 8 seconds and its countdown then shows the remaining 60-second cooldown.
5. Use **Take 25 damage** to inspect health. Switch origins to compare capacities and summaries.
   Workshop switches intentionally restart the profile's ability clock and refill health; normal
   appearance changes during a live run do neither.
6. Press **F5** for the full game. Choose **Character**, inspect the statistics, confirm and start
   **New Run**. The same ability button appears above the hotbar. **F9** toggles screenshot capture.

The workshop has ordinary editable nodes through its `World` instance of `LearnGodot.tscn`.
Its controls are development tools and do not change the production character-selection rules.

## Edit an origin yourself

Open `art/characters/carrier.tres`, `art/characters/vucic/vucic.tres` or
`art/characters/plenki/plenki.tres` in the Inspector. Expand **Stats** on the resource.
`Health = 1.1` means 110% of player base health; `ActiveStaminaCost = 0.65` means 65% of normal
cost while the ability is active. Keep percentages as fractions, then restart the test scene.
Set ability name, description, cooldown and duration together so the displayed text stays accurate.
An instant restore uses `RestoreStaminaFraction`; timed effects use the `Active…` fields.

Profiles validate finite positive multipliers and cooldown, non-negative duration, and resistance/
restoration fractions in 0–1. Invalid resources warn and use neutral values instead of corrupting
player statistics. A new character also needs an available rigged model and an entry in
`art/characters/roster.tres`; adding a stats resource alone does not validate a new rig or garment fit.

## Implementation files

| Area | Files |
| --- | --- |
| Profile resource and per-run clock (new) | `core/CharacterStats.cs`, `core/CharacterRun.cs` |
| Existing character catalog (extended) | `core/CarrierCharacter.cs`, the three character `.tres` resources above |
| Run lifecycle and ability requests (extended) | `core/RunState.cs` |
| Actor input/effects and HUD (new) | `player/CharacterAbility.cs`, `ui/hud/CharacterAbilityPanel.cs` |
| Player scene (extended) | `player/Player.tscn` |
| Existing gameplay paths (extended) | `player/CarrierHealth.cs`, `player/CarrierStamina.cs`, `player/CarrierNeeds.cs`, `player/PlayerController.cs`, `player/PlayerRagdoll.cs` |
| Live model selection (extended) | `player/CarrierModel.cs` |
| Creation page and run start (extended) | `world_ui/CharacterPage.cs`, `world_ui/StartScreen.cs` |
| Capture context (extended) | `debug/PlaytestCapture.cs` |
| Interactive workshop (new) | `debug/CharacterAbilityLab.cs`, `debug/TestCharacterAbilities.tscn` |
| Automated regressions (new) | `debug/tests/CharacterAbilityTests.cs`, `debug/tests/CharacterAbilityTests.tscn` |
| Documentation (new/extended) | This guide, `README.md`, `debug/README.md`, `docs/character-creation.md` |

New C# scripts include Godot-generated `.uid` files. No manager/autoload or external dependency was
added. Headless regressions default to the neutral Carrier profile regardless of a player's saved
selection or whether its optional FBX is installed. Explicit test selections still exercise other profiles.

## Validation and limits

Built with Godot **4.7.2 .NET** and the matching SDK, with zero C# warnings/errors. The repository's
required smoke script passed, including import, autoload/extension loading, a 300-frame main run
and all **43 test scenes/518 checks**. The ability suite contains 19 checks; the final focused rerun
and a 300-frame workshop smoke also passed after test-file isolation and a button-label clarification.
Rendered screenshots were inspected for the workshop, character page and full-game ability HUD.
Tests exercise live capacities, proportional switching, input activation, one-time cost application,
cooldowns, pause, expiry, saved-appearance isolation, boons, missing models, death and new runs.

These values still need human balance playtesting. Character-model deformation and prototype
garment fit are unchanged asset limitations described in [character creation](character-creation.md)
and [modular clothing](modular-clothing.md). This feature adds no face/body morphs or new model.
