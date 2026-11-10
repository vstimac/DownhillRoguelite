# Humanoid enemies

Two Tripo-generated, Tripo-rigged models supplied by the director (2026-11-10), FBX with embedded
textures (Godot extracts them next to each model):

- `brute/brute.fbx`: the bog ogre (wildlife/enemies/ogre.tres): green, heavy, about 0.91 m as
  imported, shown at 2.2 m.
- `packbeast/packbeast.fbx`: the pack fiend (wildlife/enemies/packbeast.tres): a backpack with
  teeth on legs, about 0.98 m as imported, shown at 1.9 m.

Both use Tripo's 78-bone humanoid rig (Hip, Waist, Spine01/02, L_/R_ Clavicle, Upperarm, Forearm,
Hand, Thigh, Calf, Foot, ToeBase, fingers, twist bones), rest facing +Z. They have no animations of
their own: wildlife/HumanoidEnemy.cs plays the Universal Animation Library's clips on a hidden
mannequin and RigFollower (TripoPairs) copies its pose onto the model. Any other Tripo-rigged humanoid
works the same way: add a model here and an EnemySpecies .tres.

## jade_glutton

`jade_glutton/jade_glutton.glb`: a jade pot-headed glutton in a red sash (Tripo model from the
director's concept sheet, same 78-bone Tripo rig; ModelHeight 0.91, faces +Z). It never just
arrives: using the "Grinning jade urn" item (stamina +20) has a 20 % chance to wake it beside the
nearest tree instead (`EnemyDirector.Wakeable`, `GameEvents.WakeRequested`), and it comes at once.
