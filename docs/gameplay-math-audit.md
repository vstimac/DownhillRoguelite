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

## Validation

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
ground ray's result. The diagnostic run, five focused repeats and the complete final suite passed
without a wildlife runtime change. The cause remains unconfirmed; collision-support diagnostics
were added to the failing assertion to make a recurrence actionable. The assertion was not relaxed.

Route estimates still assume current equipment/weather/health, average walking speed and deliberate
rests at exhaustion. They do not predict future weather, collision detours, falls or player choices.

## Reproduce the added checks

```powershell
$godot = 'D:\Game dev\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build
& $godot --headless --path . --fixed-fps 60 res://debug/tests/GameplayMathTests.tscn
& $godot --headless --path . --fixed-fps 60 res://debug/tests/DescentPlaytestTests.tscn
```

For the rendered descent, omit `--headless`, pass `--audio-driver Dummy`, and append
`-- --skip-start "--playtest-screenshot=D:/Game dev/math-audit-artifacts/descent.png"`.
