# Fitting the sword and shield

MeleeCombat works out where the sword and shield sit from the rig's rest pose. That is close but not
exact, so on top of it come hand-tuned corrections, saved in `player/sword_fit.tres` (a `SwordFit`)
and loaded by the game.

## Open the fitting scene

```
godot --path . res://debug/SwordFitting.tscn
```

(or open `debug/SwordFitting.tscn` in the editor and press F6). The carrier stands on a flat floor with
the sword out, frozen in the guard pose.

## Keys

| Key | What it does |
| --- | --- |
| Tab | next part: sword in hand → sword on hip → shield |
| Arrows | shift left/right (X, across the guard) and up/down (Y, along the blade) |
| Page Up / Page Down | shift forward/back (Z, the blade's flat) |
| Shift + Left/Right | turn about the blade (Y) |
| Shift + Up/Down | turn about X (tip toward/away from you) |
| Shift + Page Up/Down | turn about Z (tip left/right) |
| [ / ] | halve / double the step (starts at 1 cm and 5°) |
| + / − | size of the sword (or shield) |
| 1–7 | pose: guard, shield up, blow 1, 2, 3, kick, draw |
| , / . | scrub through the pose; Space plays / freezes it |
| R | reset the part you are on |
| right-drag, wheel | orbit and zoom the camera (it follows the part) |
| Ctrl+S | save to `player/sword_fit.tres` |

## How to fit

1. **Sword in hand**, pose 1 (guard): first turn it until the blade points the way the hand holds it,
   then shift it until the grip sits in the closed fist, the guard just above the thumb and forefinger.
2. Check poses 3–5 (the blows) and 2 (shield up): scrub with , and . and look that the grip stays in
   the fist. Small corrections only, since one fit serves every pose.
3. **Sword on hip** (Tab): the walk pose; set the sheathed sword on the left hip, the grip forward and
   up, the blade down and back, not through the leg.
4. **Shield** (Tab): pose 2 (shield up) and 1 (guard); the shield's face away from the forearm, its
   middle on the back of the forearm.
5. Ctrl+S. The values are printed too; the file is what the game uses.
