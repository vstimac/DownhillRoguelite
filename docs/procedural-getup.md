# Procedural get-up: status and plan

Goal: the carrier rises from a fall, face up or face down, through physics and active-ragdoll
control only, with no clip driving the body.

## Status (2026-11-16)

- **Default path unchanged:** the clip rise (`GetUp_Back` face up, `Crouch_Idle` face down).
  `GetUpTests` default run: **6 of 6 pass**, exit 0.
- **Procedural controller:** `game/player/RagdollRise.cs`, switched on by
  `PlayerRagdoll.ProceduralGetUp` (default **off**). It is **not working yet**: it does not stand him up.
- **Experimental checks:** `GetUpTests -- --procedural` adds four procedural rises (face up and face
  down: starts, no clip, stands within 15 s, drift under 1 m). Current run: **the "starts" and "no clip"
  checks pass; "stands him up" and "drift" fail on both sides** (see numbers below). The run exits 1,
  so it is opt-in and not part of the default suite.

## Design of the controller (as built)

- Every bone pair of the rig is driven toward a target relative orientation, with the tuned
  `RagdollMuscles` law: bounded implicit spring and damping, equal and opposite impulses, a torque cap
  per kilogram of the child body.
- The target blends from the lying shape (captured when the rise begins) to the rest pose, by one
  phase variable: 0 lying, 1 standing (smoothstep over `RiseSeconds`).
- The hips (root) get a self-righting torque toward world up (`RootFrequency`, `RootTorqueCap`).
- The rise ends when the phase is 1 and the hips' up axis holds within about 25° of world up for
  0.25 s (`StoodUp`), or at a timeout of 8 s (not stood). After it ends, the existing blend-out takes
  over to the animation.
- Not built yet: arm placement and push direction from the ground normal, a kneel stage, per-fall
  variation, and the lab page in `TestActiveRagdoll`.

## Numbers (GetUpTests, headless, 60 Hz)

| Setting | Face up: stood / frames / drift | Face down: stood / frames / drift |
|---|---|---|
| Gains 2.5 Hz, 8 N·m/kg, root 1.2 Hz / 150 N·m | no, 480 frames (timeout), 2.59 m | no, timeout, 6.07 m |
| Gains 1.5 Hz, 12 N·m/kg, root 2.0 Hz / 400 N·m (run A) | no, 515 frames, 5.32 m | no, 516 frames, 4.53 m |
| Same gains, second run (run B) | no, 516 frames, 7.45 m | no, 509 frames, 1.88 m |

Run A and run B used the same settings and differ in drift, so the physics result varies from run to
run. Rounded numbers are taken from the logs; the stood flag is false in every case.

Trace (face up, run A): the hips stay near ground height (0.10 to 0.15 m), with tilt 0.4 to 0.5
(upright is 1.0), and hip speed 1 to 2 m/s. The hips never leave the ground. The joint springs
seem to fight the contacts, and the legs have no stance to push from.

The "he stands up" checks that pass in the procedural run are the existing blend-out to the
animation after the timeout, not the procedural rise. The new "stands him up" check is what
measures the procedural rise.

## Next steps (not done)

1. Give the rise a stance: a kneel phase first (one hand and the feet under the hips), with the
   push direction taken from the ground normal, so the legs have something to push against.
2. Check the hips' lift per joint in the lab before tuning gains.
3. Only then flip the default and replace the clip path, if the procedural rise stands him up
   within the timeout with drift under 1 m on flat ground, then on a slope and after a hit.

## Setup needed on a fresh worktree

- Run `Godot --headless --import --path game` once (the worktree has no `.godot/imported`). The
  import takes several minutes and may need a longer timeout.
- The `nobodywho` GDExtension is absent on this machine (and in the main checkout). It prints load
  errors but does not affect the get-up tests.

## Risks

- Joint stability: the solver steps are 64 / 32 (see [active-ragdoll.md](active-ragdoll.md),
  "Joint limits and solver steps"). Any new drive must stay within those limits.
- `TerrainTests` already exceeds its 16 ms budget on spikes; the controller has not been profiled.
- Multiplayer: replicate physics state, not clip frames. The multiplayer branch is not touched.

## Open decisions for the director

- Whether to keep the clip as a permanent fallback once the controller passes.
- Whether the face-down rise may use a kneel stage before standing (a stance change to approve).
