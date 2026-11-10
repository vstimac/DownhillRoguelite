# Fitting clothes by keys

Every worn piece (hat, shirt, sleeves, trousers, gloves, boots, pack…) can be moved, turned and resized on
each character by hand. The fits are saved in `player/clothing_fit.tres` and the game applies them as the
piece is put on. There are two ways to fit: in the editor with the gizmo (`debug/ClothingFitting.tscn`),
or run as a game from the keyboard, like the sword fitting (this page).

## Open it

```
godot --path "D:\Game dev\release-wt" res://debug/ClothingFittingPlay.tscn
```

(or open `debug/ClothingFittingPlay.tscn` in the editor and press F6). `--path` must point at the checkout
that has the clothing work. Run from any other folder and Godot opens a different project, which has no
fitting scene, and it closes at once.

The character stands in its rest pose wearing the outfit. A yellow dot marks the piece being fitted.

## Keys

| Key | What it does |
| --- | --- |
| Tab / Shift+Tab | next / previous piece |
| Arrows | shift left/right (X) and up/down (Y) |
| Page Up / Page Down | shift back/forward (Z) |
| Shift + Left/Right | turn about the up axis |
| Shift + Up/Down | turn about the across axis (tip forward/back) |
| Shift + Page Up/Down | turn about the front-to-back axis (tilt left/right) |
| Ctrl + Left/Right, Up/Down, Page Up/Down | stretch or squash along X, Y, Z (wider, longer, deeper) |
| + / − | size, all axes together |
| [ / ] | halve / double every step (starts at 5 mm, 5°, 0.02) |
| R | reset the piece |
| C | next character |
| O | next outfit |
| Space | whole body / close on the piece |
| right-drag, wheel | orbit and zoom |
| Ctrl+S | save every piece's fit for this character |

## How to fit

1. Pick the character with C and an outfit with O that has the piece you want.
2. Tab to the piece. Turn it first (Shift), then shift it until it sits on the body, then stretch it with
   Ctrl if it is too tight or too loose (a sleeve through the arm, trousers through the thigh).
3. Orbit round it (right-drag) to check that nothing pokes through from the side or the back.
4. Ctrl+S before changing the character or the outfit. C and O rebuild the scene from the saved file,
   so unsaved fits are lost. Fits are kept per character and per piece, so one character's fit
   doesn't move another's.

# Fitting in the editor, and backpacks

Use **debug/ClothingFitting.tscn** to adjust an existing piece for one character in the Godot editor.
Use **debug/TestBackpackAttachment.tscn** with F6 to test the pack during gameplay. The editor
fitting scene is a static preview; F6 does not provide editable gizmos or playable movement.

## Editor workflow

1. Build the C# project, then open **debug/ClothingFitting.tscn**.
2. Select the root **ClothingFitting** node. Set **CharacterId** and **Outfit** in the Inspector.
   Outfit is empty by default, showing the base shirt, trousers, boots and cosmetic pack. To fit
   another piece, add item ids such as `trail_shirt` and `trail_backpack`, one per equipment slot.
3. Click the Inspector's **Rebuild** button after changing the character or outfit.
4. Expand **Fits** in the Scene tree. Expand a locked `*_pivot` and select its child **Marker3D**.
   Edit the marker, leaving the pivot in place. Gloves may have separate Left and Right markers.
5. Use **W** to move, **E** to rotate and **R** to scale the marker, or enter its Transform in the
   Inspector. Values describe a correction around the piece's centre: X right, Y up, Z back,
   distances in metres and rotations in degrees. Keep scale positive and above zero.
6. Click **Save**, or save the scene with **Ctrl+S**. This writes the character/piece corrections
   to **player/clothing_fit.tres**. It leaves imported meshes and their original files intact.
7. Test the character in the backpack lab or full game. New visuals apply saved fits when built,
   including equipment replacement and character changes. Restart a running game after editing
   the resource externally; an existing visual does not poll the file for changes.

**Reset all handles** returns the current markers to identity; click Save to keep that reset.
Rebuild reloads saved values and discards unsaved marker edits. Always Rebuild after changing
CharacterId or Outfit, before saving, so the handles belong to the selected character and outfit.

## Backpack fit

The same native upper-spine attachment is used in the fitting scene and in gameplay. Automatic
fitting measures the torso and positions the pack first. The marker's saved adjustment is then
applied around that unadjusted pack centre. Explicit **Refit** retains that adjustment, replacing
the automatic placement without accumulating the saved correction. The original model root
transform remains below the pack pivot.

`base_pack` identifies the cosmetic pack shown with no Back-slot item. An equipped pack uses its
item id, such as `trail_backpack`. These have independent fits for each CharacterId. Unequipping
an item restores the base pack and its saved fit; the visual change does not resize inventory.
See [backpack-attachment.md](backpack-attachment.md) for offsets, diagnostics and runtime controls.

The fitting preview uses its chosen outfit independently of live equipment events. It neither
equips actual inventory items nor changes ownership, durability, storage or movement statistics.

## Files and limits

Claude's fitting files were copied from the release worktree into the active project, then
reconciled with the backpack system. The source worktree was left unchanged.

- Copied: `player/ClothingFit.cs`, `ClothingFitEntry.cs`, `ClothingFitPart.cs`, `ClothingFitter.cs`,
  `ClothingMeshFit.cs`; `debug/ClothingFitting.cs/.tscn`; `debug/tests/ClothingFitTests.cs/.tscn`;
  accompanying script UIDs. Source hashes are recorded in
  `D:/Game dev/math-audit-artifacts/clothing-fit-source-manifest.json`.
- Integrated: `player/BackpackAttachment.cs`, `ClothingVisualController.cs`, `CarrierWornGear.cs`
  and the CharacterId passed by `CarrierModel.cs`. Resource classes in `core/CarrierCharacter.cs`,
  `CarrierRoster.cs`, `CharacterStats.cs`, `inventory/ItemDefinition.cs` and `ClothingVisual.cs`
  have Tool annotations for editor use.
- Authored on Save: `player/clothing_fit.tres`. Automated tests must use a separate `user://`
  resource path instead of overwriting this file.

This adjusts existing compatible garments and rigid equipment. It does not skin arbitrary GLBs,
retarget incompatible rigs, create production clothing art or simulate cloth. Some current pieces
remain prototype geometry. Inspect extreme bends, shoulder clearance and animated poses in the
runtime lab before keeping a fit. No animation, inventory, survival or movement maths is replaced.

## Validation

The combined implementation builds with zero warnings and errors. ClothingFitTests passes 18
checks, including isolated preview events, resource round-trip, repeated refitting with rotation
and scale, base-pack restoration and character isolation. BackpackAttachmentTests passes 45
checks; DevPlaygroundTests passes 32 with the new backpack lab entry. Native base-outfit and
side pack views were rendered and inspected; capture ran every five frames. This does not prove
absence of clipping in every pose. The earlier combined smoke run passed 51 test scenes / 691 checks,
including TerrainTests. DescentPlaytestTests failed the smoke rule because Godot logged an
uninitialized GC handle during PackedScene.Instantiate at `environment/SetPieceNode.cs:30`.
Its gameplay assertion alone passed; this does not override the engine error. The full project
is not marked green and no commit was made. Log: `D:/Game dev/math-audit-artifacts/clothing-fit-combined-smoke.log`;
individual logs: `C:/Users/user/AppData/Local/Temp/tmp.npz5QJUzHS`.

After the streaming lifetime and terrain allocation changes, the readiness run passed all
**53 scenes / 715 checks** (`plant-optimized-full-smoke.log`) without that engine error.
See [terrain-streaming-performance.md](terrain-streaming-performance.md) for the measured
performance changes. This later run supersedes the earlier failing-suite status.
