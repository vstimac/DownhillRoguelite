# Travellers with their own model

Travellers at a station's fire (`narrative/NpcFigure.cs`) are a cloaked figure built from simple
shapes, unless their persona names a model. A traveller with a model sits on a log by the fire:
a hidden mannequin rig (`art/characters/quaternius_ual/UAL_Mannequin.glb`) plays `Sitting_Idle`,
and the model follows it bone for bone through `RigFollower`, the same way the carrier's character
does. Pressing E to talk plays `Sitting_Talking` for `TalkSeconds`, then they settle back.

First one: **Mr. Hollis** (`narrative/npcs/broker.tres`), the businessman in a suit.

## Adding one

1. **Model**: a rigged `.glb` with Mixamo bone names (any prefix: `mixamorig:`, `mixamorig1_`, ...),
   resting in a T- or A-pose. It needs no animations of its own. Generated models (Tripo, Mixamo
   auto-rig) work as they come.
2. **Shrink the textures** (generated models often ship 4K-8K maps; an NPC needs 1K):

   ```
   godot --headless -s tools/models/shrink_glb_textures.gd -- <in.glb> art/characters/npcs/<id>/<id>.glb 1024
   ```

   Only the image buffers change; mesh, skin and bones are copied exactly.
3. **Persona**: copy `narrative/npcs/broker.tres` and set `ModelPath` and `ModelScale`
   (Tripo exports come in about 1 m tall; 1.85 makes a 1.8 m person). `MinCircle` says from which
   circle on they can appear (0 = Limbo, from the first station).
4. **Main scene**: add the persona to `Personas` on the `StationVisitors` node in
   `debug/TestTerrain.tscn`.
5. **Check**: open `debug/TestNpcModels.tscn` and press F6 (add the persona to the `Lab` node's
   `Personas`). Run with `-- --talk` to see them talk, `-- --rest` to see the model's own rest pose
   (tells a model fault from a rig fault), `-- --probe` to print the spine, neck and head bones.
   `debug/tests/NpcModelTests.tscn` checks height, intact bones, sitting and talking headlessly.

Tunables on `NpcFigure`: `RigPath`, `IdleClip`, `TalkClip`, `TalkSeconds`, and the log
(`SeatPosition`, `SeatRadius`, `SeatLength`).

The playable character is a different system (character creation, clothing and gear on the
carrier); this page is only about travellers.
