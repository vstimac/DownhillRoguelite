# addons

Third-party Godot addons.

## nobodywho

Runs local language models (LLM chat, speech-to-text, text-to-speech, embeddings) inside Godot as a GDExtension. Installed by the director on 2026-11-06. Licence: `nobodywho/LICENSE`.

Its prebuilt libraries (`*.dll`, `*.so`, `*.dylib`, `*.pdb`, about 1.3 GB in total) are **not in git**: each is about 140 MB, over GitHub's 100 MB file limit. On a fresh clone run `bash scripts/fetch_nobodywho.sh` (Linux; `windows` or `all` for the others): it downloads the pinned 13.0.0 release, copies only the libraries into `addons/nobodywho/` (keeping the committed `nobodywho.gdextension`) and checks their SHA-256. Requires Godot 4.6 or newer. See docs/BUILDING.md.
