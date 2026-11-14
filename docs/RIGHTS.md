# Licensing and rights — DownhillRoguelite

**Read this before copying assets, contributing content, making a game release, or using the project in promotional material.**

This repository contains original source code and material with **separate rights holders and licenses**. The presence of a file in a public repository does not mean anyone may freely copy it.

## 1. MIT-licensed source code

The root [LICENSE](../LICENSE) applies to **original source code owned by the project authors** and contributed to the project under MIT, such as original C# and script files. It also covers original software documentation to the extent the authors have the rights to license it.

**It does not relicense or supersede independently licensed third-party code, including Godot addons and external libraries.** Check the license supplied with each dependency.

MIT permits copying and modifying covered code with the copyright notice and license kept intact. It includes an “as is” disclaimer; **it is not insurance against lawsuits** and cannot grant rights the authors do not own.

## 2. Game content — no blanket license

**The root MIT license does not apply automatically to:** 3D models, rigging, animations, artwork, images, posters, concept art, logos, trademarks, character likenesses, game dialogue/story/narrative, voice recordings, music, sound effects, photographs, external models, and third-party assets. These require rights from their respective owners, or separate explicit licenses. For original project-specific content not otherwise licensed, all rights are reserved.

| Content / location | Current treatment | Before redistributing |
| --- | --- | --- |
| Kenney / Quaternius asset packs in `art/` and `audio/sfx/` | Attributed as CC0 in [art/CREDITS.md](../game/art/CREDITS.md) | Check the actual pack's included `LICENSE.txt` and source provenance |
| `art/characters/mixamo/` | Adobe Mixamo terms | Do **not** publish raw FBX character / animation packages; follow [Mixamo notes](../game/art/characters/mixamo/README.md) |
| `art/characters/*character-pack.png` | Concept illustration, includes recognizable satirical personalities | Not a model pack; do not imply affiliation, endorsement or permission to commercially use any real person's likeness |
| `audio/narrator/` | Original script; voice output made using ElevenLabs | Review voice permissions, service terms and use of any cloned voice |
| `audio/music/`, `audio/ambience/` | Some procedurally generated audio | Confirm source and all incorporated audio elements before releasing |
| `audio/sfx/` | Mixture of potentially separately licensed assets | Track source and individual permissions |
| `audio/sonniss/` | Cut and processed clips from the Sonniss #GameAudioGDC 2026 bundle (royalty-free, commercial use, no attribution required); every clip's library and original file is in [audio/sonniss_sources.csv](audio/sonniss_sources.csv) | The licence forbids selling the sounds "as they come" and any use for AI training; ship them only inside the game, never as a sound pack |
| `audio/creatures/mimic_*`, `cup_*`, `audio/voice/` | The team's own recordings ([original_recordings.csv](../game/audio/original_recordings.csv)) | Rights stay with the team |
| `addons/` | Third-party software | Respect its included licenses, even when original project code is MIT |
| Optional `*.gguf` models | Not shipped in the repo | Distribute only under the model's actual license and with required notices |
| Posters, logos and original character art | Not automatically MIT | Use only with verified rights and a suitable separate license |

For details, see [art/CREDITS.md](../game/art/CREDITS.md). Do not infer a universal open-content license from an engine project being open source.

## 3. Songs, sound recordings and voices

**Do not add or distribute any third-party commercial song, instrumental, sample or master recording without documented rights for use in the game and relevant media.** Permissions concerning the composition and permissions concerning the actual recording can come from different rightsholders. Personal messages from a performer/songwriter may **not** clear the recording/master rights.

For each proposed song or recording, keep private documentation covering:
- exact track/version and intended uses (in-game, trailers, streaming, downloadable builds);
- who owns the composition, performance and master recording;
- which permission covers each use, territories, duration, platforms and monetization;
- written approvals and restrictions.

No rights are claimed here to the music or trademarks of third-party artists. **Do not copy a real person's voice or add a voice clone without a suitable authorization.**

## 4. Real people, satire and brand marks

Concept artwork may parody public people or reference brands. **Parody is context-dependent; labelling something “satire” does not automatically make every use lawful.** Avoid implying a real person or company officially endorses, sponsors or is associated with the game. Review potentially sensitive promotional uses, especially merchandise, advertising and commercial releases, with a qualified legal professional.

The project is not officially affiliated with or endorsed by the people, bands, brands, organizations or publishers depicted or referenced, unless separately documented.

## 5. Contributions from friends and community

- Code changes submitted by pull request are accepted for consideration under the project’s **MIT license**; only submit code you have the right to license.
- Before contributing **art, audio, music, likenesses or writing**, open an issue and agree with the maintainer on acceptable asset licensing and attribution in writing. A pull request alone does not make third-party content legal to redistribute.
- Do not submit copied copyrighted art, store-bought packs without redistribution rights, trademark/logo copies, unauthorized voice clones, leaked sources, or AI outputs containing third-party protected material you cannot legally supply.
- Give provenance information (author, download page, license version, required attribution) and store relevant notices in the repository.
- A contribution is not an offer of paid work; any compensation or ownership terms must be agreed separately in writing **before** work begins.

See [CONTRIBUTING.md](../.github/CONTRIBUTING.md).

## 6. Before distributing a new release

- [ ] Identify all media, dependencies and third-party material actually **packaged in the release**, including imported models baked into Godot resources.
- [ ] Verify licenses and preserve required notices and attribution.
- [ ] Ensure no copyrighted commercial songs or unauthorized recordings are included.
- [ ] Confirm original artwork, music, scripts, voice and brand asset rights (or replace them).
- [ ] Audit public-figure likenesses, trademarks and promotional material for rights and implied endorsement.
- [ ] Confirm the release contains only materials permitted for distribution in that form.
- [ ] If monetizing or in doubt, obtain legal review.

This document describes the project's intended license boundaries and practical checks. It is **not a warranty, ownership certification or individualized legal opinion**.
