# debug

The current main game entry point is `TestTerrain.tscn`. Other scenes provide focused tuning,
the free-fly camera, overlays (COM, foot targets, forces) and automated checks in `tests/`.

**Start developer testing with `DevPlayground.tscn` (F6).** One window connects 17 manual labs,
an editable movement/packing course, item spawning, stat tools, weather and station checkpoints.
**F10** opens the developer menu in any lab. [Developer guide](../docs/dev-playground.md).

New to the editor? Open **`LearnGodot.tscn`**, press **F6**, and follow
[the project tutorial](../docs/getting-started-godot.md). Its ground and obstacles are normal
editable nodes; its movement settings are local to the practice scene.

**`TestClothing.tscn`** is the equipment workshop: garment toggles, Carrier/Vučić preview,
condition/colour changes and explicit carried-load save/load. See [the clothing guide](../docs/modular-clothing.md).
**F9** enables/disables viewport recording every five rendered frames (off by default).

**`TestBackpackAttachment.tscn`** adds live pack fitting controls to the equipment workshop:
actual attachment bone, body clearance, offsets/rotation/scale and character switching.
See [the attachment guide](../docs/backpack-attachment.md).

**`ClothingFitting.tscn`** is the editor fitting scene: choose the root character/outfit, press
**Rebuild**, move the Marker3D handles under **Fits**, then **Save**. Adjustments are stored per
character and piece and also survive automatic backpack refitting.
See [the fitting workflow](../docs/clothing-fitting.md). F6 shows the static preview; use editor gizmos to edit.

**`TestCharacterAbilities.tscn`** compares origin stats, health/stamina capacities and abilities.
Choose a character, spend stamina and press **H**; the parchment HUD shows active time/cooldown.
See [the character stats and editing guide](../docs/character-abilities.md).

**`TestSmoothLocomotion.tscn`** previews walk, jog, fast run, turns, stopping and jump/landing
with the actual player and character outfits. Click **Replay full demo** or use manual controls.
See [animation assets and tuning](../docs/smooth-locomotion.md).
