# Procedural get-up: status and plan

Goal: the carrier rises from a fall, face up or face down, through physics and active-ragdoll
control only, with no clip driving the body.

## Status (2026-11-16)

Not started in code. This note records the baseline and what the first phase needs.

- Current path: `PlayerRagdoll` plays `GetUp_Back` (face up) or `Crouch_Idle` (face down) under
  the fading ragdoll (`BeginRise`, `UpdateRise`, `RisePhase` Gather / Hold / Stand).
- Baseline headless run of `game/debug/tests/GetUpTests.tscn`: **6 of 6 checks pass**
  (face-up and belly falls: rise starts, clip choice, stands up with head at about 1.5 m).
  Only the existing clip path is tested; there is no face-down clip.
- Build: `dotnet build` in `game/` succeeds with 0 warnings and 0 errors.

## Setup needed on a fresh worktree

- Run `Godot --headless --import --path game` once (the worktree has no `.godot/imported`). The
  import takes several minutes and may need a longer timeout.
- The `nobodywho` GDExtension is absent on this machine (also absent from the main checkout). It
  prints load errors but does not stop the get-up tests.

## Phase plan (not yet done)

1. **Lab spike:** self-righting controller in `game/debug/TestActiveRagdoll`, driving hips, spine,
   legs and arms toward target angles with bounded implicit springs (the `RagdollMuscles` style:
   equal and opposite torque impulses, capped torque, stability-bounded gains). Flat ground, face up
   and face down. Success: he rises without tipping over.
2. **Stance and rise:** keep upright with `ActiveBalancePose`; arm placement and push direction from
   the ground normal; one phase variable (lying, kneel, stand) for every variant.
3. **Naturalness:** per-fall variation (weight shift, head lead, arm timing); damping tuned until the
   rise has no snap or overshoot; OpenGL captures for the director.
4. **Integration:** replace the Gather/Hold/Stand clip path in `PlayerRagdoll`; extend
   `GetUpTests` with flat face-up, face-down, slope and after-a-hit checks. No threshold changes.

## Risks

- Joint stability: the solver steps are 64 / 32 (see [active-ragdoll.md](active-ragdoll.md),
  "Joint limits and solver steps"). Any new drive must stay within those limits.
- `TerrainTests` already exceeds its 16 ms budget on spikes; the controller must be profiled.
- Multiplayer: replicate physics state, not clip frames. The multiplayer branch is not touched.

## Open decisions for the director

- Whether to keep the clip as a permanent fallback or delete it once the controller passes.
- Whether the face-down rise may use the kneel phase before standing (a stance change the director
  should approve).
