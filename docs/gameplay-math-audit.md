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

## Reproduce the added checks

```powershell
$godot = 'D:\Game dev\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build
& $godot --headless --path . --fixed-fps 60 res://debug/tests/GameplayMathTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/DescentPlaytestTests.tscn
```

For the rendered descent, omit `--headless`, pass `--audio-driver Dummy`, and append
`-- --skip-start "--playtest-screenshot=D:/Game dev/math-audit-artifacts/descent.png"`.
