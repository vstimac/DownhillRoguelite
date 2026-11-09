# debug

The current main game entry point is `TestTerrain.tscn`. Other scenes provide focused tuning,
the free-fly camera, overlays (COM, foot targets, forces) and automated checks in `tests/`.

New to the editor? Open **`LearnGodot.tscn`**, press **F6**, and follow
[the project tutorial](../docs/getting-started-godot.md). Its ground and obstacles are normal
editable nodes; its movement settings are local to the practice scene.

**`TestClothing.tscn`** is the equipment workshop: garment toggles, Carrier/Vučić preview,
condition/colour changes and explicit carried-load save/load. See [the clothing guide](../docs/modular-clothing.md).
Debug game runs record the viewport every five rendered frames; **F9** pauses/resumes recording.

**`TestCharacterAbilities.tscn`** compares origin stats, health/stamina capacities and abilities.
Choose a character, spend stamina and press **H**; the parchment HUD shows active time/cooldown.
See [the character stats and editing guide](../docs/character-abilities.md).

**`TestSmoothLocomotion.tscn`** previews walk, jog, fast run, turns, stopping and jump/landing
with the actual player and character outfits. Click **Replay full demo** or use manual controls.
See [animation assets and tuning](../docs/smooth-locomotion.md).
