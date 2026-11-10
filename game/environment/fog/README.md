# Ground fog

A shallow layer of fog lying on the mountain. It pours downhill and pools in hollows. It parts
around trunks and rocks, gets shoved aside by the carrier, and closes again behind them. It
churns harder at a sprint, swirls under each footfall, and keeps drifting when nothing touches it.

## Files

| File | What it is |
| --- | --- |
| `GroundFogSimulation.cs` | The fluid itself, pure C# (no scene tree), on a 128×128 patch of 0.75 m cells around the carrier. Per cell it holds the ground height, whether the cell is blocked, the density, and the flow. |
| `PhysicsFogGround.cs` | Finds the ground under a cell with a physics ray. The ray passes through anything that isn't terrain. A small sphere query then marks blocked cells (trunks, rocks, walls). |
| `GroundFogRenderer.cs` + `ground_fog.gdshader` | Draws the patch by ray-marching a box around it. |
| `GroundFog.cs` | The node. It follows the carrier and samples the ground (nearest cells first, a budget per physics frame). It steps the simulation at a fixed 30 Hz and uploads each step for the shader. It also registers `/fog` in the console. |
| `GroundFogDebugView.cs` | Map overlay and 3D sampling crosses. |
| `GroundFogSettings.cs`, `default_ground_fog.tres` | Every tunable. |

## How it works

### Simulation (each fixed step)

1. **Forces.** The fog runs down the slope of its own surface (ground + depth × density). This makes it pour downhill, fill hollows and spread out of heaps. A divergence-free swirl field, worked out in world space, keeps still fog moving.
2. **Drag**, and no flow into blocked cells.
3. **Semi-Lagrangian advection** of density and flow. Each cell is traced back along the flow and sampled bilinearly over open cells only. Density is compressed where the flow converges.
4. **Diffusion** of density and flow.
5. **Recovery.** Each cell eases back toward its settled density over `RecoveryTime`. Settled density is `Ambient`, raised in hollows by how far the cell lies below the ground around it.

Rows run in parallel.

### Following the carrier

The patch is aligned to whole world cells. When the carrier is more than `ShiftCells` from its middle, it shifts by whole cells and keeps everything in the overlap, so nothing snaps or pops. New cells arrive as settled fog over unknown ground. They are sampled nearest-first, and are drawn only once their ground is known. They also sit in the faded edge of the patch, so they don't pop in.

### What the carrier does

- **Body.** A bubble around the feet is cleared and shoved outward. It is wider at a sprint, and the fog is dragged along a little.
- **Wake.** Two eddies peel off behind, with random churn added at a sprint.
- **Footfalls.** Each footfall (`GameEvents.FootPlacementUpdated`, the moment a foot plants) adds a whirl turning the foot's way, plus a puff outward. A foot high above the fog stirs nothing.

### Drawing

The box spans the patch, from just under its lowest ground to above its tallest possible fog. It uses `cull_front`, so it works with the camera inside or outside the box.

- **Ray length.** A ray runs from where it enters the box to the nearest of three limits: the far side of the box, the scene's depth (so trees and the carrier stand in it softly), and `MaxDistance`.
- **Stepping.** Over open air the ray strides toward the layer. Inside the layer it takes fine steps, which grow with distance.
- **Lighting.** Light is accumulated and absorbed by Beer–Lambert.
- **Shape.** The fog's depth is its density × `Depth`. Its top is ragged by 3D noise carried along the flow, using two flow-map phases so the wisps never stretch.
- **Blending.** The two newest simulation steps are blended at the frame rate, so the fog moves smoothly between 30 Hz steps.
- **Banding.** A fixed per-pixel jitter (interleaved gradient noise) removes banding without flicker.
- **Colour** follows `WorldClock.Daylight`.

## Integration

It is already in `debug/TestTerrain.tscn` as the `GroundFog` node. To add it to another scene with terrain:

1. Add a `Node3D` with `environment/fog/GroundFog.cs`, and set `Settings` to `default_ground_fog.tres` (or a copy).
2. Make sure the ground counts as ground. Height-map shapes (the terrain's chunks) are recognised automatically. Any other ground body needs the group `fog_ground`.
3. Obstacles need to be on `GroundMask` (layer 1).

It needs nothing else. It reads `GameEvents.LocomotionUpdated` and `FootPlacementUpdated`, and nothing reads it. Without a carrier it follows its `Target` node.

The controller, IK, terrain and camera are untouched. The Low quality preset turns the fog off (`OffOnLowQuality`).

## Tuning and debugging

In game, open the console (backtick or F1):

- `/fog on`, `/fog off`
- `/fog debug density`, `/fog debug flow`, `/fog debug ground`, `/fog debug off`. These show the map overlay and tint the fog the same way. `ground` also draws crosses on the sampled ground (green), blocked cells (red) and cells not found yet (yellow).
- `/fog set <setting> <value>` changes any setting live (e.g. `/fog set Depth 1.2`, `/fog set WakeStrength 2`). `/fog get <setting>` reads one, and `/fog settings` lists them all.
- `/fog stats` shows step, upload and sampling times, how much ground is known, and the density under the carrier.

The main settings are:

- `Depth` and `Ambient`: how deep the fog is.
- `Density`: how thick it looks.
- `Speed`: how fast it runs downhill.
- `Turbulence`: how much it churns.
- `WakeStrength`: how hard the carrier ploughs it.
- `RecoveryTime`: how fast it closes.
- `NoiseSize` / `NoiseAmount`: wisps.

## Lab and tests

- `debug/GroundFogLab.tscn` (F6) is a slope with a hollow, a gully, trunks and rocks. A walker goes round a loop through them.
  - Keys: F fog on/off, 1–4 debug view, Space sprint, P pause, right-drag orbit, wheel zoom.
  - Command-line options after `--`: `--fog=Setting:value`, `--no-fog`, `--view=N`, `--pitch=`, `--distance=`.
- `debug/tests/GroundFogTests.tscn` (headless, 26 checks):
  - flows downhill, pools in a hollow
  - nothing enters blocked cells, parts around them and closes behind
  - bubble clears and recovers, sprint wake is bigger than walk
  - footfall whirls turn opposite ways
  - still fog drifts
  - shift keeps the overlap
  - 50 s of hard use stays finite and within limits
  - the physics sampler (through a rock, a trunk blocks)
  - the node learns its patch, follows a walker (shifting), clears around it, uploads, and `/fog` works

## Performance

Measured on 2026-11-10 on a laptop with an RTX 3060 Laptop GPU and a 16-thread CPU, Debug build.

| | |
| --- | --- |
| Simulation step, 128×128 (parallel rows) | 1.24 ms mean, 1.8 ms worst; at 30 steps/s about 4% of one core |
| Upload (two float textures) | 0.1–0.2 ms per step |
| Ground sampling, 64×64 patch from nothing | 4096 cells in 75 ms (spread over frames) |
| GPU at 1920×1080, low camera looking across the fog | 7.62 ms/frame with fog vs 6.07 ms without: **about 1.5 ms** |
| GPU at 1920×1080, camera looking down onto it | no measurable difference |

Run the benchmark yourself:

```
godot --path . res://debug/GroundFogLab.tscn --resolution 1920x1080 -- --fog-bench
```

Raise `GridSize` only after benchmarking. The step cost grows with the number of cells (×4 for 256²). The shader cost depends on `Steps`, `FineStep` and `DistanceStep`, not on the grid.

## Limits

- The grid covers 96 m around the carrier. Beyond that the fog fades out at the patch's edge, where the scene's own distance fog takes over.
- Blocked cells are learnt once per cell, so a moving cart doesn't part the fog.
- The fog is a height field: it has no overhangs and doesn't flow under bridges.
