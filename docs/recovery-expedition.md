# Recovery expedition prototype

First playable step of the exploration/cargo plan, on `fix/gameplay-math-audit`.

## Play

Open `debug/TestRecoveryExpedition.tscn` and press F6. The ordinary F5 main scene is preserved.
Choose New Run. The expedition card names the orange-flagged workshop and shows the distance.
Follow the trail downhill, detour to the workshop, face the pump and press E. It needs a free
3 × 4 × 3 block of pack cells and adds 6.5 kg. If it doesn't fit it stays at the workshop.
Carry it to Station 1. At the delivery table press E, then E again to confirm.
It must arrive at 60% condition or better; hard landings and falls use the existing contract
damage and packing exposure rules. Successful delivery pays up to 150 score, enables a three-flask
water reserve beside the station, and offers one boon choice. E at the reserve transfers one real
water flask into inventory. A full pack leaves the reserve stock unchanged.

Arrival alone still permits existing shelter/rest and station interactions. In this scene it
doesn't offer a free boon. Other successful contracts and relic deliveries also earn their
destination station's boon, at most once per station. Old scenes retain their existing rules
unless `BoonOfferings.RequireCompletedWork` is enabled.

## Integration

- `RecoveryExpedition` is a scene component, not an autoload. It registers a recovery offer at
  origin 0 through the existing `ContractDirector`, which remains the sole contract owner.
- `RecoveryWorkshop` uses the same cargo grant transaction as station boards. Delivery, unique
  ownership, dropping/recovery, packing, condition and run telemetry use existing systems.
- `StationWaterReserve` transfers ordinary items through an acknowledged `GameEvents.TryGrantItem`
  request. No space means no transfer and no stock loss. Flasks retain unique physical copy IDs.
- The site uses a separate seeded stream, has clearance from every route, and samples the
  approach's gradient. It doesn't change terrain, route or loot seeds. Physics obstacles such
  as individual trees/boulders still need in-game route inspection.
- The brief uses existing parchment/fonts, measured text wrapping and hides behind inventory,
  map, packing and pause menus. It leaves room for the character ability control.
- `GameEvents` is partial so the new request API doesn't mix into unrelated event changes.
- Reusing a removed/queued boon stone can no longer apply its modifier again.

## Validation

`debug/tests/RecoveryExpeditionTests.tscn` covers 32 reproducible mountain placements and the actual
world's recovery, load mass, no-room behavior, duplicate recovery/delivery prevention, condition
damage, wrong destination, service/boon rewards, finite resupply, independent item ownership,
new-run reset and failed cargo. Native graphics captures are also required for presentation;
headless tests do not establish appearance or mobile performance.

## Limits and next work

This is one authored expedition, not the complete new roguelite loop. The workshop and pump use
clearly identified prototype geometry. The mission and service reset with the run, like existing
contracts; there is no new mid-run contract save feature. Water is granted as filled inventory
flasks; reusable empty containers/refilling are a later step. The old station/depth score and
depth-based starting-kit unlocks are unchanged pending comparative playtests. The existing guide
can still describe old station progression; the expedition card is the prototype's instruction.

Remaining plan: useful tool actions, additional exploration outcomes, route/cargo balancing,
unified L-frame and straps, clothing coverage/ordinary walking, physical flight and landing QTE,
inventory packing integration, and configurable camera framing. See `mobile-terrain-plan.md`
for the Galaxy Tab A9 environment direction.

## Optional chatbot

`CompanionChat.EnableLanguageModel` and `Hud.EnableCompanionChat` now default off. Core play does
not search for/load the external GGUF or expose the companion Enter shortcut. Scripted traveller
dialogue and recorded narration remain. Explicit `--llm-live` is a development opt-in.
The local model outside the repository is preserved. The development GDExtension is still installed;
removing its runtime libraries and the launcher's legacy model offer from release packaging must
be checked before the next authorized export. No export size reduction is claimed by this change.
