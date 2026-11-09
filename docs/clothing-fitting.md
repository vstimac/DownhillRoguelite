# Fitting clothes by keys

Every worn piece (hat, shirt, sleeves, trousers, gloves, boots, pack…) can be moved, turned and resized on
each character by hand. The fits are saved in `player/clothing_fit.tres` and the game applies them as the
piece is put on. There are two ways to fit: in the editor with the gizmo (`debug/ClothingFitting.tscn`),
or run as a game from the keyboard, like the sword fitting (this page).

## Open it

```
godot --path "D:\Game dev\release-wt" res://debug/ClothingFittingPlay.tscn
```

(or open `debug/ClothingFittingPlay.tscn` in the editor and press F6). `--path` must point at the checkout
that has the clothing work. Run from any other folder and Godot opens a different project, which has no
fitting scene, and it closes at once.

The character stands in its rest pose wearing the outfit. A yellow dot marks the piece being fitted.

## Keys

| Key | What it does |
| --- | --- |
| Tab / Shift+Tab | next / previous piece |
| Arrows | shift left/right (X) and up/down (Y) |
| Page Up / Page Down | shift back/forward (Z) |
| Shift + Left/Right | turn about the up axis |
| Shift + Up/Down | turn about the across axis (tip forward/back) |
| Shift + Page Up/Down | turn about the front-to-back axis (tilt left/right) |
| Ctrl + Left/Right, Up/Down, Page Up/Down | stretch or squash along X, Y, Z (wider, longer, deeper) |
| + / − | size, all axes together |
| [ / ] | halve / double every step (starts at 5 mm, 5°, 0.02) |
| R | reset the piece |
| C | next character |
| O | next outfit |
| Space | whole body / close on the piece |
| right-drag, wheel | orbit and zoom |
| Ctrl+S | save every piece's fit for this character |

## How to fit

1. Pick the character with C and an outfit with O that has the piece you want.
2. Tab to the piece. Turn it first (Shift), then shift it until it sits on the body, then stretch it with
   Ctrl if it is too tight or too loose (a sleeve through the arm, trousers through the thigh).
3. Orbit round it (right-drag) to check that nothing pokes through from the side or the back.
4. Ctrl+S before changing the character or the outfit. C and O rebuild the scene from the saved file,
   so unsaved fits are lost. Fits are kept per character and per piece, so one character's fit
   doesn't move another's.
