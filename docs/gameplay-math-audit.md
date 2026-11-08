# Gameplay math audit — 2026-11-08

Branch: `fix/gameplay-math-audit`, based on `playtest-1` at `f8ad235`.
The existing uncommitted objective, wayside, NPC and UI work was preserved.

## Findings and fixes

| Area | Reproduced problem | Correction |
| --- | --- | --- |
| Load centre of mass | A zero-mass empty backpack divided zero by zero and published NaNs. | Publish a finite zero centre when mass is zero. Positive-mass weighting is unchanged. |
| Packing | Zero and negative box dimensions could pass both corner bounds checks without occupying cells. | Reject every nonpositive dimension before checking occupancy. |
| Stamina refill | Refilling used base capacity: a 150-capacity carrier received 100; a hungry 60-capacity carrier also received 100. | Refill to effective capacity and clear movement/recovery timers. |
| Vitals | HUD and other readers received base capacity instead of the hunger/boon-adjusted maximum. | Publish effective maximum. |
| Recovery timing | A frame crossing the recovery delay received the entire frame's recovery; movement memory stopped aging during the delay. | Recover only for the time after the delay, and age movement memory every tick. |
| Exhaustion | Capacity below the recovery threshold made exhaustion impossible to clear. | Cap the recovery threshold at effective capacity; estimates use the same threshold. |
| Slope ETA | Distance was measured along the route but travel seconds used its horizontal projection. | Divide ground distance by ground speed, matching `FloorConstantSpeed`. |
| Rest ETA | The last stop could restore less than the exhaustion threshold; an initial rest was omitted from the rest count. | Count each stop and charge its complete threshold recovery and delay. |
| Route stamina | Summing all effort ignored recovery on flat/downhill stretches before later climbs. | Track stamina and recovery delay in route order. Reject unwalkable/zero-recovery estimates when needed, and invalid sampling steps. |
| Body animation | Semi-implicit spring integration diverged during sustained 100 ms frames. | Integrate the damped spring analytically, including underdamped, critical and overdamped cases. Existing frequency and damping tuning remains in use. |
| Sleeping | Sleep ignored needs boons and depth scaling, and could refill using stale hunger capacity. | Apply difficulty, depth and needs modifiers during sleep; refresh stamina modifiers before refilling. |

`GameplayMathTests` first reproduced **14 failing cases** on the original runtime code.
It now passes all **23 checks**, including frame-partition invariance in four damping regimes,
undamped energy conservation, low stamina capacity and invalid route parameters.

## Initial validation (`b541fe7`)

- Godot 4.7.2 .NET, .NET SDK 10.0.401, Windows.
- Build: zero warnings, zero errors.
- All **30 test scenes passed**, with **363 PASS checks** and no FAIL/error lines.
- Core high-pack versus low-pack balance and packing scenarios still pass.
- Station travel prediction: 58 seconds estimated versus 55 seconds measured, reported error 4%.
- Main-scene automated descent: 131.9 metres downhill in 30 simulated seconds, health 100, finite movement and body animation.
- Graphical Forward+ run on the RTX 3060: 128.8 metres downhill in 30 simulated seconds, health 100; screenshot saved and inspected.
- Godot resource import and 300-frame verbose main-scene smoke passed without warnings/errors; all autoloads and the .NET runtime loaded.
- Relic and wayside tests intentionally print warnings while rejecting invalid/duplicate test resources. These also occurred before the changes.

Local logs, numeric-operation index, test summary and rendered screenshot:
`D:\Game dev\math-audit-artifacts\`.

## Coverage and remaining observation

The review indexed numeric operations across first-party gameplay, UI, audio and world scripts,
then traced movement, balance, packing, survival, weather, route generation and progression
against their existing scenarios. This is a targeted math audit and runtime validation, not a
formal verification of every script or every possible mountain. Third-party addons were not edited.
The documented point-mass load inertia approximation and the calibrated balance model were retained.

One baseline wildlife run failed the grounding check: the worst animal was 70 cm above the
ground ray's result. Its random population seed was not captured. The follow-up below adds
replayable population seeds and distinguishes missing support from valid capsule clearance.

Route estimates still assume current equipment/weather/health, average walking speed and deliberate
rests at exhaustion. They do not predict future weather, collision detours, falls or player choices.

## Wildlife follow-up

Controlled physics tests confirmed that idle animals skipped `MoveAndSlide`, retaining stale
`IsOnFloor` and `HasGround` flags. Removing a ledge left a grazing animal suspended one metre
above the floor with zero velocity. Displacing an idle animal upward also left it hanging.
The first regression run failed three checks before the fix. Grazing animals now refresh their
physics contacts every tick, allowing gravity to act when support disappears.

The investigation also reproduced a false grounding failure with population seed 8: the central
vertical ray was 15 cm below a sheep's origin, but its capsule was only **2.4 mm** from the
actual supporting surface. On a plane, a capsule's vertical clearance is `r * (1 / cos(slope) - 1)`;
on an obstacle edge the contact can be on a different face from the centre ray's hit. The test
now requires live support and measures `abs(dot(bottomSphereCentre - contactPoint, normal) - r)`.
The tolerance remains 8 cm; missing support fails explicitly. A separate 45-degree plane test
checks both the analytical vertical clearance and the actual sphere-to-plane contact.

Follow-up validation:

- Build: zero warnings/errors.
- All **7** controlled wildlife physics checks passed.
- Population seeds **1–16** all passed their **112** checks, including the formerly failing seed 8.
- Reported smoothed wildlife cost ranged from **0.249 to 0.546 ms**, within the existing 1 ms budget.
- The earlier **23** math checks still passed.
- Main-scene descent passed again: **124.3 m** downhill in 30 simulated seconds, health 100.
- Population RNG seeds are printed and can be supplied using `-- --wildlife-seed=8`.

Logs and seed results are in `D:\Game dev\math-audit-artifacts\wildlife-*`.
The exact initial unseeded 70 cm observation cannot be uniquely attributed after the fact;
the controlled stale-contact defect and the seeded false-positive case have both been addressed.

## Impact and disturbance follow-up

The previous ragdoll formula used the magnitude of the entire velocity change as impact speed.
That counted tangential braking as a blow, added outward rebound speed to the incoming hit,
and could count acceleration toward a surface as an impact. `ImpactMath.SpeedLost` now uses
`u = max(0, -dot(velocity, unitNormal))` and `max(0, uBefore - uAfter)`. The existing safe speed,
hardness, body-part weighting and damage tuning remain in use. The velocity-change magnitude
is retained only as a cheap candidate filter before probing for a surface.

Balance also failed to attribute landing impulses and wind torque as external pushes. That
allowed automatic standing steadying to suppress their falls. Zero impulses could disable
steadying, while resetting balance left a cancelled push's memory behind. Landings and wind
now refresh push memory, zero impulses leave it alone, and a fresh/reset carrier has no
remembered push. No stiffness, torque multiplier or normal gameplay threshold was retuned.

The first analytical regression run reproduced **11 failures** before these fixes. The new
scenarios cover head-on and glancing impacts, sliding, rebound, rotated normals, impulse
attribution, reset, zero impulses, long push memory and a brief gust after the wind has stopped.
The balance scenarios use sensitive test-only stumble thresholds to isolate push attribution.

Validation after the impact changes:

- Build: zero warnings/errors.
- All **33 test scenes** passed, with **390 PASS checks** and no FAIL/error lines.
- The new impact and disturbance suites account for **20** checks, including the brief gust.
- A real 2 m rock drop still causes no damage. A 6 m drop causes **23.7** damage on rock versus
  **9.6** on grass, retaining the existing surface-hardness criteria without changing thresholds.
- The A-versus-B descent still ends Fallen for the high/lopsided load and Stable for the low load.
- Main-scene descent: **129.2 m** downhill in 30 simulated seconds, health 100, finite motion.
- Full suite results and logs: `D:\Game dev\math-audit-artifacts\impact-full-suite.csv` and `impact-suite-*.log`.

The existing ray remains a contact-detection heuristic; this change does not replace it with
engine contact impulses. Reported damage still uses bone linear velocity rather than full
angular contact-point velocity.

## Carried mass follow-up

The stamina, jump, needs and route calculations previously used only `CurrentLoad.Mass`, which
is the packed backpack. Wearing clothing or storing something in a pocket therefore removed its
weight from effort calculations. Six conservation/effort checks reproduced this problem.

`CarriedLoad.Mass` now adds backpack mass to `GearSnapshot.WornMass` and `PocketMass`. Broken
clothing retains its mass. Movement effort, jumping, load-dependent turning, needs, route
estimates and the character sheet use this total. The backpack's balance, inertia and packing
readout continue to use its own packed mass and measured centre of mass. Pocket grids and
individual body-space positions remain a separate design question; no artificial COM was added.

All **12** carried-mass checks now pass, including dressing, taking off clothing, pocket food,
consumption, pocket-to-pack transfers and a new run. The first focused regression pass covered
**11 scenes and 163 checks**, including the existing balance, packing, jump, survival and UI tests.
The complete follow-up passed **34 scenes and 402 checks**, followed by a clean import and
300-frame main-scene smoke test.

## Pocket actions follow-up

Nine new failures showed that pocket items were not handled consistently: eat/drink requests
searched only the backpack, hotbar use could delete a pocketed material, tool-use requests
ignored pocket tools, and hand-over could not remove pocket cargo. Mending also ignored pocket
materials and reported the kit's full restoration even when the gear reached its cap sooner.

Food selection now compares backpack and pocket supplies using the same deficit rule. Explicit
tool use, mending and cargo hand-over consume the actual pocket item, while hotbar use preserves
nonconsumables. Cargo removal uses the copy's definition identity, so two parcels of one kind
cannot be confused. Mending publishes the amount actually restored. All **10** pocket-use
checks pass, including exact cargo identity and prevention of a second hand-over.
The complete suite then passed **36 scenes and 413 checks** after resource import.

One focused contract-variant run encountered a native Godot `gchandle.is_released()` fatal error
during a resource finalizer, followed by invalid resource handles. The captured log is
`pocket-final-ContractVariantTests.log`. An isolated repeat passed all 9 contract checks, and
`ContractResourceTests` passed validation across 60 forced collections. Those initial checks
did not reproduce the native crash; the follow-up below adds a stronger reproduction.

## Per-copy condition and ownership

Fourteen failing scenarios reproduced free repairs and item duplication during container
transitions. `StoredItem` now retains each loose/pocket copy's durability. Pack/unpack, repack,
drop/throw/pickup, dressing and pocket spill preserve wear, including zero durability. Repack
normalizes an order against owned backpack/loose copies, ignores excess or foreign entries,
and keeps omitted copies. Pockets stay in place. Stale packed references cannot be unpacked
or consumed twice. Clearing supplies clears pockets too.

`GearConditionTests` now passes all **14** checks, including different wear on two copies of
the same shared definition. The public definition-list views remain live without allocating
for Count/index access; instance wear never changes the shared item resource.

## Contract resource lifetime

The native finalizer failure recurred in a second focused run. A stronger diagnostic calls
`GC.Collect()` immediately before revalidating the full cargo catalog, allowing finalizers
and reloads to overlap. It reproduced the same fatal `gchandle.is_released()` error in
`contract-full-gc-race-before.log`. Waiting for finalizers before reloading had hidden it.

`ContractDefinition` now retains its typed `ItemDefinition` while its cargo path is unchanged;
validation and offer creation reuse it. Changing the path invalidates that reference. The
same full-scene diagnostic then passed **10 consecutive runs** (60 collections each), with
all 9 gameplay checks passing every time. This mitigates the observed cargo lifetime race;
it does not claim to repair Godot's native binding layer. Similar engine crash signatures
are reported in [Godot issue 83762](https://github.com/godotengine/godot/issues/83762), which
alone does not establish that our trigger has the same underlying cause.

Run the stress variant with `ContractVariantTests.tscn -- --stress-resource-gc`.
The complete follow-up passed **37 scenes and 427 checks**, with zero error lines and all
processes exiting successfully (`condition-full-suite.csv`). C# builds without warnings;
resource import and the 300-frame main-scene smoke check also pass.

## Delivery deadlines and score arithmetic

The economy regression reproduced **11 failures** across late delivery, nonterminal telemetry
notes and large rewards. Delivery previews now reject expired jobs immediately; committing
delivery checks the clock before removing cargo and resolves an expired job only once. Exact
deadline equality remains valid. The delivery post explains when the deadline has passed.

Run telemetry only records identified, final contract outcomes, and only identified Accepted
notes count as acceptances. Early/nonterminal notes do not consume the final-outcome dedupe key.
Cargo and relic rewards accumulate with wide intermediates and saturate at `int.MaxValue`.
The combined depth/station/cargo score also saturates, while damaged contract pay uses double
precision before rounding to avoid losing integer reward precision. Ordinary rewards and score
weights are unchanged. `EconomyTests` passes all **14** checks, including real cargo ownership
at the deadline, repeated delivery, malformed notes, duplicate rewards and large valid values.
The full regression passed **38 scenes and 441 checks**, all exiting successfully with no
FAIL/error lines. Resource import and the 300-frame main-scene smoke check also passed.

## Reproduce the added checks

```powershell
$godot = 'D:\Game dev\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build
& $godot --headless --path . --fixed-fps 60 res://debug/tests/GameplayMathTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/DescentPlaytestTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/WildlifePhysicsTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/ImpactMathTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/BalanceDisturbanceTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/CarriedMassTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/PocketUseTests.tscn
```

For the rendered descent, omit `--headless`, pass `--audio-driver Dummy`, and append
`-- --skip-start "--playtest-screenshot=D:/Game dev/math-audit-artifacts/descent.png"`.
