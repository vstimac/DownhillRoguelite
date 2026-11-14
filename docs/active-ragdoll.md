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
  0.45 seconds of a living fall, the two elbow hinges resist deformation using conservative effective
  limb inertia, implicit spring/damping gains and a 1.5 Nm per-muscle torque cap. Each torque
  has an equal opposite torque on its parent. Shoulder cone joints stay passive. Elbow springs
  extract only hinge twist and use the parent's constraint axis; forbidden swing is never a target.
  The drive waits two physics steps and releases for the entire fall if hinge axes separate by
  more than five degrees, leaving alignment to the native constraint solver.
- Physical muscles are **off by default** (`RagdollMuscles.Enabled`, explicitly false in Player.tscn).
  The lab's **Toggle arm muscles (opt-in)** button enables them for a trial. Turning them off stops
  impulses immediately; turning them back on during a fall waits for the next fall.
- Above MaxControlledSpeed (4 m/s), muscles relax for the remainder of the fall. Fast throws and
  dives retain the passive ragdoll; muscles cannot re-engage mid-fall. Death is passive too.
  No root lift, global upright target or position/velocity override is added.
- Down/up transitions and teleport clear recovery springs. PlayerRagdoll continues to own impacts,
  hip tracking and getting up. Inspector exports on both nodes expose tuning and disable switches.

## Multiplayer boundary

This standalone integration has no network dependency. The separate multiplayer branch carries
its own pose replication and shared-world authority; its co-op commit `124caa03` is not part of
this main-branch integration.

Muscles can change fall trajectory and impact damage when explicitly enabled. They remain off
by default, and the existing PlayerRagdoll owns physical simulation, impacts and recovery.

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

## Elbow follow-up, 2026-11-14

The original hybrid is the standalone ragdoll commit `832a1217`; its patch has no network files.
The elbow fix and opt-in switch are a separate ragdoll-only follow-up. Co-op work stays separate.

`ActiveRagdollElbowTests` exercises actual enabled muscle impulses at 1, 2 and 3 m/s, retaining
the original joint test's 16-degree limit over six held physics frames. The identical new trial
against the original `832a1217` implementation fails at 16.03 degrees; the corrected elbow-only
drive reaches 9.98 degrees with 124 paired impulses. The original `RagdollJointTests` source and
all its thresholds are unchanged. The existing ankle/neck/wrist swing sensitivity is not hidden
by raising a limit. Test saves and A/B logs are isolated in `mp-wt/.validation/`.

Follow-up validation: ActiveRagdollTests passes 55 checks; the enabled-elbow trial passes at
30 / 60 / 120 FPS (7.50 / 9.98 / 9.89 degrees). The unchanged original joint test passes all
eight checks at 60 FPS (14.65 degrees sideways, 9.93 degrees excess swing). Build and import pass;
the final full suite passes 76/77 scenes, with only the existing TerrainTests streaming timing check failing
at 18.6 ms against 16 ms (a previous run's plant timing was 17.3 ms). The lab's explicit opt-in control and default-off status were also
checked in an OpenGL render. No test threshold was raised.

## Main integration, 2026-11-14

Integrated onto main `53c4ac81`, retaining its crouched get-up and timed airborne roll handoff.
The torch ownership regression uses the ordinary local torch actions, so the tests compile and
run without multiplayer APIs. `DiveWithMusclesTests` repeats all 12 dive/roll checks with the
protective elbow drive enabled; the ordinary player remains opt-in.

The complete main suite passes **81/81 scenes**, including TerrainTests, the original joint test,
55 hybrid checks, the enabled-elbow trial, both dive variants, save/UI tests and 36 update checks.
Build, asset import, main-scene smoke and the OpenGL lab preview also pass. Logs are under
`main-ragdoll-wt/.validation/`. The earlier terrain timing failures above are historical results
from the multiplayer worktree, not the result of this main integration.

The existing Beta 2.9 packages were verified against GitHub's asset SHA-256 values and their draft
publication was completed. Both public full-download links return HTTP 200. The actual updater
planner selects the 2.9 patch for Windows 2.8, the 2.9 full build for older Linux, and no update
for either platform already on 2.9. These prebuilt 2.9 packages predate this ragdoll integration;
the new controller changes will ship in a subsequent build/update.
