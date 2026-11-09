# The restsite president (parody NPC)

"Predsednik odmorišta (parodija)", NPC id `npc_restsite_president`, runs the **Odmorište Konačnih Rezultata**: a restsite beside every second station's fire (stations 1, 3, 5... by default). He is a caricature in an invented world. Everything he says is original satire, not real statements, news or endorsement. His name in the game carries "(parodija)", and his tooltip says the voice is synthesized. The running joke is that you descend while he insists everything is going uphill. Deeper in the descent (circle 3+, or after six returns) his small talk turns quietly melancholic.

Code: `narrative/president/`. Data: `narrative/president/data/`. Voice: `audio/voice/npc_restsite_president/`. Tests: `debug/tests/PresidentTests`. Line preview: `debug/TestPresident.tscn`.

## Data (no words in code)

| File | What |
|---|---|
| `data/npc_restsite_president.csv` | The manifest: 57 lines, `id, priority, event, emotion, text_sr, audio_path`. NPC speech comes only from `text_sr`. |
| `data/npc_restsite_president_voice.json` | The voice pack (NPC_Vucic_Dialogues.json): 24 recorded lines `vucic_001`–`vucic_024`, each a `forest` and an `outdoor` take with durations. |
| `data/player_text.csv` | The player's words (choices, in Croatian), names, hints, quest titles: `id,text`. `{name}` placeholders. |
| `audio/voice/npc_restsite_president/forest/`, `outdoor/` | The 48 takes (Vorbis). |

**One id per line everywhere.** The dialogue, subtitles, choices and log use the manifest id (`intro_001`). A manifest line is voiced when the pack has a recording with exactly its words. All 24 recordings are the manifest's first 24 lines (`vucic_001` = `intro_001` ... `vucic_024` = `return_after_death_02`). The log names both, for example `[voice] npc_restsite_president/intro_001 (first_meet, serious) voiced: vucic_001 outdoor/vucic_001_intro_stop.ogg`.

Some of the pack's `context` labels don't match the words; the dialogue follows the words:

- `vucic_008` ("I treba da idete...") is the answer to "Idem dalje", not a quest warning.
- `vucic_016`/`017` are the truth/agree endings of quest 1.
- `vucic_018` ("čovek od malo reči") is the silent ending. It is also used as an idle remark, as the pack suggests.
- `vucic_023` ("Opet ste tu?") greets you on a return visit.
- `vucic_024` ("Čujem da ste pali") greets you after a death.

**Which take plays:** the director's `VoiceVariant` (`outdoor` by default; `-- --president-voice=forest`). If that take is missing, the other one plays (never both). If neither exists, the manifest's `audio_path` is tried. Then the line is subtitles only, timed by its length; nothing fails.

**Adding the other 33 lines:** record each line on its own, never several in one clip. Then do one of these:

- Add it to the JSON pack with its exact `text_sr` words.
- Or save it at the manifest's `audio_path`: `res://audio/voice/npc_restsite_president/<id>.ogg`, for example `idle_03.ogg`. `.wav` and `.mp3` with the same name also work.

Godot imports the file; nothing else changes.

## Talking

E at the president starts a talk. His lines play one after another (E skips a line), then the choices show on the HUD (`ui/hud/DialogueChoices`): number keys 1–9, which the hotbar doesn't get while choices show. Nothing pauses or blocks walking; walking off (8 m) closes the talk. When you're near and not talking, he says an idle line at most once every 45–90 s.

Where a talk starts, in order:

1. **A quest ready to hand in.**
2. **The first meeting** (once per save): `intro_001`, `intro_002`, then the choices:
   - "Ali ja se spuštam." → `intro_004`
   - "Tko si ti?" → `intro_a2`, `intro_a3`
   - "Samo se želim odmoriti." → `intro_b2`, then rest
   - "Imaš posao?" → `intro_c2`, then quest 1 is offered
   - "Idem dalje." → `intro_d2`
3. **A special moment:**
   - back after a death (`return_after_death_02`, or `_03` after five returns)
   - low health (`low_health_01`)
   - a heavy pack (`heavy_inventory_01`)
   - a return visit (`return_after_death_01`, "Opet ste tu?")
   - quest 2 stalled (`q2_fail`)
4. **A quest on offer.**
5. **Small talk:** an idle line, then the menu (who are you, rest, shop, a job, the secret question, leave → `leave_01`, or `farewell_final_01` after the secret).

**Rest** plays `rest_001` (`intro_b2` at the first meeting). If it's late enough to sleep, you sleep until dawn (the station's own sleep). Otherwise a hint points to the bedroll; there is no separate rest UI.

## The three quests

States `not_started / active / ready / completed`, saved. Each pays once (finishing twice does nothing). They show on the objectives panel as errands from the president (`GameEvents.PublishQuestNote` → ObjectiveDirector).

**Nema konačnih rezultata** (`q_final_results`)
- Offer and answer: `q1_offer_01`, `q1_offer_03`, then accept (`q1_accept`) or decline (`q1_decline`).
- Three records (`president_record`) lie 3–7 m off the path at 30, 55 and 80 % of the way to the next restsite. They are ordinary pickups for the inventory. Carrying three makes the quest ready.
- A record left 40 m behind is lost to the wind; a damaged copy (`president_record_damaged`) waits by the next restsite's board, so the quest never locks.
- Hand-in: `q1_return_01`, `_02`, `_04` (the records are taken), then "Ti ne znaš rezultat." / "Rezultati su sjajni." / "(Šuti.)" → `q1_end_truth` / `_agree` / `_silent`.
- Reward: +2 favour (+1 with a damaged copy) and the **temporary mandate** (`item_temporary_mandate`). While it's in the pack, a fatal fall brings you back with 25 % health and uses it up. Hunger, thirst, cold and the abyss aren't falls.
- Each later run starts with a new mandate, so it works once per run. The hook is `GameEvents.LastChanceRequested`, asked by CarrierHealth just before a death; nothing else in the death handling changed.

**Samo izvođači radova** (`q_contractors`, after quest 1)
- Offer and answer: `q2_offer_01`, `_03`, `_05`, then `q2_accept`.
- Two worker-automatons (E sends one to the bridge) and four units of bridge material (pickups) lie on the way to the next restsite. Either two workers or four units is enough. Hand-in: `q2_complete` (the material is taken).
- Reward: +2 favour and `bridge_unlocked` (saved). Every restsite's bridge scaffold becomes "druga faza".
- **Stub (TODO art and route):** the route has no bridges or shortcuts yet, so the unlock is only the saved flag and the cosmetic bridge. The worker-automatons are box placeholders.

**Istorijska pobeda** (`q_historic_victory`, after quest 2)
- Offer and answer: `q3_offer_01`, `_03`, then `q3_accept`.
- The 12 kg banner (`historic_banner`, contract-style cargo) is packed into the backpack. It takes real space, and its centre of mass sits off to one end, so it pulls the load to one side.
- The stand is placed on the path 400 m lower. A knock of 6+ damage while the banner rides on the pack's outside (exposure ≥ 0.5) tears it. A torn banner can still be planted: partial hand-in, `q3_broken` and +1 favour instead of `q3_complete` and +3.
- If the run ends before the banner is planted, the banner is lost and the quest can be taken again.
- Reward: the **megaphone**. Use it from the hotbar; it has a 30 s cooldown. It publishes `GameEvents.MegaphoneBlasted(position, radius 25 m, confuse 4 s, notice 10 s)`, then raises `GameEvents.Detectability` to 1.3 for the notice time.
- The night beasts (`wildlife/BeastDirector`, docs/night-beasts.md) answer both: a blast throws off every beast in its radius for 4 s (a crouch or a charge breaks off), and while `Detectability` is above 1 they attack that much sooner. The podium's 1.1 counts too.

## The restsite

`RestsiteSet` builds each piece from simple shapes as its own node: Podium, CoffeeMachine, Flags, Board, Stairs (into a wall), Bridge. Each has a `PackedScene` slot, so real models can replace them one at a time.

- **Shop "Privremene mere":** `shop_001` when opening, `shop_002` when leaving without buying. The game has no money, so it's paid in the president's favour (earned by the quests); with too little favour he says `no_money_01`. Items:
  - Pečat poverenja (1): does nothing, and says so.
  - Sendvič strateških rezervi (1): food.
  - Mapa optimizma (2): use it to hear the distance to the next station, halved.
- **Podium:** one speech per run. "Održi govor." → `podium_buff_01`, then +10 % stamina recovery and `Detectability` 1.1 for 60 s. "Bez govora." → `podium_skip_01`, no effect. `used_podium_this_run` isn't saved, so every run gets a new speech.
- **"Istorijske pobede" board:** +1 for every death and every station reached. It's a joke counter, saved.

## The secret scene

After five returns (`player_returns_to_restsite`: each restsite reached in a run after the first meeting), with all three quests done and the scene not yet seen, the menu offers "Zašto si još ovdje?".

He only says manifest lines: `secret_02`, `secret_04`, `secret_06`, `secret_07`, `secret_aftermath_01`. The player's questions between them are in `player_text.csv`. Afterwards `secret_seen` and the badge `the_last_announcement` are saved; it's cosmetic, shown as a hint.

## Save

`user://restsite_president.cfg`; headless runs and tests keep it in memory. It holds:

- met, favour, `player_returns_to_restsite`
- the three quest states, `secret_seen`, `the_last_announcement`
- `bridge_unlocked`, `mandate_unlocked`, `megaphone_unlocked`
- `historic_victories`, `last_run_died`, `victory_torn`, the quest 1 ending

Per run only (not saved): the podium speech, the placed records, workers and material, the stand.

## Testing

- **Headless:** `godot --headless --path . --fixed-fps 60 res://debug/tests/PresidentTests.tscn`. It checks:
  - the data agrees
  - the takes and their fallback
  - the first meeting and that it isn't repeated
  - quest 1 from offer to reward, paid once
  - the mandate revive
  - the podium once per run
  - the secret gate
  - the save round-trip
- **Every line in the editor:** open `debug/TestPresident.tscn` and run the current scene (F6).
  - Up/Down picks a line; Enter says it.
  - V switches forest/outdoor; R says an idle line.
  - The panel shows the recording or "subtitles only".
  - `-- --lab-line=intro_004` starts on a line.
- **In the game:** start the main scene and walk to Station 1.
  - The president stands at the podium left of the fire.
  - E talks; 1–5 choose.
  - Take "Imaš posao?" for quest 1. The records are on the way to Station 3.
  - `-- --president-voice=forest` plays the forest takes.
  - `-- --at-station=1 --at-president` starts beside him (renders and quick checks). Renders (`--write-movie`) keep the save in memory, like tests.
  - To start over, delete `%APPDATA%/Godot/app_userdata/<project>/restsite_president.cfg`.
