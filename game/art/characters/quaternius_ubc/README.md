# Quaternius Universal Base Characters (modular carrier)

`Superhero_Male_FullBody.gltf` from Quaternius' *Universal Base Characters [Standard]* (CC0, see
LICENSE.txt), the "Godot - UE" export: about 14k triangles, Unreal bone names (pelvis, spine_01..03,
neck_01, Head, clavicle/upperarm/lowerarm/hand, thigh/calf/foot/ball, fingers). Texture paths in the
.gltf were fixed to the shipped file names; textures import at 1024 px at most (phones).

- `modular.tres` is the roster entry ("The Carrier (modular)", id `modular`), the default character.
- RigFollower.UseRigOf maps the Unreal names onto the mannequin's Rigify (DEF-*) bones, so every
  animation, foot IK, balance lean and the ragdoll drive this body unchanged.
- The body ships in underwear. ClothingVisualController cuts a shirt, trousers and boots from the body
  itself (player/BodyGarment.cs: the triangles the torso, leg or foot bones drive, pushed out a few
  millimetres), so they fit and bend exactly. Equipped torso/legs/feet clothing hides the matching one.
- Other base characters from the same pack (female, other body types), and Quaternius' outfit packs,
  use the same skeleton: copy the .gltf + textures here and add a roster entry.
