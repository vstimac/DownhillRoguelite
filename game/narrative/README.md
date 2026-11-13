# narrative

Story data for the narrator companion. See docs/narrative.md for the setting.

`data/metadata.csv`: 500 narrator lines with trigger, circle, phase and pacing metadata. Imported with the "keep" importer so Godot treats it as a plain file, not a translation table. Read it with `FileAccess.GetCsvLine`.

`NarratorDirector` plays the voiced lines (`audio/narrator/`) on game events: it maps events (falls, stumbles, stations, weather, depth, circles, idling, pickups, load, death) to the CSV triggers, then picks a line that fits the circle and is off cooldown, preferring the current narrative phase. Triggers for systems that don't exist yet (traders, ruins, routes, relics) wait for those systems. While the player talks with the companion, the voiced lines hold back.

## Conversation (M11)

`CompanionChat` lets the player talk with the companion (Enter opens the conversation page, Enter sends, Esc closes). It runs a local language model through the NobodyWho addon (`addons/nobodywho`):

- **The model** is a `.gguf` file, not in git (2.1 GB). The default is `../Models/qwen2.5-3b-instruct-q4_k_m.gguf`, next to the project folder (`D:\Game dev\Models\`). It's looked for relative to the project folder, the exported game's folder, and a `Models/` folder beside the game. `-- --llm-model=PATH` picks another one; `-- --no-llm` switches conversation off.
- **Without a model** the companion stays silent: the page says so and nothing breaks (an exported demo without the model just has a quiet companion).
- **Loading:** the model loads the first time the player speaks (about 5 s once it's on disk; video memory is used only then). Replies take 2 to 3 s on the GPU (Vulkan) and stream in token by token, cut off after `MaxReplyTokens`.
- **The persona** is `data/companion_prompt.txt`: calm, wry, short answers, practical advice, never confirms godhood, never steps outside the world.
- **Context:** every question goes with a note in words of where the carrier is and how they are (circle, depth, time, weather, shelter, hunger, thirst, injuries, tiredness, worn gear, the pack's weight and state, what's carried). The model reads it; the player never sees it.
- **A new run** gives a new carrier, so the conversation starts over.
- The exchange is written in the journal.
- Tests: `debug/tests/ConversationTests` (headless, silent). `-- --llm-live` also asks the real model.

## The restsite president (parody NPC)

`president/`: "Predsednik odmorišta (parodija)" at the Odmorište Konačnih Rezultata beside every second station. He speaks only manifest lines (`president/data/npc_restsite_president.csv`), voiced from the recorded pack (`president/data/npc_restsite_president_voice.json`, forest/outdoor takes in `audio/voice/npc_restsite_president/`) or shown as subtitles. He has three quests, a favour-paid shop, a podium speech and a secret scene, and his state is saved in `user://restsite_president.cfg`. See docs/restsite-president.md. Preview lines in `debug/TestPresident.tscn`; tests in `debug/tests/PresidentTests`.

## The šalteruša (counter clerk)

`salterusa/`: a Croatian counter clerk behind glass in a little booth (`CounterBooth`) beside the fire of station 2, and again every 4th station after the carrier has passed her. Voiced only by the team's own recordings (`audio/voice/npc_salterusa/hr-HR/`, 97 clips). `salterusa/data/npc_salterusa.csv` gives each clip an id, an event (bark, idle, breath, explain, quest, documents, gear, refuse, door, angry, return, rare, dear), an emotion and its subtitle; move a clip to another event there after listening to it. The subtitles in [brackets] are placeholders until the lines are written down (the pack was cut at pauses, not transcribed). She barks when you come up to the counter, talks or sighs if you loiter, and E opens the counter: permits, what it takes to go further (in order), your gear, refusals and the door, or "Can we just talk?" for the M11 conversation (persona `npcs/salterusa.tres`). After a death or an earlier run her first line is a return line; struck, "Bože dragi", then threats. Lab: `debug/SalterusaLab.tscn`; tests: `debug/tests/SalterusaTests`.
