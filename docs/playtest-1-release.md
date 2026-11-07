**You carry a pack down a mountain that never ends.** Every run starts on a summit; death is permanent; the next life gets a new mountain. Below lie Dante's nine circles, and somewhere further down, maybe, godhood. Keep your balance, keep fed, reach the next fire before night.

This is the first open playtest. Thank you for playing. Everything below takes 5 minutes to read.

---

## 1. Get it running (5 minutes)

**Windows**
1. Download `DownhillRoguelite-playtest1-windows.zip` (below, under Assets) and unzip it anywhere.
2. Run `DownhillRoguelite.exe`. If Windows warns about an unknown app: More info → Run anyway.

**Linux (beta: not tested on real Linux yet, tell us how it goes)**
1. Download `DownhillRoguelite-linux-beta.tar.gz`.
2. `tar -xzf DownhillRoguelite-linux-beta.tar.gz && cd DownhillRoguelite-linux-beta`
3. `./DownhillRoguelite.x86_64` (if "permission denied": `chmod +x DownhillRoguelite.x86_64 race-same-mountain.sh`).
4. Needs Vulkan drivers (mesa-vulkan-drivers or the NVIDIA driver).

**You need:** a 64-bit PC with a graphics card that supports Vulkan (any recent NVIDIA, AMD or Intel). Headphones strongly recommended: the narrator, the weather and your footsteps carry a lot of the game.

**Optional: talking (2.1 GB).** The companion and the travellers you meet answer anything you type, using a small AI model that runs on your own PC (nothing goes online). It's too big for GitHub:
1. Download [qwen2.5-3b-instruct-q4_k_m.gguf](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf).
2. Make a folder called `Models` next to `DownhillRoguelite.exe` and put the file in it.

Without it everything else works: the companion stays quiet and travellers say a few lines of their own. The first answer takes 10 to 15 seconds (the model wakes up), later ones a few seconds. Skip this on a PC without a dedicated graphics card.

---

## 2. Tonight's race

Start the game with **`Race - same mountain.bat`** (Linux: `./race-same-mountain.sh`). Everyone gets **the same mountain**.

- **Your first life counts.** Deepest depth wins (shown top right while you play and on the screen when you die).
- After your first death the game gives you new mountains: play on as long as you like, just for fun.
- Pressing R gives up the run, so don't, unless you're stuck.

---

## 3. Your first ten minutes

- **Walk down.** The summit is the only way to start. Follow the cairns and the dotted paths on your map (M).
- **Stations** are fires with a lean-to, a beam of light you can see from far away, and graves. Reach the next one **before night**: nights are cold and dark. The compass top right points to it, and the map shows an orange arrow with the distance.
- **Your body:** four dials top left: health, hunger, thirst, stamina. Walking costs stamina; **stand still** to get it back. Eat (F) and drink (Q) before you need to.
- **Your pack:** a heavy or badly packed load tips you over. **Lean** against it with the mouse buttons; hold both to **brace** on steep ground. **C** takes careful steps on scree. At a station, **Tab** opens the pack: heavy things low and close to your back.
- **Below each station the path splits:** safe (cairns), hazardous (shorter, rougher), or supply-rich (longer, more to find). A lookout sign compares them.
- **At a station:** sleep at the bedroll (E, from 16:00), choose one of three **boons** (glowing stones), mend worn gear, and maybe meet a **traveller** by the fire (E to talk).
- **Things on the ground** glow with a grid; an arrow shows them when you're close. E picks them up. Clothes keep you warm and dry; materials mend things; paper and pencil let you draw on the map.

---

## 4. Controls

**Moving**
| | |
| --- | --- |
| W A S D | walk |
| Shift | sprint (costs stamina) |
| C | careful steps on steep or loose ground |
| Space | jump |
| Mouse | look |
| Left / right mouse button | lean left / right against the load |
| Both mouse buttons | brace |

**Using things**
| | |
| --- | --- |
| E | pick up · sleep · choose a boon · **talk to a traveller** |
| F / Q | eat / drink |
| 1–9 or mouse wheel | choose from the hotbar |
| V | use the chosen item (eat, drink, wear) |
| G | drop the chosen item |

**Pages**
| | |
| --- | --- |
| I | inventory: details, Eat / Drink / Wear / Mend / Drop, and your carrier's stats |
| M | map: **drag** to move it, **click** sets a waypoint, right-click removes one, wheel zooms, **Shift+drag** draws (needs paper and a pencil), **C** recentres, drag the corner to resize |
| J | journal: everything that happened, written down |
| Enter | talk with your companion (Enter sends, Esc closes) |
| Esc | pause, all controls, volume |

**At a station: repacking (Tab)**
| | |
| --- | --- |
| Left click | pick up an item / place it (green ghost fits, red doesn't) |
| Q / E | turn it |
| R / F | tip it |
| Right click | put it back on the mat |
| Tab | pack back on |

**Interface:** hold **Alt** and drag any part of the screen (dials, map, inventory…) to move it; pages drag by their title. **Alt+Backspace** resets.

---

## 5. What's in this build

- **An endless descent** through the first circles: procedural mountains, weather fronts per circle (fog, rain, snow, wind, heat), day and night, cold and chill.
- **Physical carrying:** a real 3D pack you pack by hand; its weight and balance push you around; falls and hard landings hurt.
- **Survival:** hunger, thirst, stamina, health; supplies along the routes; stations to rest at.
- **Branching routes** below every station; **boons** at stations; **permadeath** with a new mountain every life; records of your best depth.
- **Gear:** hat, coat, trousers, gloves, boots, cape, each with durability; pockets; mending with found materials at a fire.
- **Your companion:** a narrator who walks at your shoulder (500 recorded lines), and, with the model, answers whatever you say. Every spoken line is subtitled on the side the voice comes from.
- **Travellers** at most station fires: a pilgrim who turned back, a cartographer, an old carrier, the keeper of the fires, a penitent. Each has their own voice and their own story.
- **The map:** fog of war, paths ahead, waypoints you can see in the world, pencil drawing, a legend.
- **The journal UI:** hand-drawn parchment everything, all movable.
- **Footprints** in snow, mud and grass, with puffs of powder and dust.

---

## 6. What we'd love you to watch for

1. In the first five minutes: did you know what to do? Where did you get stuck?
2. Did falls feel fair or random? Did you use lean and brace?
3. Too easy, too hard? Did you starve, freeze, or get lost?
4. Did you find the stations? Use boons, sleep, repack?
5. Did you talk to anyone? What did you ask, and did the answers fit?
6. Did you find clothes and wear them? Notice wear and mending?
7. Bugs: crashes, getting stuck, falling through the ground, a page that won't close.

## 7. After you play: five quick questions

1. In one sentence: what is this game about?
2. Your best moment? Your worst?
3. What confused you?
4. Would you play another run right now? (yes / maybe / no)
5. How deep did you get, and what killed you?

Answer in the Discord, in whatever words you like.

---

## Known issues

- Linux build not yet tested on real Linux.
- The companion's live answers use your system's voice (not the recorded narrator's voice) and aren't positioned in 3D yet.
- The game pauses itself when its window loses focus (alt-tab): press Resume.
- Walking and foot placement get a big polish pass after this playtest. Tell us what feels off.

## If something breaks

Send the log from your last session (copy it before restarting the game):
- Windows: `%APPDATA%\Godot\app_userdata\Downhill Roguelite\logs\godot.log`
- Linux: `~/.local/share/godot/app_userdata/Downhill Roguelite/logs/godot.log`

Stuck? R gives up and starts a new mountain (your race result is the depth you reached before).

*Source: branch [`m11-npcs`](https://github.com/vstimac/DownhillRoguelite/tree/m11-npcs).*
