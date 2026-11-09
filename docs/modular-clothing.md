# Modular clothing and equipment

Implemented against **Godot 4.7.2 .NET**, verified from the project SDK and installed engine.
This extends the existing equipment system. No replacement equipment autoload or second player
stat system was introduced. Work remains on `fix/gameplay-math-audit`.

## Codebase findings and integration

| Responsibility | Existing component / extension |
| --- | --- |
| Character | `player/Player.tscn`, `PlayerController`, `CarrierModel`, `CarrierRoster` |
| Animation | Quaternius driver Skeleton3D, CharacterAnimator, IK, body springs, ragdoll, RigFollower |
| Equipment manager | `inventory/CarrierEquipment.cs` |
| Equipment item resource | `inventory/ItemDefinition.cs`, now with optional `ClothingVisual` |
| Inventory | CarrierInventory, BackpackGrid, PackedItem, loose/pocket StoredItem copies |
| Visual controller | `player/ClothingVisualController.cs`; existing CarrierWornGear handles legacy rigid pieces |
| Compatibility | `player/ClothingCompatibility.cs`, semantic rig mapping in ClothingRig |
| Stat aggregation | CarrierEquipment rebuilds GearSnapshot; existing movement/stamina/needs/impact code consumes it |
| Persistence | Explicit `EquipmentPersistence` carried-load snapshots; existing run/record saves remain separate |

The two installed playable meshes are The Carrier (local Mixamo FBX) and Vučić (GLB). Plenki
remains an unavailable roster entry. The local Mixamo model is excluded from git by the existing
asset policy; fresh checkouts may therefore offer fewer characters. The mannequin driver remains
the fallback. An imported character with no usable albedo textures also falls back to that rig,
with a prototype shirt and backpack instead of an incomplete untextured character.

## Slots and ownership

Supported unique slots: Head, Face, InnerTorso, OuterTorso, Hands, Legs, Feet, Back, Waist,
Accessory. Legacy `Torso=2` aliases OuterTorso; `Cape=6` aliases Back, preserving existing item
resources. Cape and external backpack therefore compete for the same Back slot. Use
`EquipmentSlots.All`, not enum enumeration, when building a unique slot list.

Equip/swap/unequip use a pure storage preflight. The plan removes the incoming copy, finds room
for the outgoing copy, computes the resulting pocket capacity and repacks any pocket spill.
Only a valid complete plan is committed. Failure preserves the original placements and copies;
it does not drop or destroy pocket contents. The worn item leaves normal backpack storage.
Per-copy durability, wetness, dirt, variant and tint survive pack/loose/pocket/worn/world transfers.
Every physical copy also has a stable GUID, independent of its shared item resource. Duplicate
pickups of an already-owned identity are rejected. Snapshot restoration removes only matching
saved identities from world drops, leaving other copies of the same kind untouched.

The built-in backpack grid/frame remains the game's existing storage container. The new
`trail_backpack` is a removable external pack on that frame, with its own carried mass and pocket;
it does not resize the grid or silently replace the frame's weight. A naked frame has a cosmetic
base-pack model, and the equipped external pack replaces that base visual.

## Visuals and compatibility

- `trail_shirt`: fitted **skinned prototype** in InnerTorso.
- Oilskin coat, wool trousers and leather gloves: fitted **skinned prototype** visuals.
- `trail_backpack`: **rigid prototype** on a Spine1 BoneAttachment3D.
- Existing hats/helmets and cape use their existing bone-attachment implementation.
- Other slots can use authored rigid or skinned `ClothingVisual` scenes.

The trail shirt and external pack are registered as rare entries in the normal supply table,
available from the first circle. They also start in the clothing workshop for immediate testing.

The prototypes generate actual vertex bone indices, normalized weights and inverse-rest Skin
binds for the current rig. They are low-poly test garments, not production art or real cloth.
Measurements use the actual rig's joint spacing and import scale. The semantic joint mapping
supports the project's Mixamo and DEF mannequin joints; unsupported rigs fail gracefully.

Imported skinned garments require a source skeleton and explicitly selected mesh names. Every
skin bind must map to a target bone with the **same global rest transform in skeleton space**.
Names alone are insufficient. Different rests/proportions are rejected, requiring a fitted
character-specific variation. CharacterIds can further restrict a visual to validated presets.
Native body pieces are hidden only after a garment builds successfully and are restored on
removal. Controllers restore only meshes they hid, leaving helmet/vest visibility to its owner.
An unpartitioned body cannot be selectively cut by this system; author partitions or masks.

`clothing_condition.gdshader` supplies per-instance colour/variant, cloth grain, wet darkening
and roughness, dirt and abrasion. It does not duplicate mesh resources or run cloth simulation.
Wetness/dirt use world-clock rates; rain wets clothing, dry conditions dry it, walking adds dirt.
Wet garments lose some warmth. Durability remains the existing hit-point system.

## Gameplay effects

Item resources retain weight, grid dimensions, category, durability, warmth, rain resistance,
grip, wind-catching area and pockets. New optional fields add movement/stamina multipliers,
wind resistance, fall/physical protection, description and rarity metadata. Rarity does not
multiply stats. Defaults preserve existing tuning.

Effects are rebuilt from currently worn, unbroken pieces, so repeat equips or loads cannot
stack them. Broken garments retain mass and their visual damage. Movement and route estimates
use the same speed factor; movement stamina and route estimates use the same stamina factor.
Fixed stamina costs (jump/dive) also apply that factor once.
Wind resistance affects weather chill. The existing ragdoll impact path applies fall and
physical protection once; hunger/thirst/cold damage does not receive armour protection.

## Save/load

`EquipmentPersistence.Save/Load(inventory, path, out problem)` snapshots the complete carried
load: grid placements, loose copies, pockets, worn slots, pack durability and per-copy appearance.
This scope prevents worn items being duplicated by independently restoring storage. Version,
dimensions, resource paths, slots, placements, duplicate cargo identities and condition data are
validated before mutation. Repeated loads replace state rather than adding items or effects.
The current identity-bearing schema is version 2; older experimental snapshots are rejected safely.
Writes replace a temporary file atomically. Invalid/missing files leave the current load intact.

This is **explicit equipment/load persistence**, not a saved world or run-resume implementation.
Arbitrary unsaved inline item resources cannot be serialized by identity: save them as item
resources first. Current canonical persistent items live in `res://inventory/items/`.

## Blender → GLB → Godot workflow

1. Export the reference character's actual skeleton/rest pose and keep its scale and bone axes.
   The project driver is a T-pose; imported characters can have their own validated rests.
2. Fit clothing to that character in Blender. Keep the exact armature, bone hierarchy and rests;
   do not rename bones or apply transforms to only the garment after binding.
3. Transfer weights from the body, normalize them, and inspect shoulders, elbows, hips and knees.
   Keep at most four influences per vertex for the initial pipeline. Remove stray weights.
4. Keep garment meshes separately named. Prepare body partitions for covered torso, hands and
   feet; store exact covered mesh names in HideBodyMeshes. Preserve a complete base outfit.
5. Export **GLB**, selected armature and garment meshes, skinning and materials/textures. Export
   the complete bind skeleton; avoid an exporter option that removes required nondeform parents.
   Apply consistent metre scale to character and clothing; do not independently rescale the skin.
6. Import in Godot 4.7.2 .NET, confirm Skeleton3D/Skin and texture assignments, then rebuild C#.
   Do not edit generated `.godot` data or destructively edit imported scene nodes.
7. Create a ClothingVisual resource: Skinned, imported Scene, MeshNames and validated CharacterIds.
   The current imported workflow expects those mesh nodes directly under its source Skeleton3D.
   Rigid equipment instead uses an AttachmentBone, metre Offset, rotation and scale.
8. Validate rest, idle, walk, sprint, brake, jump, step-up, ragdoll and recovery. Check front/back
   silhouettes and all covered body partitions. A mesh passing bind validation still needs this
   geometry/clipping review. Fit a separate variant when character proportions differ.

## Test it in Godot

1. Build C#.
2. Open **`debug/TestClothing.tscn`** and press **F6**.
3. Shirt and external backpack start equipped. Use the parchment buttons to toggle each garment,
   change between Carrier and Vučić, wet/dirty/recolour the shirt, or reduce its durability.
4. Walk/sprint/jump with the normal controls. Escape releases the mouse for the buttons.
5. Save carried load, change equipment, then Load carried load. The lab file is
   `user://clothing-lab.json`. Try a full backpack before taking something off.
6. Drop the shirt, face it and press E to pick it up; then equip it again. Condition stays intact.
7. Use Remote → Skeleton3D to inspect skinned prototypes and BoneAttachment3D nodes.

For the preset/colour/rotating portrait screen, run F5 → Character. The starter character-creation
flow and its current asset limits are documented in [character creation](character-creation.md).

Automated scenes: ModularEquipmentTests, CharacterPreviewTests, CharacterTests, GearTests,
GearConditionTests, CarriedMassTests, PocketUseTests, plus the existing full regression suite.

Initial integrated validation passed **40 scenes / 475 checks**, followed by **43 focused
checks** after the fallback, pickup appearance and skin-validation additions. C# builds with
zero warnings/errors and the 300-frame main-scene smoke passes. Native Forward+ captures of
the menu and the interactive workshop were rendered and inspected. The recorder's bounded
ring was separately checked: eight retained images, consecutive rendered frames 25–60 at
exactly five-frame intervals. The untextured fixture intentionally emits one fallback warning.

Skin checks accept the actual Vučić skin on its own rig and reject identical bone names with
changed rest transforms. All four prototypes have finite vertices and valid weights. Uploaded
weights use a two-unit UNORM16 tolerance, matching the measured 0.99998474 sum and the engine's
[16-bit weight unpacking](https://github.com/godotengine/godot/blob/master/servers/rendering/renderer_rd/shaders/skeleton.glsl).

The final code regression passed **42 scenes / 497 checks**, all processes exiting successfully
with no FAIL/error lines, followed by a clean 300-frame main-scene smoke. After the final neckline
geometry refinement and native-body masking checks, the four clothing/customization suites passed another **58 checks** and
the actual Vučić creation screen was rendered and inspected again. This validates the implemented
workflow and current rigs; production fit, masks and the complete animation matrix still require
the manual asset checks above.
The legacy GearTests also passed its 28 checks on that final source. Registering the new loot
entries was followed by clean Survival, Station, Objective and Wayside scenes (53 checks).

## Every-five-frame capture

The debug-only PlaytestCapture autoload records the **game viewport** every five rendered
frames, including menus while the world is paused. It is **off by default** (the GPU readback and
JPEG encoding halved the frame rate, about 118 → 58 FPS on the dev PC): F9 starts/stops it during
play, and `-- --capture` starts a run with it on. Restart an
already-running game once after building to load the new recorder.

Default output: `D:\Game dev\math-audit-artifacts\live-capture\<session>`. Each session keeps
the newest 600 full-resolution JPEGs in a rolling buffer and an index.json with rendered frame,
sequence, selected character, paused state, position and thumbnail pixel-change score. The
score highlights visual changes; camera motion, fog and lighting also affect it, so it is not
a semantic bug detector. Captures must still be inspected. GPU readback/encoding adds overhead;
disable capture when measuring performance. It does not record other desktop applications.

Overrides after `--`: `--capture`, `--capture-off`, `--capture-every=5`, `--capture-max=600`,
`--capture-directory=D:/path`. Headless runs never capture; movie exports skip this recorder
unless explicit capture arguments are supplied. Capture ends with the game process.
