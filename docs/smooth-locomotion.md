# Smooth locomotion with the existing animation assets

Open **`debug/TestSmoothLocomotion.tscn`**, build C# and press **F6**. Click **Replay full demo**
for walking, jogging, a faster turning run, a stop, a jump/landing and braking. Each preview also
has its own button. **Manual controls** restores normal keyboard movement: WASD, Shift sprint,
C brake and Space jump. Esc releases the mouse for the buttons. F9 toggles screenshot recording.
The character buttons compare the Carrier and Vučić; they do not change the saved selection.

The previews edit a scene-local copy of movement settings and refill vitals for inspection.
The full game keeps its original movement speeds, stamina costs, collision, balance and damage rules.
Press **F5** to test the same animation changes in the full game. The older
`debug/TestLocomotion.tscn` remains the stairs/slopes/rock-field course with its existing overlays.

## Assets used

The project's **Quaternius Universal Animation Library Standard** GLB contains 46 animations
and the existing 53-bone driving skeleton. Its license is included at
`art/characters/quaternius_ual/LICENSE.txt` (CC0). This change uses nine existing imported clips:

| Imported Godot clip | Use |
| --- | --- |
| `Idle` | Normal standing |
| `Walk` | Slow stride |
| `Jog_Fwd` | Normal faster movement |
| `Sprint` | Fast/downhill stride |
| `Crouch_Idle`, `Crouch_Fwd` | Partial careful posture while braking/sliding |
| `Jump` | Existing looping airborne pose |
| `Jump_Start` | Short authored takeoff transition |
| `Jump_Land` | Short authored landing transition |

The GLB's source names include `_Loop`; Godot strips that import suffix. No animation files were
downloaded or replaced. Existing Mixamo vault assets remain optional, local and unmodified.
The Quaternius skeleton still drives the available characters through `RigFollower`, followed by
the existing clothing system. This does not certify an arbitrary new model or skeleton as compatible.

## What changed

Previously, walk, jog and sprint used separate playback rates. Keeping every branch running with
`Sync = true` did not keep their footfalls together: their cycle durations differed, so a speed
change could mix poses from different parts of the stride.

Each moving gait now has a **one-second normalized timeline**, stretched to sample its original
clip. Walk, jog, sprint and crouch advance at the same cycle rate, including while their blend
weight is zero. Speed bands still blend the existing gait poses smoothly. The shared cadence is:

`cycles/second = actual horizontal speed / blended distance per cycle`

Each clip's distance per cycle is its measured native speed × original clip length. The same
walk/jog/sprint/brake weights blend those distances. Playback limits use the blended original
duration. Pose weights are smoothed, while cadence follows actual speed so acceleration does not
leave the feet behind. The default minimum playback rate is zero: a slow stride may slow down
instead of forcing the feet to advance faster than the body. At rest the gait clock stops and idle blends in.
This fixes phase drift without repeatedly
restarting clips or changing physical movement to match an animation.

`Jump_Start` and `Jump_Land` are sampled over short configurable transitions, with smooth fade-in
and fade-out envelopes. Landings keep more of the locomotion pose when moving, so a stationary
crouch does not replace a running stride. Optional missing transition clips contribute zero weight.
The transitions cancel for a vault, ragdoll or death; the existing systems keep pose ownership.

The animator supplies its parameters before the AnimationTree evaluates and before the shown
character copies the driving model's step offset. Teleport/respawn clears old speed, air/brake
weights, step offsets and transitions. The visual's original local position is preserved.
Foot IK, ground alignment, procedural body weight/lean and the rig follower remain in place.

## Tuning in Godot

Open `player/Player.tscn`, select **Animator**, and inspect:

- **Blend bands:** the physical speeds at which walk blends to jog and jog to sprint.
- **Native clip speeds:** asset calibration values in m/s, not the player's target movement speeds.
- **Min/Max playback rate:** cadence bounds. Large changes can reintroduce foot sliding.
- **Takeoff Time / Landing Time:** transition duration in seconds, initially 0.28 / 0.45.
- **Transition Fade Time:** edge blending, initially 0.09 seconds.
- **Moving Landing Weight:** 0.25 at running speed, preserving most of the current stride.
- **Speed/Brake/Air sharpness:** how quickly presentation catches up with the controller.

Movement itself remains in `player/default_locomotion.tres`. The workshop's Walk/Jog/Fast buttons
temporarily change only its local copy to inspect different blend bands. Default flat sprinting
can still use the jog asset; the faster sprint asset blends in at the configured higher speed band.
Authoring native start/stop, pivot and directional-strafe clips would support further polish;
those motions are not invented from unrelated combat or dancing animations.

## Validation

`debug/tests/AnimationSmoothTests.tscn` checks the actual imported clips, physical target speed,
normalized gait clocks across repeated speed changes, idle settling, Vučić's retargeted turn,
real jump input/landing, vault/ragdoll ownership and teleport reset. Its thirteen checks pass.
Measured gait phase separation stayed below 0.001 cycle (observed 0.0000), and the largest
per-frame bone movement in the retargeted turn was approximately 0.087 hip heights (limit 0.3).
Existing `LocomotionTests` still passes all 36 stairs, slopes, braking, foot-contact and grip checks.

Packing's former auto-packed fixtures had a larger absolute sideways COM offset in the supposedly
centred pack than in the high/right pack. The descent test now uses a validated high/right cell layout
and legal lateral moves to centre the low pack. It checks actual COM separation, equal mass and
the same physical item IDs. The test measures **1.5× sustained RMS sway** over the whole descent,
plus a greater peak/no safer worst state for the high pack and a stable-or-wobbling low pack.
This replaces the old 1.5× single-peak assertion, which depended on the lead foot at the blend boundary.
Observed COMs are (0.061, 1.225, -0.208) versus (-0.0004, 1.069, -0.201), equal mass 23.4 kg.
RMS sway was 0.320 versus 0.186 (1.72×), with the high pack wobbling and the low pack stable.
The existing load-A-falls/load-B-stays-up balance test remains unchanged.
No balance equations, balance settings or inventory rules changed.

Build, import, full regression suite and rendered checks are recorded in
`../math-audit-artifacts/smooth-*.log`. Screenshot recording remains every five rendered frames;
headless checks cannot establish visual quality. The interactive demo was rendered and its walk,
jog, takeoff and landing frames inspected. Human playtesting is still needed for subjective feel.
Current prototype garment fit and the existing model's deformation remain asset limitations.

Final repository smoke script: **RESULT: PASS**, Godot **4.7.2 .NET**, zero C# warnings/errors,
successful import, autoload/extension loading, 300-frame main smoke and **44 scenes/533 checks**.
The final demo also ran at fixed 30- and 60-FPS time steps with clean logs; screenshots were
inspected for both. These are correctness/rendering checks, not a hardware performance benchmark.

## Changed files

- `player/CharacterAnimator.cs`: shared gait cadence, transitions and update/reset ordering.
- `debug/SmoothLocomotionLab.cs`, `debug/TestSmoothLocomotion.tscn`: interactive workshop/demo.
- `debug/tests/AnimationSmoothTests.cs`, `.tscn`: integration regressions.
- `debug/tests/PackingTests.cs`: valid same-item fixtures, COM/ownership checks, full-descent RMS contrast and peak/state safeguards.
- Godot-generated `.uid` files for the two new C# scripts.
- This guide and links in root, player and debug READMEs.
