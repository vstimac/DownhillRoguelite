# Your first edits in this Godot project

Start with **`debug/LearnGodot.tscn`** for learning and **`debug/TestTerrain.tscn`** for the full game.
The practice scene has the real player, a floor and two editable obstacles. Its movement settings
are local to that scene, so your practice speed changes do not change the main game's settings.

## 1. Open the right project

1. Launch the existing **Godot .NET** editor in `D:\Game dev\Godot_v4.7.2-stable_mono_win64`.
   Use the `.exe` without `_console` for normal editing. This project uses C#.
2. In the Project Manager, click **Import** and choose
   `D:\Game dev\downhill-roguelite-(copy)\project.godot`, then open the project.
3. Let the first import finish. If C# properties are missing or scripts need compiling, use
   Godot's **Build** button. The terminal equivalent, from this project folder, is
   `dotnet build`.
4. In the **FileSystem** dock, find `debug/LearnGodot.tscn` and double-click it.
5. Press **F6** to play this scene. Walk with **WASD**, look with the mouse, jump with
   **Space**, and brake with **C**. **Escape** releases the mouse; click to capture it again.
   Press **F8** to stop. If you walk off the floor, stop and run again.

**F5 always starts the full game. F6 starts the scene you currently have open.**
Laptop keyboards may need the Fn key. These are the default shortcuts; the top-right play/stop
buttons work too. See Godot's [scene basics](https://docs.godotengine.org/en/stable/getting_started/step_by_step/nodes_and_scenes.html)
and [running the current scene](https://docs.godotengine.org/en/stable/getting_started/step_by_step/scripting_first_script.html).

## 2. Know the four things you are looking at

| Part of the editor | What you use it for |
| --- | --- |
| FileSystem dock | Find files: scenes, models, scripts and settings. |
| Scene dock | Select objects in the open scene: Player, Ground, BlueStep, OrangeBlock. |
| 3D viewport | See, select and move those objects. |
| Inspector | Change the selected object's position, shape, colour or gameplay values. |

A **node** is one object or component. A **scene** (`.tscn`) is a saved tree of nodes.
A **resource** (`.tres`) stores settings or content. A **script** (`.cs`) defines behaviour.
For the editor layout, see [Godot's illustrated interface guide](https://docs.godotengine.org/en/stable/getting_started/introduction/first_look_at_the_editor.html).

## 3. First exercise: move a block

1. Stop the game with **F8**.
2. Select **OrangeBlock** in the Scene dock. Select the parent, not its Mesh or Collision child.
3. In the Inspector, expand **Transform → Position** and change **X** from `5` to `-5`.
4. Press **Ctrl+S**, then **F6**. The block is now on the other side.
5. Stop, select OrangeBlock again, and press **Ctrl+D** to duplicate it. Move the copy by
   changing its Position. You have added an obstacle with working collision.

Positions are in metres: X is left/right, Y is height, Z is forward/back.
The mountain descends toward **negative Z**. Selecting a node and pressing **F** in the 3D
viewport focuses the editor camera on it.

To change colour, expand **OrangeBlock → Mesh**, click its **Mesh** resource, then its
**Material**, and edit **Albedo → Color**. Duplicates initially share mesh/material resources:
use the resource's dropdown **Make Unique** before giving only one copy a different colour.
To resize an obstacle, edit both **Mesh → Mesh → Size** and **Collision → Shape → Size** to
the same dimensions. Avoid scaling the physics body as your first resizing method.

Changes in the **Remote** scene tree while playing are temporary. Make lasting edits in the
**Local** scene tree after stopping. Save the scene before testing again.

## 4. Second exercise: change walking speed without code

1. In `LearnGodot.tscn`, select **Player**.
2. In the Inspector, click/expand its **Settings** resource.
3. Under **Speed**, change **Stride Speed** from `3.5` to `5`.
4. Save and press **F6**. Compare walking, then restore `3.5` if you prefer it.

This is a local practice resource. For the actual game's movement, open
`player/default_locomotion.tres`. That shared file affects scenes using it. Useful starting
values include Stride Speed, Acceleration, Deceleration and Max Walkable Angle.
Change one value at a time. The Inspector's revert arrow restores a property's default.

In C#, `[Export]` is what makes these properties editable in the Inspector; rebuild after adding
new exported properties. See [C# exported properties](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_exports.html).

## 5. Which scene or file should I open?

| I want to work on… | Open this | First place to look |
| --- | --- | --- |
| My first level/props | `debug/LearnGodot.tscn` | Ground, BlueStep, OrangeBlock; use Save Scene As for your own level. |
| The full game | `debug/TestTerrain.tscn` | Terrain, Player, Stations, Weather, Supplies, Contracts, Hud. |
| Movement on slopes/stairs | `debug/TestLocomotion.tscn` | Course node's exported values; Player. |
| Inventory/packing | `debug/TestPacking.tscn` | Player/Inventory and Packing. |
| Clothing and equipment | `debug/TestClothing.tscn` | Live garment buttons, condition, save/load and character switch. |
| Player body/camera | `player/Player.tscn` | Collision, CameraRig, Stamina, Needs, Balance. Test it inside a level. |
| Walking/turning feel | `player/default_locomotion.tres` | Speed, acceleration, turning and ground settings. |
| Mountain shape | `terrain/default_terrain.tres` | TerrainSettings values; test with F5. |
| Food, water, clothes | `inventory/items/*.tres` | Display Name, Mass Kg, Size Cells, Nutrition/Hydration, clothing stats. |
| Backpack dimensions | `inventory/default_backpack.tres` | Grid Size and Cell Size. |
| Weather | `environment/weather/default_weather.tres` | Weather table. |
| Job text/reward | `progression/contracts/resources/*.tres` | Offer Text, Score, Cargo Item Path, Stations Ahead. |
| NPC dialogue/content | `narrative/npcs/*.tres` | Each NPC's resource fields. |
| Character presets/appearance | F5 → Character; `world_ui/CharacterPage.cs` | Presets, base shirt/pack colours and portrait rotation. |
| HUD and menu layout | `ui/Hud.cs`, `ui/screens/`, `ui/hud/` | Most UI is built in C# when the game runs. |

`TestTerrain` is the real entry scene despite its name. Its mountain is generated at runtime,
so you will not see the whole mountain laid out for hand-editing in the editor. Select the
**Terrain** node for its **Settings** and ground appearance controls. Stop before changing
terrain settings; generation also reads them on a background thread. For repeatable terrain,
turn **Random Seed On Start** off on Terrain and use the settings' **Default Seed**. Turn it
back on when you want random mountains again.

The UI and many placed world objects also appear at runtime. An empty-looking node in the
editor can still create a large part of the game. `character.tscn` and `control.tscn` are draft
files in the working folder; the main game currently instances **`player/Player.tscn`**.

## 6. Make your own food item

1. In FileSystem, right-click `inventory/items/ration.tres` and duplicate it as
   `my_snack.tres`.
2. Select the copy. Set **Item Id** to `my_snack`, **Display Name** to `My Snack`,
   **Nutrition** to `20`, and **Mass Kg** to `0.2`. Keep each Size Cells dimension positive.
3. Save. A new resource alone does not make it spawn.
4. To test it, open `debug/TestPacking.tscn`, select **Player → Inventory**, and add
   `res://inventory/items/my_snack.tres` to **Starting Item Paths**. Save, then **F6**.
5. To make it appear among normal world supplies, add its path to **Item Paths** in
   `inventory/world/default_supplies.tres`; add matching entries in **Weights** and
   **Min Depths** at the same index. Larger weights make an item more likely relative to
   the other entries. Use `0` minimum depth for early appearances.

Make a new file by duplicating a similar resource, give it a unique ID, then register it in the
table/scene that uses it. The same general pattern applies to clothing and contracts. Inventory
design details live in [inventory-design.md](inventory-design.md).

## 7. A comfortable daily workflow

1. Stay on a working branch. The current audit branch is **`fix/gameplay-math-audit`**;
   main has not been edited. Save your own experiments on a separate branch when you are ready.
   There are already unrelated uncommitted changes here, so review selected files when committing.
2. For your first level, use **Scene → Save Scene As** to save a copy of LearnGodot as
   `debug/MyFirstLevel.tscn`.
3. Make one small edit, save, run with F6, stop with F8, and compare.
4. Use F5 periodically to check the full game.
5. For C# behaviour, open the project in VS Code or Visual Studio, edit `.cs`, save, build,
   and rerun. Godot's built-in C# editing is limited; see [C# basics](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html).
6. Check the **Output** and **Debugger** panels if something breaks. Fix the first red error.

Keep `.godot/` as generated editor data, and keep third-party addon files in `addons/` separate
from your game edits. If you cannot see a custom property, build C# and reselect the node/resource.
If you ran the wrong scene, stop and use F6 on your desired scene tab. If editing a model import
does not stick, instance it in a `.tscn` scene and make your changes around that instance.

Your first ten minutes: **open LearnGodot → move OrangeBlock → duplicate it → change its
colour → change Player's Stride Speed → F6 → walk around what you made.**
