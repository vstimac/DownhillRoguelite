# Mixamo character and animations (not in git)

The carrier is a Mixamo character ("Ch17": body, boots, helmet, vest, shirt, pants, hair) with Mixamo
animations. Adobe's Mixamo terms allow using them in the game (they ship inside the builds) but not
sharing the raw files, and this repository is public, so the files here are ignored by git:

| File | What it is |
| --- | --- |
| `carrier.fbx` | the character, T-pose, 65-bone `mixamorig1_` skeleton (from `character.fbx`) |
| `jump_over.fbx` | the "Jump Over" vault animation, same skeleton (2.25 s) |
| `*_N.png` | textures Godot unpacks from the FBX on import |

To set up a fresh clone: download the character and the "Jump Over" animation from
[mixamo.com](https://www.mixamo.com) (FBX Binary, With Skin), name them as above and put them here.
Without them the game still runs: `player/CarrierModel.cs` falls back to the Quaternius mannequin.

How it's wired: the mannequin's rig still drives everything (animations, foot IK, the balance lean,
the ragdoll); `player/RigFollower.cs` copies its pose onto this character every frame, bone for bone
(both rigs rest in a T-pose), and hides the mannequin's meshes. `CarrierModel` shows the helmet and the
vest when something is worn on the head or the torso.
