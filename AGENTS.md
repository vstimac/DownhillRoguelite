# Agents working on DownhillRoguelite

This file is read by every coding agent (Claude Code, Codex and others). Keep it short; details live in the files it points to.

## Story and narrative: read `docs/STORY_BIBLE.md` first

Before you write or change anything narrative (NPC dialogue and personas, narrator lines, lore,
quests, item descriptions, voice recordings and their triggers, events such as the Shift, the
opening, endings), read `docs/STORY_BIBLE.md`, the director's shared narrative baseline. In short:

- **LOCKED** decisions stay: start in medias res, no culprit or hidden mastermind, discovery
  through play (no lore lectures), roguelite-first structure, mystery with consistent rules.
- **PROVISIONAL** ideas (the Last Witness, the Shift, the Unlived, the Promise) are options, not
  canon: never state them as fact in game text.
- **OPEN** questions (why descend, what the bottom is, how death works in-world, ...) are undecided:
  offer options and ask the director before canonizing an answer.
- The bible is direction, not an implementation order. Don't build quests, events, mechanics or
  audio triggers from it alone; check the code and get approval for the specific task.
- Never merge conflicting lore by yourself; write the conflict down and ask. Known conflict:
  `docs/narrative.md` (2026-11-06, "you descend to become a god", the narrator's godhood lines in
  `game/narrative/data/metadata.csv`) gives a reason for descending that the bible leaves OPEN.
- Use the bible's "Narrative change proposal" format (section 10) and label every new lore claim
  LOCKED / PROVISIONAL / OPEN. Approved decisions go into the bible's decision log.

Existing characters (HŽ, Šalteruša, the pack mimic, ...) keep their established voices; the
team's own recordings come first (`game/audio/original_recordings.csv`).

## Code

- The Godot 4.7 .NET project is in `game/`. Build with `dotnet build` there; tests are the scenes in
  `game/debug/tests/` (each prints PASS/FAIL lines and exits non-zero on failure).
- Commit messages are read by players (they are posted to Discord): plain, player-facing English,
  no AI co-author trailers.
- Don't push, merge into `main` or publish releases without the director's go-ahead.
