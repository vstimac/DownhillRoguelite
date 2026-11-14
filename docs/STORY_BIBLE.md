# DOWNHILLROGUELITE — STORY BIBLE

> **Status:** Narrative direction / shared reference for all development agents  
> **Version:** 1.0 — 2026-11-13  
> **Working narrative subtitle:** *The Promise* (not the game's title)  
> **Game title:** **DownhillRoguelite**  
> **Language:** Croatian for current story development; localization may follow.  
> **Authority:** This document guides narrative proposals. It does **not** assert that unimplemented systems exist.

## 0. Agent instructions — read first

This is the **single shared narrative baseline** for Claude Code, Codex, writers, quest designers, audio designers, and other agents working on DownhillRoguelite.

1. **Preserve the LOCKED decisions** below. Do not silently rewrite them.
2. Treat **PROVISIONAL** ideas as options, **not established lore**. Never describe them as confirmed in game text.
3. Treat **OPEN** questions as undecided. Present options and ask for approval before canonizing answers.
4. **Do not implement** quests, events, mechanics, dialogue, audio triggers, or world changes based on this document alone. Inspect the actual repository and obtain task-specific authorization first.
5. Do not overwrite existing NPC characterization, voice lines, quest systems, HUD, inventory, multiplayer, save systems, or established content without reviewing the project.
6. Do not invent a single culprit, chosen-one prophecy, hidden villain, or mandatory boss defeat as the answer to the mystery.
7. Never turn the story into a linear cutscene-driven experience. Gameplay discovery is the primary delivery mechanism.
8. New narrative proposals must identify whether they are **LOCKED**, **PROVISIONAL**, or **OPEN**, and distinguish fictional design from implemented behavior.
9. Maintain continuity across player death, repeat runs, and multiplayer; do not assume any specific save/network architecture without checking code.
10. When two agents disagree, **do not merge conflicting lore automatically**. Record the conflict and request a human decision.

## 1. Narrative pillars — LOCKED

### 1.1 In medias res

The game begins **in the middle of an ongoing situation**, not with a creation myth, exposition dump, amnesia reveal, prophecy, or lengthy cinematic. The player is already moving downward and must react to immediate physical danger. The world existed before the player arrived and continues without explaining itself.

### 1.2 No culprit

**There is no guilty party responsible for the world's condition.** No character deliberately caused the central catastrophe, no secret mastermind needs to be defeated, and no moral twist retroactively assigns blame to the protagonist, the Unlived, or the Witness. Characters may make imperfect decisions and experience grief, but the central phenomenon is not a punishment for someone's sin.

### 1.3 Discovery through play

Players learn through landscape, encounters, item descriptions, conflicting testimony, environmental changes, and consequences of action. NPCs should have their own agendas and personalities; they are not exposition dispensers.

### 1.4 Downward movement as both mechanic and question

Descending is the central action. The player begins by moving down to survive, later becomes curious about the world, and eventually develops a personal emotional reason to continue. The precise metaphysical meaning of “down” remains undecided.

### 1.5 Mystery without arbitrary confusion

Withhold explanations, **not consistency**. Strange events must obey discoverable rules. A later revelation should make earlier details more meaningful without invalidating everything the player experienced.

### 1.6 Emotional depth without melodramatic coercion

The story explores absence, possibility, impermanence, connection, and promises. Avoid relying on a dead romantic partner, an evil deity, or a guilty hero as a shortcut to emotional impact. Humor, ordinary details, and moments of quiet are essential.

### 1.7 Roguelite-first structure

Narrative content must work with repeated attempts, variable routes, survival, physical equipment/backpack systems, and potential cooperative play. The story should not require a fixed sequence of long cinematics.

## 2. Player experience arc — LOCKED direction, flexible details

| Stage | Player's immediate perception | Narrative function |
| --- | --- | --- |
| I — Where am I? | Movement, danger, strange NPCs, distant presence | Establish rules by doing |
| II — What is happening? | Spatial contradictions, traces of other possibilities, the Shift | Build curiosity and uncertainty |
| III — Who or what is waiting? | Recurring clues, an ambiguous promise, traces of an absent presence | Build personal emotional investment |
| IV — What was lost? | Evidence of lives or moments that never came to pass | Recontextualize early scenes |
| V — What do I do with this knowledge? | A meaningful choice or acceptance, not necessarily combat | Pay off theme without assigning blame |

These stages are **dramatic progression**, not a fixed number of levels, hours, bosses, or required runs.

## 3. Opening — PROVISIONAL playable sequence

A possible opening to prototype or storyboard, **not approved as a final scene**:

- **0:00:** Black screen, footsteps, breathing, distant bell; an unidentified voice shouts, “Ne staj! Nemamo vremena!”
- **0:15:** Control begins during a dangerous descent. Teach movement through the environment.
- **0:30–1:30:** A broken route requires an immediate traversal decision.
- **1:30–3:00:** An apparently ordinary traveler stands still while the landscape behind them shifts unnaturally.
- **3:00–5:00:** An encounter with **HŽ — Vječni putnik** introduces dry humor and the suggestion that the player has been here before. Example: “A, evo vas. Mislio sam da ćete opet zakasniti.”
- **~5:00:** Fog briefly parts to reveal a vast distant figure, the **Last Witness**. The traveler casually discourages staring. No explanation follows.

**Guardrails:** Do not force a specific opening line or NPC placement without design approval. Avoid a generic waking-up-with-amnesia opening. Do not assume the game currently has these scenes.

## 4. Central phenomena

### 4.1 The Shift / Pomak — PROVISIONAL concept

A rare, recognizable event during which the world changes in ways that challenge ordinary causality or memory. Possible manifestations:

- Ambient sound falls away before a major spatial change.
- A bridge or cliff section changes position.
- Several NPCs look toward the same distant point.
- Footsteps are heard before the player takes them.
- A familiar place becomes unfamiliar, while an item or record retains evidence of its former state.
- Very rarely, the distant Witness is absent.

**Important:** The Shift is **not confirmed to be caused by the Witness**. Treat causality as OPEN. Events should be authored with clear triggers, limits, player-readable signals, and consequences. Do not randomly delete quest-critical items, progress, or players' inventory.

### 4.2 Last Witness / Posljednji svjedok — PROVISIONAL identity, LOCKED narrative role

A vast, visually recognizable presence seen far below the player at selected moments. Its visibility and unusual stillness create tension. Its **identity, intentions, and relationship to the Shift remain OPEN**.

Do **not** canonize the earlier suggestion that the Witness is a future version of the player. Do not assume it is evil, a god, a boss, or responsible for the world's condition. The important early-game question is: **What changes when it moves or disappears?**

### 4.3 The Unlived / Nerođeni — PROVISIONAL mystery

A possible emotionally central presence associated with **unlived possibilities**: moments, relationships, or lives that could have existed but did not. The Unlived is **not yet confirmed as a literal person, ghost, child, antagonist, or metaphysical entity**.

Possible clues: a second set of footprints; a recording with a missing speaker; a photo that seems to show someone who was never there; an object with an impossible history; a conversation overheard in a room that is empty when entered.

**Do not canonize** earlier versions in which the protagonist created the Unlived, the Unlived caused the catastrophe, or the protagonist repeatedly sacrificed others. These conflict with the no-culprit direction.

### 4.4 The Promise / Obećanje — PROVISIONAL motif

A recurring phrase, object, or encounter may suggest a promise: **“Ako se izgubimo, nađemo se dolje.”** Its speaker, recipient, original meaning, and truth status remain OPEN. It should emerge gradually through play, **not** appear as an explicit opening quest or mandatory mission objective.

### 4.5 The bottom / Dno — OPEN

The bottom may be a place, a condition, a threshold, a change in perception, or something else. Do not state as canon that it is a boss arena, a fixed location, a time loop, or a simulation. The possibility that moving upward does not return the player to the same place is **PROVISIONAL**, not a confirmed navigation rule.

## 5. Existing characters — preserve distinct identities

The character roster below reflects the established concept-art direction. Character lore must not be rewritten just to explain the main mystery.

| Character | Established characterization | Optional story connection — PROVISIONAL |
| --- | --- | --- |
| **HŽ — Vječni putnik** | Awaits a train that never arrives; sells tickets to nonexistent destinations | A ticket references a station erased or never built |
| **Šalteruša — Čuvarica prolaza** | Bureaucratic gatekeeper; A38 and missing stamps | Files document impossible journeys |
| **Plenković — Finalni ministar** | Satirical boss of a bureaucratic cycle; promises exits that lead to more cycles | Bureaucratic repetition reflects the larger theme without explaining it |
| **Mile Kekin — Buntovni narator** | Occasional ally/commentator | Observes inconsistencies through irreverent commentary |
| **Monsignor — Trgovac grijesima** | Sells blessings, indulgences, easier descent for a fee | Has a personal interpretation of the Witness, not authoritative truth |
| **Ruksakar — Putujući trgovac** | Traveling merchant with oversized backpack | Has traded objects from places no longer found |
| **Stric iz podruma** | Gives confidently bad advice | Accidental clues mixed with nonsense |
| **Internet trolaš** | Misinformation and misleading tasks | Demonstrates why testimony is unreliable |
| **Baba proročica** | Predicts failures, enjoys being right | Speaks as if she remembers unrealized futures |
| **Lovro kockar** | Gambles for rare items | Chance and alternate outcomes resonate with theme |
| **Carinik kontrolor** | Inspects loads; penalizes excess weight | Notices goods with impossible origins |
| **Ruksak mimik** | Loot-associated backpack enemy | Might retain an object after a Shift |
| **Beskrajni hodočasnik** | Always ahead; player never catches him; haloed, mysterious figure | Remembers something others do not; never reduce him to a routine quest vendor |
| **Vrag — Ravnatelj porezne** | Horned tax administrator at the bottom; dry, passive-aggressive bureaucracy | Maintains accounts for people or debts that seem not to exist |

**Important:** Beskrajni hodočasnik and Vrag are **two separate characters**. Do not combine them. Preserve Croatian deadpan, absurdist humor without turning every character into a tragedy or exposition device.

## 6. Narrative delivery rules

### 6.1 Environmental storytelling

Prefer a concrete contradiction to an explanation. Example: a railway ticket names a station that no map acknowledges. The player discovers the discrepancy by examining the ticket and the environment.

### 6.2 NPC dialogue

- Dialogue must fit the character's voice and immediate situation.
- NPCs can be wrong, biased, evasive, or simply uninformed.
- Avoid monologues that explain the cosmology.
- Write short, playable lines; reserve long speeches for rare earned moments.
- Use humor to humanize the world, not to undercut every serious scene.
- For voice acting, include stable IDs, performance directions, and separate nonverbal takes.

### 6.3 Quests

Quests should have a local, concrete purpose first (find a ticket, deliver a form, inspect a place, retrieve an item). A larger thematic clue may emerge as a side effect. Avoid generic “collect lore fragments to reveal the truth” gating. Keep optional discoveries optional where feasible.

### 6.4 Items and the backpack

The physical backpack is a gameplay system, **not automatically a magical memory vault**. Some rare story items may preserve evidence across a Shift, but their persistence and mechanics are **OPEN**. Do not make every loot item symbolic. Respect equipment weight, accessibility, and existing inventory rules.

### 6.5 Repeat runs and death

**Provisional design preference:** death can be acknowledged in-world, and subsequent runs may recontextualize prior clues. The exact canon of death, persistence, memory, and reset is **OPEN**. Never assume a full world reset or mandatory New Game+ without reviewing design and code.

### 6.6 Multiplayer

Players may witness different *perceptual* details (audio, visions, clues), but must share coherent physical objectives and traversable geometry unless a network-safe design is explicitly approved. No narrative feature may silently desynchronize the host and clients.

## 7. Example discovery scenes — PROVISIONAL, not implementation orders

### Scene A — The playground

An abandoned playground appears at depth. A swing moves with no visible occupant. If the player waits, a brief conversation is heard:

> **Nepoznati glas:** “Misliš li da je moguće nedostajati nekome koga nikada nisi upoznao?”  
> **Drugi glas:** “Ne znam.”  
> **Nepoznati glas:** “Onda zašto imam osjećaj da me netko čeka?”

A photograph depicts the same swing with a seated figure. No immediate explanation is given.

### Scene B — The impossible ticket

HŽ sells or shows a ticket for a destination that does not appear on any current route. Later, a physical sign briefly names that destination during a Shift.

### Scene C — The empty room

The player hears a normal conversation from a nearby room. Upon entering, it is empty. One mundane object remains displaced. No jump scare is necessary.

### Scene D — The Witness is missing

After several sightings establish a familiar distant silhouette, a rare vista reveals that it is gone. NPCs do not all react in the same way. The absence is more unsettling than a direct attack.

## 8. Narrative tone and boundaries

**Desired mix:** grounded survival + Croatian absurdist satire + gradual existential unease + restrained emotional revelation.

**Do:** let mundane objects carry meaning; use silence; allow players to form hypotheses; make repeat discoveries rewarding; preserve character-specific humor; allow unresolved ambiguity.

**Do not:** copy the plot, visual signature, or mechanics of *Clair Obscur: Expedition 33*; use a Paintress-style countdown; introduce an evil mastermind; force the protagonist into a guilt-redemption story; rely on “it was all a dream/simulation”; over-explain the Witness; promise a fixed ending before one is chosen.

The inspiration from other games is **the feeling of a visible mystery and the gradual recontextualization of evidence**, not their plot elements.

## 9. Open decisions — require explicit human approval

1. **Why is descending necessary?** Physical route, survival pressure, curiosity, personal connection, or layered reasons?
2. **What is the Shift's actual rule set?** Trigger, frequency, limits, reversibility, effects on saves and network state?
3. **What is the Witness?** Identity, visibility, agency, and relation (if any) to the Shift?
4. **What is the Unlived?** Entity, metaphor, possibility, relationship, or unreliable interpretation?
5. **What exactly is the Promise?** Speaker, recipient, wording, and emotional meaning?
6. **What is the bottom?** Place, threshold, paradox, or something else?
7. **How does death work in-world?** Memory and progress persistence?
8. **How do multiplayer perspectives affect discovery?** Shared truth versus individual perception?
9. **What constitutes an ending?** Closure without a mandatory villain or boss; how replayability continues?
10. **Which specific events, locations, NPC quests, and audio lines are approved for production?**

## 10. Workflow for agents

Before proposing or changing narrative content:

1. Read this file and the relevant current game assets/scripts/quest definitions.
2. State the specific player-facing problem being solved.
3. Propose the smallest coherent addition and identify dependencies.
4. Label each new claim as **LOCKED / PROVISIONAL / OPEN**.
5. Check continuity with named NPCs, existing recorded voice lines, item systems, saves, and multiplayer.
6. Present potential spoilers separately from player-facing copy.
7. Ask for approval before changing canon or implementing mechanics.
8. After approval, update this file's decision log and any related quest/dialogue specifications.

### Suggested task handoff format

```md
## Narrative change proposal
- Goal:
- Player experience:
- Relevant locked pillars:
- New lore claims (LOCKED / PROVISIONAL / OPEN):
- Gameplay impact:
- NPC/dialogue impact:
- Save/multiplayer implications:
- Required assets/audio:
- Risks/contradictions:
- Approval needed:
```

## 11. Decision log

| Date | Decision | Status |
| --- | --- | --- |
| 2026-11-13 | Start in medias res, amid an ongoing descent | **LOCKED** |
| 2026-11-13 | No culprit or hidden mastermind responsible for the world's condition | **LOCKED** |
| 2026-11-13 | Reveal the story through play rather than exposition | **LOCKED** |
| 2026-11-13 | Maintain mystery, existential themes, emotional depth, and absurdist humor | **LOCKED** |
| 2026-11-13 | Last Witness as a distant visible presence | **PROVISIONAL** |
| 2026-11-13 | Shift as a recognizable reality-changing event | **PROVISIONAL** |
| 2026-11-13 | The Unlived and the Promise as central motifs | **PROVISIONAL** |
| 2026-11-13 | Witness is a future player / protagonist or Unlived caused disaster | **NOT CANON** |

---

**Core creative test:** Does this addition make the player *notice, wonder, act, and later reinterpret* something, without forcing a villain or a lore lecture? If not, revise it.
