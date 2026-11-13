# NPC voices, subtitles and dialogue language

State as of 2026-11-13. `game/debug/tests/DialogueLanguageTests.tscn` checks what is listed here.

## Dialogue language (Options → Accessibility → "Dialogue text")

- **English / Hrvatski.** Only dialogue text follows it: NPC subtitles, the player's answers in NPC
  conversations and those conversations' notes and prompts. The rest of the interface stays English.
- **Voices never change.** A Croatian recording with English chosen keeps speaking Croatian; its
  subtitle is the English translation. With Hrvatski chosen the subtitle is the words as recorded.
- First launch: Hrvatski on a Croatian, Bosnian or Serbian Windows, English elsewhere.
- Data: every voice manifest has `text_sr` (the words as recorded) and `text_en` (the translation);
  `player_text.csv` has `text` and `text_en`. A line without `text_en` falls back to its own words.
  Code: `core/DialogueLanguage.cs` (`DialogueText.FromCroatian` / `FromEnglish`), `VoiceLine.Shown`,
  `TextTable.Get`, `Salterusa.ShownChoices`.
- English voices (the narrator's 500 lines, the companion, the newspaper seller) are not translated
  into Croatian: only Croatian dialogue gets an English version.

## Audit

| NPC | Lines | Recordings | Words (subtitles) | English | In the game |
| --- | --- | --- | --- | --- | --- |
| Predsjednik odmorišta (`narrative/president`) | 57 | 24 recorded (forest + outdoor takes), 33 subtitle-only | all 57 | all 57, plus 74 answers/notes | yes, rest stops |
| Šalteruša (`narrative/salterusa`) | 97 | all 97 load (team's recordings) | **4 transcribed, 93 only described** in [brackets] | all 97 (descriptions translated) | yes, station 2 and every 4th |
| HŽ — Vječni putnik (`narrative/hz_putnik/data`) | 23 | all 23 load | all 23 | all 23 | **no: the NPC doesn't exist yet**; the manifest is ready for it |
| Pack fiend (Mimic), bog ogre (Cup of Pohlepe) | 21 | team's recordings | no words (breaths, growls) | not needed | yes |

### What needs a person

1. **Šalteruša: 93 clips need their words.** The pack was cut at pauses, not by recognised speech
   (its README says so), so a clip may hold more than one line or a cut-off line. Until the words
   are written into `text_sr` (and translated into `text_en`), the subtitle shows a description such
   as "[Dokumenti, dozvole i pravi žigovi.]". Options: automatic transcription (Whisper, then a
   person checks), or the team writes them while listening.
2. **HŽ: two lines to confirm by ear** (column `review` in `npc_hz_putnik.csv`): HZ_009 "Trebao je
   doći u utorak" (marked uncertain earlier) and HZ_017 heard as "Ne stojite na rubu perona" (a
   warning would be "Ne stojte").
3. **Šalteruša and the story bible:** the session that built her listed 7 points for the director
   (the biggest: she "remembers" players who come back after dying, while the bible keeps death and
   memory OPEN). The descriptions in her manifest were kept as they were.
4. **The president's 33 unrecorded lines** are subtitles only (reading time instead of a voice).
