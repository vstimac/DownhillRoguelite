# Playtest 1 (2026-11-07, 18:00, 5 players)

Build: `D:\Game dev\Builds\DownhillRoguelite-playtest1\` (branch `playtest-1`: everything through M14 gear and M11 conversation).

## Setup (before 18:00)

- **Windows 10/11, 64-bit, a GPU with Vulkan** (any recent NVIDIA, AMD or Intel card).
- Copy the whole build folder to each PC. `Models\` (2.1 GB) is only needed for talking with the companion; without it the companion says it is silent and everything else works.
- Start with **`DownhillRoguelite.exe`**, or **`Race - same mountain.bat`** for the race below.
- Headphones help: the narrator, weather and footsteps carry a lot.
- **Talking (Enter):** the model loads when the page first opens (a few seconds); the first answer takes 10 to 15 s, later ones a few seconds. The game drops to 15 fps while the companion thinks (it frees the GPU; the world is behind the page). On a PC without a dedicated GPU, answers will be slow: skip talking there.
- **Voices and subtitles:** the companion walks at your left shoulder. Recorded lines play from there in 3D; live replies are read by the Windows voice (not 3D yet). Every spoken line is subtitled on the side the voice comes from, with an arrow when the speaker is off to the side.
- **Moving the UI:** pages drag by their title; hold Alt to drag any HUD piece; Alt+Backspace resets. Layouts are saved per PC.
- **Linux beta:** `D:\Game dev\Builds\DownhillRoguelite-linux-beta.tar.gz` (untested on real Linux). Unpack with `tar -xzf`, then run `./DownhillRoguelite.x86_64` or `./race-same-mountain.sh`. Needs Vulkan drivers. Copy `Models\` from the Windows build for talking.
- Known: one wildlife check sometimes catches an animal hovering a few cm over the ground (cosmetic).
- If one PC plays a few people in turns, quit and restart between players (each start is a fresh carrier).

## The race (the social part)

Everyone starts with `Race - same mountain.bat`: the same mountain (seed 1), one life. **Your first life counts.** Deepest depth wins (shown when you die, and on the descent note top right). After your first death the game moves you to new mountains: play on freely, that's for fun only.

## Controls card (also in game: Esc)

| | |
| --- | --- |
| WASD | walk |
| Shift | sprint (costs stamina) |
| C | brake: careful steps on steep ground |
| Space | jump |
| Mouse | look |
| Left / right mouse | lean left / right against the load; both = brace |
| E | pick up, sleep at a bedroll, choose a boon |
| F / Q | eat / drink |
| 1-9, V | choose from the hotbar, use it |
| I | inventory: Eat, Drink, Wear clothes, Mend gear (at a station) |
| J | journal |
| M | map: click a waypoint, right-click removes, drag draws (with paper and pencil) |
| Enter | talk with the companion (Enter sends, Esc closes) |
| Tab | repack the backpack (at a station) |
| Esc | pause, controls, volume |
| R | give up (ends the run: new mountain) |

Survival in one line: reach the next station (the fire and its light beam) before night; eat, drink and rest; heavy or badly packed loads tip you over.

## What to watch (for the director)

Note the minute and the player for each.

1. **First 5 minutes:** do they understand what to do without help? Where do they get stuck (controls, balance, where to go)?
2. **Balance and falls:** do falls feel fair or random? Do they use lean and brace?
3. **Survival:** does anyone starve, freeze or die of thirst? Too easy, too hard?
4. **Stations:** do they find them? Do they use boons, sleep, repacking?
5. **Talking:** does anyone try Enter on their own? What do they ask the companion, and do the replies land?
6. **Gear:** do they find clothes and wear them? Do they notice wear and mending?
7. **Bugs:** crashes, stuck carrier, falling through the ground, UI that won't close.
8. **Mood:** when do they laugh, swear, go quiet? When do they stop paying attention?

## Questions afterwards (2 minutes each)

1. In one sentence: what is this game about?
2. Best moment? Worst moment?
3. What was confusing?
4. Would you play another run right now? (yes / maybe / no)
5. Depth reached, and how you died.

## If something breaks

- The log of the last session: `%APPDATA%\Godot\app_userdata\Downhill Roguelite\logs\godot.log`. Copy it out before restarting the game (each start replaces it; older ones are kept with dates).
- A stuck carrier: R gives up and starts a new mountain (the race result is the depth before that).
