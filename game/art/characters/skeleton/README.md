# Skeleton

The skeleton who sits on the bench beside the summit's Reno 4 with a rose in his hand
(environment/SummitRadio.cs). Supplied by the director as `Skeleton.zip` (2026-11-10): an
anatomical human skeleton (`SM_Human_Skeleton.fbx`, one object per bone, no rig, 1.5 M triangles,
textures not included). Converted in Blender 5.1 to `skeleton_sitting.glb`:

- only the assembled skeleton kept (the file also holds a laid-out set of loose bones);
- the mirrored limbs split into left and right, then posed sitting by turning each part about its
  joint: thighs forward, shins down, a slight lean back, the right forearm raised before the chest
  (it holds the rose), the left hand on the thigh, the head tilted towards the rose;
- life size (1.78 m standing), the origin at the seat (where the pelvis rests), facing +Z;
- merged into three meshes and decimated to 36k triangles, one bone-coloured material;
- nodes for the game: `torso` (sways to the music), `head` under it (nods), `hand_r` (the right
  palm, where the rose goes).

The originals are not in git.
