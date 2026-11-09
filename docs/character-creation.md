# Character creation: implemented starter flow

The existing start-screen character page now provides a three-part character-creation layout:
origin preset, base outfit appearance, and an equipment-inspection preview. The design follows
the broad RPG pattern of choosing identity, inspecting a large 3D character and adjusting visible
appearance, while retaining this project's parchment UI and existing gameplay rules.

This is a functional starting point inspired by the requested BG3 creation flow, not a reproduction
of BG3's complete editor or assets.

## What works now

- F5 → **Character** opens **Create your carrier**.
- The available roster includes **Aleksandar Vučić** using the project's existing textured GLB.
  The original Carrier is also available when the local Mixamo asset is installed.
- Arrows, keyboard and controller select presets using the existing delayed apply behaviour.
- Shirt and pack colour palettes update the visible base outfit immediately without reloading
  the character or changing its physical weight/stats.
- Turn left/right orbit the portrait in 45-degree steps so the back-mounted pack can be inspected.
- A portrait-only fill light improves inspection without adding a permanent gameplay light.
- Confirm appearance keeps the choice and returns to the main menu; New Run starts the game.
- Character and base colours persist in the existing options file. Resetting gameplay options
  preserves these identity/appearance choices.
- Worn garment variants/tints remain per-copy equipment data, separate from the cosmetic base
  outfit. Use `debug/TestClothing.tscn` to equip garments and test their condition and persistence.

## Existing rig and skin policy

The mannequin skeleton remains the animation driver. Imported characters follow it through
RigFollower; clothing either uses a validated matching skin or is a measured skinned prototype.
Changing a texture resolution changes sampling/detail, not body shape, bone compatibility or
garment fit. Preserve UVs when replacing a texture; author a compatible garment variant when
geometry or rest pose differs. Untextured custom characters fall back to the dressed mannequin.

Rapid selection before a model's deferred rig setup completes is now handled: a removed model's
queued Setup exits before reading global transforms. The regression reproduced the previous
out-of-tree transform error and passes after this guard.

## Files and extension points

| Area | File |
| --- | --- |
| Preset catalog | `art/characters/roster.tres`, `core/CarrierCharacter.cs` |
| Vučić preset/model/texture | `art/characters/vucic/` |
| Creation page and input | `world_ui/CharacterPage.cs`, existing CharacterCarousel |
| Portrait orbit and fill | `world_ui/MenuCamera.cs`, StartScreen signal wiring |
| Saved base colours | `core/GameOptions.cs` |
| Live material updates | `player/ClothingVisualController.cs` |
| Worn clothing and snapshots | `inventory/CarrierEquipment.cs`, EquipmentPersistence |

## Asset work still required

Face/body morph sliders, interchangeable hair/facial parts, production garment topology and
character-specific fitted meshes need authored Blender assets and validated skinning. There are
no invented face-shape parameters or hidden unsupported race/class choices in this starter UI.
The procedural garments and pack are explicitly prototypes; the existing Vučić body is not
destructively partitioned to remove its suit. Fit/clipping on that unpartitioned body still needs
manual review before replacing the prototype with final clothing art.

Follow [the clothing pipeline](modular-clothing.md) for weight transfer, export/import, body
partitions and animation validation. These are the next asset steps for expanding the editor.
