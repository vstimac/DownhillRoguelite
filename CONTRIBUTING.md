# Contributing to DownhillRoguelite

DownhillRoguelite is an indie Godot/C# survival roguelite in active development. **Friends, artists, developers, sound designers and beta testers are welcome.**

## How you can help

- **Godot 4 / C#:** gameplay, stability fixes, tooltips, UI and quality of life
- **3D / animation:** original stylized characters, clothing, backpacks, props, rigs and movement polish
- **Audio / narrative:** original, licensed audio, sound design and dialogue
- **QA:** play the latest beta and report reproducible bugs
- **Design / documentation:** balance feedback, route readability, onboarding, accessible UI

Current beta: [Releases](https://github.com/vstimac/DownhillRoguelite/releases). New contributors: start with [Issues](https://github.com/vstimac/DownhillRoguelite/issues) and open a proposal before investing time in a large feature.

## Getting started with code

1. Read [README](README.md), [CLAUDE.md](CLAUDE.md) and [BUILDING.md](docs/BUILDING.md).
2. Check open issues to avoid duplicate work. For bigger changes, describe the scope in an issue first.
3. Fork the repository, branch from `main` (or an explicitly agreed working branch).
4. Make one focused change, add or update documentation and run `dotnet build`.
5. For gameplay changes, use an appropriate Godot debug scene or the project's smoke tests when the required tools are installed.
6. Open a pull request with a short description, testing notes and links to the related issue.

**Do not commit:** credentials, API keys, proprietary game/music files, original Mixamo FBX source exports, private logs, third-party packs without redistribution permissions, or oversized local LLM models.

### Code conventions

- C# with explicit `if/else` blocks, one class per file, PascalCase public members.
- Use exported/configurable values rather than magic numbers for tunables.
- Prefer focused changes, tested incrementally; follow [CLAUDE.md](CLAUDE.md).

## Licensing of contributions

- **Code:** By contributing your own code, you agree to its distribution under the repository's [MIT license](LICENSE). Ensure you have the legal right to make that grant (including from an employer if applicable). Preserve third-party licensing and notices.
- **Art, audio, writing, voice, models:** Not automatically covered by MIT. Before uploading, clearly state the author, provenance, license and permitted game/marketing/source distribution uses. Seek agreement on the license and credits in the issue first. See [RIGHTS.md](RIGHTS.md).
- **Music:** Do not submit commercial tracks or samples without documented composition and recording permissions.
- **AI-generated content:** Disclose how it was created, confirm you may use it, and do not label it as real gameplay or a finished 3D asset when it is not.
- **Real people and brands:** No unauthorized impersonation, endorsements, recordings or logo copying; recognizable satirical depictions require review.

**Volunteer collaboration:** Contributing does not imply employment, compensation or transfer of unrelated rights. Any paid work or other special arrangement must be agreed directly and in writing **before** it starts.

## Good bug reports

Include beta version, operating system, steps to reproduce, expected vs actual behavior and a log or screenshot where useful. Do not paste private tokens, accounts or sensitive logs publicly.

## Need a task?

Use a [contribution proposal](https://github.com/vstimac/DownhillRoguelite/issues/new?template=help-wanted.md) to tell the project what you enjoy working on. Small tasks and good playtest reports are valuable.
