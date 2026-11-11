# Fitting the torch

The lit torch sits in the carrier's left hand and, when put away (X in game), is tucked in the belt
on the right hip. Where it sits is saved in `player/torch_fit.tres` (a `TorchFit`), which
`CarrierTorch` loads. With no file the defaults in `player/TorchFit.cs` are used.

## Open the fitting scene

```
godot --path "D:\Game dev\release-wt\game" res://debug/TorchFitting.tscn
```

(or open `debug/TorchFitting.tscn` in the editor and press F6). The carrier stands on a flat floor,
frozen in his idle pose, with the torch lit in his left hand.

## Keys

| Key | What it does |
| --- | --- |
| Tab | next part: lit torch in the hand → put-away torch at the belt |
| Arrows | shift left/right (X) and up/down (Y, along the haft) |
| Page Up / Page Down | shift forward/back (Z) |
| Shift + Left/Right | turn about the haft (Y) |
| Shift + Up/Down | turn about X (top toward/away from him) |
| Shift + Page Up/Down | turn about Z (top left/right) |
| [ / ] | halve / double the step (starts at 1 cm and 5°) |
| + / − | size of the torch |
| Ctrl + Up / Down | carry pose: raise / lower the left arm |
| Ctrl + Left / Right | carry pose: bend / straighten the elbow |
| Ctrl + Page Up / Down | carry pose: tuck the arm in toward the chest / out |
| U | in the hand: upright (the flame never tips, only the turn above counts) or turning with the hand |
| 1–6 | pose: idle, walk, jog, sprint, crouch, jump |
| , / . | scrub the pose back / forward; Space plays or freezes it |
| R | reset this part |
| Right-drag, wheel | orbit and zoom |
| Ctrl+S | save to `player/torch_fit.tres` |

## The carry pose

While the torch burns in the hand, `player/TorchArm` (a modifier on the driver skeleton, so the shown
character, the torch and co-op views follow it) eases the left arm up: the upper arm swings forward
by **ArmRaise**, tucks in by **ArmTuck** and the elbow bends by **ElbowBend** (degrees, in the
carrier's own frame). It eases out when the torch is put away or burns out, and while the sword is
drawn (the sword and shield need the arm).

## Frames

- **In the hand.** The shift is in the hand bone's frame (so the grip follows the fist). Upright, the
  turn is in the carrier's frame (X to his right, Y up, Z behind him); otherwise it is in the hand's.
- **At the belt.** The shift and turn are in the carrier's frame, from the hips bone, so it sits the
  same on every character.

`-- --fit-belt` starts on the belt and `-- --fit-yaw=degrees` turns the camera (for renders).
