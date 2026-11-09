# Adding a playable model or clothing

The player has a reusable animation/IK/ragdoll driver and a separate visible model. This supports
compatible model changes; it is not a promise that any downloaded GLB will animate or fit clothing.
NPC travellers use a separate setup: see `npc-models.md`.

## A new playable character

1. Prepare a textured, skinned humanoid GLB in Blender, at the reference character's scale. Check
   the feet origin, upright axis, proportions and rest pose. Keep a clean T/A reference pose.
   Prefer the existing Mixamo naming/hierarchy and test mapping rather than renaming arbitrary
   bones to look compatible. An unrigged mesh cannot follow the skeleton.
2. Put the asset under `art/characters/<id>/`. Import it, inspect its Skeleton3D, Skin and textures,
   and fix scale/axis issues at source. The player preset does not have the NPC's ModelScale field.
3. Duplicate `art/characters/vucic/vucic.tres`. Give it a unique Id, DisplayName, description and
   ModelPath. Make its Stats resource unique before changing health, stamina, speed or ability.
4. Add the new preset's resource path to CharacterPaths in `art/characters/roster.tres`.
5. Build C#. Start F5 → Character, choose the model, confirm, then **New Run**. The live run's
   origin/stat profile is locked; changing the saved appearance is not a mid-run ability reset.
6. Check idle, ordinary walk, braking, sprint, jump/dive, slopes, foot placement, falling and
   recovery. Verify hands/feet, body height against collision, clothing and pack attachments.
   Use `debug/TestClothing.tscn` for clothing changes and `debug/TestCharacterAbilities.tscn`
   for profile checks. Add a regression for the actual new rig before declaring it supported.

`RigFollower` maps the displayed model onto the mannequin driver's final pose, including IK and
ragdoll output. Common Mixamo prefixes are handled; unsupported hierarchy, proportions or rests
still need asset work or explicit retargeting. A shared set of bone names does not prove fit.
Missing/untextured character assets use the existing dressed-mannequin fallback where detected.

## A new shirt, jacket, trousers or gloves

1. In Blender, fit the garment to the **actual target character** using its armature and rest pose.
   Transfer and clean weights; inspect bends at shoulders, elbows, hips and knees. Keep the same
   skeleton transforms, scale and bind pose. Use at most four influences per vertex.
2. Export the garment plus the full bind skeleton as GLB, with separately named garment meshes.
   The current imported-clothing path expects the selected MeshInstance3D nodes directly beneath
   their Skeleton3D. Do not assume an arbitrary imported scene layout is supported.
3. Duplicate `inventory/items/trail_shirt.tres` into a new item resource. Set unique ItemId,
   DisplayName, slot, mass, cell dimensions, durability and gameplay statistics.
4. Make its Clothing resource unique. Set Kind to **Skinned**, Scene to the imported garment,
   MeshNames to the exact garment node names, and CharacterIds to the presets you have validated.
   A cloned prototype Kind will keep generating prototype geometry instead of using your scene.
5. Set HideBodyMeshes only for real, separately partitioned body pieces that are fully covered.
   An unpartitioned body cannot have just its torso hidden; author partitions or masks instead of
   hiding the entire character. Different proportions may require separate fitted garments.
6. Test equip, unequip, swapping with a full pack, movement, ragdoll, dirt/wetness and save/load
   in `debug/TestClothing.tscn`. Add the item to the supply table only after it fits and works.

The compatibility check validates every skin bind against the target bone and its global rest
transform. It rejects mismatched rests/proportions; it cannot judge visual clipping or weight quality.
Changing texture size only changes image detail. It does not change the mesh, rig, UV layout or fit.

## A backpack, helmet or rigid accessory

Use a separate 3D scene and a unique item resource. Set Clothing Kind to **Rigid**, select the
Scene and AttachmentBone, then tune Offset, RotationDegrees and Scale. Validate the attachment
while running, bending and falling. These pieces follow BoneAttachment3D rather than deforming
like a shirt. Existing backpack/frame unification and production strap fitting remain separate work.

The detailed rig/export pipeline and current prototype limitations are in `modular-clothing.md`.
