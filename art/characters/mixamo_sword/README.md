# Mixamo sword-and-shield clips

Thirteen clips from Mixamo's "Sword and Shield" pack, made on the modular carrier (65-bone
`mixamorig` skeleton, T-pose, 30 fps, no skin). The director chose to keep them in git, unlike
`art/characters/mixamo/`. Adobe's Mixamo terms allow them inside the game; check them before
sharing the raw files anywhere else.

| File | Mixamo clip | Used for |
| --- | --- | --- |
| `sword_idle.fbx` | sword and shield idle | the guard (loops) |
| `sword_block_idle.fbx` | sword and shield block idle | holding the shield up (loops) |
| `sword_block.fbx` | sword and shield block | raising the shield |
| `sword_block_impact.fbx` | sword and shield impact | a blow taken on the shield |
| `sword_impact.fbx`, `sword_impact_2.fbx` | sword and shield impact (2), (3) | shoved while the sword is out |
| `sword_attack_1.fbx` | sword and shield attack (4) | first blow of the chain |
| `sword_attack_2.fbx` | sword and shield slash | second blow |
| `sword_attack_3.fbx` | sword and shield slash (3) | third blow |
| `sword_attack_4.fbx` | sword and shield slash (5) | spare blow |
| `sword_kick.fbx` | sword and shield kick | the kick |
| `sword_draw.fbx` | sheath sword 2 | drawing from the left hip |
| `sword_sheathe.fbx` | sheath sword 1 | putting it back |

How it's wired: `player/SwordAnimator.cs` loads the clips onto a hidden Mixamo skeleton, and
`player/MixamoOverlay.cs` lays its pose over the carrier's driving rig (the mannequin), the upper
body for swings and blocks, the legs too for the kick. `player/MeleeCombat.cs` runs the sword.
