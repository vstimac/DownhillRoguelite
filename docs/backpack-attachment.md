# Backpack attachment and alignment

Godot 4.7.2 .NET, branch `fix/gameplay-math-audit`. Claude is working independently in
`release-wt` on `feature/signposts-mouse`; see `backpack-attachment-handoff.md` for the file split.

## Root cause and integration

The pack already used BoneAttachment3D on the visible skeleton. That skeleton follows the UAL
driver's animation, IK, body dynamics and ragdoll through RigFollower. No second animation driver
or manual per-frame attachment copy was needed.

The previous `(0, .05, .24)` placement assumed identical back depth for every character. The
prototype extends .125 m forward of its centre, so that offset could intersect a broader torso.
The old rigid-model placement also overwrote the model root transform. Base and equipped packs
were separately instantiated, with the unused one hidden rather than removed.

The fix adds one native BoneAttachment3D subclass, a dedicated pivot, measured rest-body clearance,
and immediate removal of the old pack from the hierarchy before replacement. Empty Back slots
restore the existing cosmetic frame pack; equipped Back visuals replace it. Missing visuals fall
back without deleting or moving inventory items. Claude's blue-debug-box fix is preserved
(current active baseline `37d5e5b4`).

```
visible Skeleton3D
  BackpackAttachment / BaseBackpackPrototype (BoneAttachment3D)
    BackpackPivot (fit, offset, rotation, scale)
      original model root (authored Transform preserved)
```

## Bones and fitting

The semantic upper-spine anchor **Spine2** resolves to actual inspected bones:

| Character | Bone |
| --- | --- |
| Modular Quaternius character | spine_03 |
| Vucic | mixamorig_Spine2 |
| Local Mixamo Carrier | mixamorig1_Spine2 |
| Mannequin fallback | DEF-spine.003 |

These joints are about 1.31 m high in the supplied rest rigs. The pivot places the pack centre
below that joint while the attachment follows the final evaluated chest pose, without overriding
bones or changing IK. Fitting happens once per visual replacement or explicit developer adjustment.
It adds no per-frame fitting, mesh allocations, physics queries or rigid-body simulation.

Automatic fit samples skinned rest vertices in a central torso band of the selected skeleton,
then uses the complete rotated/scaled pack bounds and original root transform to place its front
plane against that measured back. It is a conservative rest fit, not a live clipping solver.

Settings on the existing ClothingVisual resource:

- **AttachmentBone**: Spine2 by default for the trail pack.
- **Offset**: `(0, -.08, .24)` by default. Y puts the centre below the spine joint. With automatic
  fit enabled, Z=.24 is the legacy reference: changing it to .26 adds 2 cm rearward clearance.
- **RotationDegrees**, **Scale**: model-specific orientation and finite positive per-axis scale.
- **FitBackpackToBody**, **BackpackClearance**: measured fit, default gap .015 m.

Base settings are exported on ClothingVisualController: BasePackBone, Height, BackOffset,
RotationDegrees, Scale, Clearance and FitBasePackToBody. The BackpackAttachment exposes the live
LocalOffset, LocalRotationDegrees, ModelScale and fitting parameters; call Refit after editing.

Character-specific hand adjustments use the editor workflow in
[clothing-fitting.md](clothing-fitting.md). ClothingVisualController creates the cosmetic pack
before a preview's Setup(false) returns, and applies its saved `base_pack` fit whenever that pack
is restored. Equipped packs use their item id as the fit key. BackpackAttachment composes one
saved adjustment over its measured placement; Refit preserves it without stacking, and leaves
the original model root unchanged. The static fitting preview does not subscribe to live gear.

For another model, author a visual-only Node3D scene, including multi-mesh models if desired.
Create an ordinary Back-slot item, set Clothing Kind=Rigid and Scene to that scene. Its pivot
preserves the original root even with automatic fit disabled. Export correct GLB axes/scale.
Names alone do not guarantee compatibility with arbitrary rigs. Non-uniform rig scale, missing
bones, invalid scale and empty meshes fail with a diagnostic. Missing measurable torso geometry
uses the explicit offset and reports MeasuredBody=false; inspect that fallback manually.

## Inventory and appearance

CarrierEquipment, CarrierInventory, BackpackGrid, capacity, mass/COM, per-copy identity, durability,
condition, stat aggregation and EquipmentPersistence remain authoritative and unchanged. Existing
frame mass and removable item mass retain their rules. One displayed pack is not a storage resize.
The existing colour/condition shader still handles palette, dirt, wetness and wear.

## Manual testing

1. Build C#, open **debug/TestBackpackAttachment.tscn**, press **F6**.
2. Use the character buttons for modular, Vucic and local Carrier when available. This lab uses
   the base outfit initially; the original clothing workshop still auto-equips its prototype shirt.
3. Toggle Trail pack. Equip shows its model; unequip restores the frame visual. In Remote inspect
   the visible skeleton: one pack attachment should remain after swaps and repeated changes.
4. Scroll the left controls for offsets, rotation and scale. Edits refit immediately.
   **Apply fit / print to Output** also applies Remote Inspector edits and prints values to copy
   into the item resource. Live tuning is visual only and does not save source resources.
5. The readout shows the selected bone, item, measured fit, gap, effective offset and errors.
   F3 enables debug drawing: yellow is the anchor and cyan connects it to the pivot.
6. Walk, sprint, hold C, jump, dive and test slopes/falling/recovery in the full game. Check
   shoulders and lower back. Workshop save/load uses `user://backpack-lab-load.json` and saves
   carried equipment/storage, not the whole world or quests.

For bounded photo checks, use `-- --character=modular --pack-view=side` (or back/front). This
optional mode hides the workshop panel and uses a fixed inspection camera, leaving gameplay
camera code untouched.

## Validation and limits

BackpackAttachmentTests covers three installed characters and the fallback rig, final evaluated
bone transforms during sampled walk/brake, sprint, jump/landing, dive and native ragdoll/recovery
poses, measured mesh clearance, off-centre/rotated model
roots, equip/unequip/swap, repeated replacement, identity/condition/weight/capacity and persistence.
Invalid-bone/scale/empty-mesh cases deliberately warn. Headless tests do not establish art quality
or absence of clipping in every pose; native inspection is separate.

Before the clothing-fitting integration, local validation on 2026-11-09: build 0 warnings/errors,
all 45 attachment checks pass, including
an authored-scene equipment swap and independent local-axis rotation/scale checks. Actual sprint,
jump/landing, dive completion and ragdoll/recovery states were observed on all three characters.
Across 1,687 final-pose samples the attachment/bone transform error was zero. Native side/rear views were inspected, including the
modular character with its base outfit. The final required full smoke run passed 50 test scenes
(659 checks) and failed TerrainTests.cs:272: a 28.7 ms vegetation step against its 16 ms threshold.
BeastTests passed in this final run; it missed a charge in the preceding run. Logs:
`D:/Game dev/math-audit-artifacts/backpack-motion-smoke.log` and
`C:/Users/user/AppData/Local/Temp/tmp.FA4UVwZiFC`.
No test threshold was weakened; the full project is not marked green. No attachment commit is
made while the required smoke run is failing.

The combined clothing-fitting/backpack implementation builds with zero warnings and errors.
Focused fitting tests pass 18 checks, attachment tests pass 45, and playground tests pass 32.
Native base-outfit and side pack views were inspected. Final combined suite passed 51 scenes /
691 checks; DescentPlaytestTests failed with an uninitialized Godot GC handle at
`environment/SetPieceNode.cs:30` during model instantiation. No cause is established; TerrainTests
passed this run. Log: `D:/Game dev/math-audit-artifacts/clothing-fit-combined-smoke.log`.
The preceding full-run results describe the backpack-only baseline.
See [clothing-fitting.md](clothing-fitting.md) for the editor workflow.

Subsequent readiness validation passed all **53 scenes / 715 checks**, including the unchanged
16 ms terrain checks after allocation/scheduling improvements. See
[terrain-streaming-performance.md](terrain-streaming-performance.md) and `plant-optimized-full-smoke.log`.

The pack and outer compression straps remain labelled prototype geometry. This attachment fix
does not manufacture a production L-frame/shoulder harness, cloth simulation or universal garment
fit. Rigid packs cannot conform perfectly to every torso bend: check extreme poses and provide
authored fit variants when needed. Existing terrain timing and dirty resource/test issues are
separate from this change.

## Files

- New: player/BackpackAttachment.cs; debug/BackpackAttachmentLab.cs;
  debug/TestBackpackAttachment.tscn; debug/tests/BackpackAttachmentTests.cs/.tscn; generated script UIDs.
- Modified: player/ClothingVisualController.cs; inventory/ClothingVisual.cs;
  inventory/items/trail_backpack.tres; debug/ClothingLab.cs (optional starter shirt/reusable controls).
- Docs: this guide, clothing-fitting.md, backpack-attachment-handoff.md and debug/README.md.

The subsequent integration also brings in Claude's ClothingFit resources/helpers, fitting scene
and tests, adds preview lifecycle hooks to CarrierWornGear and ClothingVisualController, passes
CharacterId from CarrierModel, and marks the referenced character/item resources for editor use.
The complete fitting file list is in [clothing-fitting.md](clothing-fitting.md).

Previously staged DevPlayground work is separate and must not be swept into an attachment commit.
No imported model, rig, animation, movement controller, inventory logic or gameplay maths changed.
