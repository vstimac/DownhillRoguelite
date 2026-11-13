# First hybrid: active recovery

The local player layers spring-driven recovery over the existing locomotion clips, body dynamics,
slope-aware foot IK and physical falls. The existing balance model decides whether a stumble is
recovered or becomes a fall. This milestone does not implement animation-free physical walking,
autonomous recovery footsteps or networked skeletal poses.

## Try it

Build C# and open `game/debug/TestActiveRagdoll.tscn` with F6, or choose **Aktivna ravnoteza i
ragdoll** in `debug/DevPlayground.tscn`. The lab has left/right knocks, a physical fall, a slope,
reset, an A/B toggle, a replay and manual controls. WASD/Shift/C/Space/Ctrl retain normal movement.
The demonstration also runs with `-- --active-balance-demo`.

Green marks the support centre, red the projected centre of mass, and cyan the hip correction
(drawn four times larger for readability). Existing foot targets and knee poles are drawn too.
F3 toggles lines. Lab falls use a higher safe-impact threshold; normal gameplay damage is unchanged.

## Recovery and physical muscles

- `ActiveBalancePose` runs after SpineLean/BodyDynamics and before FootIK. It moves hips against
  horizontal COM error (up to 7.5 cm), lowers them with increasing imbalance (up to 12 cm), and
  opens free arms. Exact damped springs integrate recovery. Stable balance leaves the original
  hip pose untouched, including avoiding no-op writes to that bone.
- FootIK removes the applied hip displacement from ankle planning, including native modifier
  Influence/Active, and solves the legs against the corrected hips. This keeps contact targets
  on their original support rather than dragging them with the pelvis.
- The sword owns both arms while drawn; a lit torch owns the left. Existing final-pose retargeting
  continues to drive the character, clothes and backpack. The controller capsule is not moved
  by the recovery pose; local bone-mounted equipment and blade traces follow the local skeleton.
- `RagdollMuscles` captures final relative arm orientations in SkeletonUpdated. For the first
  0.45 seconds of a living fall, four joints resist deformation using conservative effective
  limb inertia, implicit spring/damping gains and a 1.5 Nm per-muscle torque cap. Each torque
  has an equal opposite torque on its parent. Elbow torques act only on the hinge axis.
- Above MaxControlledSpeed (4 m/s), muscles relax for the remainder of the fall. Fast throws and
  dives retain the passive ragdoll; muscles cannot re-engage mid-fall. Death is passive too.
  No root lift, global upright target or position/velocity override is added.
- Down/up transitions and teleport clear recovery springs. PlayerRagdoll continues to own impacts,
  hip tracking and getting up. Inspector exports on both nodes expose tuning and disable switches.

## Co-op boundary

Unsynchronized poses are primarily a remote presentation limitation in the current owner-simulated
co-op beta. RemoteCarrier receives root position/yaw, clip timing, death and torch state. Its
collision layer/mask are zero; it publishes no local gameplay events and runs neither new controller.
Other players therefore see normal clips rather than the exact hip correction or physical limbs.

The muscles themselves are **not purely cosmetic**: torques can change local fall trajectory and
impact damage. PlayerRagdoll tracks the physical hips with the player root, and NetSession sends
that resulting position and existing death state through the current protocol. No second physics
authority or bone-pose RPC is introduced. This milestone does not establish precise shared physical
contacts or PvP hitbox agreement.

Main's HUD commit `e9f29f82` adds display events without changing the Lit getter used here.
The voices commit `91c3ccc7` does not touch this controller/IK integration. Those main changes
are separate from this feature-branch milestone.

## Validation, 2026-11-13

- Separate clean parent `77cdc35d`: original joint test passes (14.65 degrees sideways / 9.93
  degrees swing excess); the corrected hybrid passes with identical metrics and unchanged limits.
  The initial hybrid regression was fixed by leaving stable hips untouched and relaxing fast falls.
- ActiveRagdollTests: **45 checks pass**. Coverage includes rotated world axes, bounded hip shifts,
  fixed ankle contacts, native modifier blending/disable, a real 20-degree cross-slope, sword/torch
  ownership, stable poses, high-energy launches, real muscle impulses, recovery, death and teleport.
- Lab controls: **8 checks pass**, exercising real buttons, A/B, both knocks, slope placement,
  movement input and physical fall/recovery.
- Full candidate suite: **74/75 scenes pass**. Build has zero warnings/errors; import and a
  300-frame main-scene smoke pass. RagdollMotion, locomotion, animation, balance, combat and torch
  regressions pass.
- Remaining failure: TerrainTests' existing 16 ms plant-invocation limit. The clean parent initially
  passes at 15.7 ms, but three predefined clean-source profiled runs fail at 28.9 / 29.3 / 20.6 ms.
  Those spikes include GC pauses (up to 28.7 ms). The corrected hybrid's full-run maximum is 26.6 ms;
  a profiled spike of 37.6 ms includes 36.8 ms of GC pause. This timing issue is reproducible without
  this feature; no thresholds were changed and the full suite is not described as green.
- Four-player co-op harness: **23 checks pass**, including LAN discovery, state exchange, shared
  pickups, combat delivery, death/respawn, revival, chat, marks, webs and shared loot. Test peers
  have separate save directories. This is localhost/LAN-discovery coverage, not internet latency
  or exact remote-pose validation.
- OpenGL rendered captures were reviewed; Forward+ rendering was not validated in this environment.

Logs and captures live under `D:/Game dev/math-audit-artifacts/`: hybrid-clean-source-proof.txt,
hybrid-clean-joints.log, hybrid-clean-terrain.log, hybrid-clean-profile-1/2/3.log,
hybrid-fixed-full-smoke.log, hybrid-branch-ActiveRagdollTests.log,
hybrid-branch-RagdollJointTests.log, hybrid-lab-probe.log and hybrid-coop-summary.log.
Test saves are isolated from player saves.
The remaining terrain/GC timing issue needs investigation before a release.
