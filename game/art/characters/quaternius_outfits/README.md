# Quaternius modular outfits (fantasy)

From Quaternius' *Modular Character Outfits – Fantasy* (Standard, free, CC0 — see LICENSE.txt).
Only the male Peasant and Ranger are kept, their textures cut from 4096 to 1024 px. They're rigged to the same 65-bone skeleton as the base
body in `../quaternius_ubc` (made for the "Regular" body, so a few rests sit 2–3 cm apart; the items
allow that with `RestTolerance`, and the garment's bones follow ours).

Worn by the modular carrier as fitted variants of items (`ClothingVariants` in the item's .tres):

| Item | Meshes | Body skin hidden under it |
| --- | --- | --- |
| trail_shirt | Male_Peasant_Body, Male_Peasant_Arms | torso, arms, hands |
| wool_trousers | Male_Peasant_Legs | pelvis, thighs |
| hobnail_boots | Male_Ranger_Feet_Boots | calves, feet |
| ranger_jerkin (new) | Male_Ranger_Body, Arms, Arms_Bracer, Body_Belt_1, Acc_Pauldron | torso, arms, hands; hides the shirt |
| ranger_breeches (new) | Male_Ranger_Legs | pelvis, thighs |
| ranger_hood (new) | Male_Ranger_Head_Hood | — |

The sleeves' bare hands keep their own skin material (`KeepMaterials = MI_Regular_Male`); the cloth
gets the condition shader with its own texture, so wet, dirt and wear still show. Other characters
wear the items' general (prototype) look, or nothing for the ranger pieces.
Test: `godot --headless --path . --fixed-fps 60 res://debug/tests/ModelledClothingTests.tscn`.
