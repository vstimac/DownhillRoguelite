# Ground textures

Four original 1K JPG maps downloaded from Poly Haven, 2026-11-09. Unmodified source files;
`sources.json` records source URLs, byte counts and SHA-256 hashes (downloads were also checked
against Poly Haven's published MD5 and byte count).

- **Forest Ground 04**, Rob Tuytel (photography/processing), Rico Cilliers (minor adjustment):
  https://polyhaven.com/a/forest_ground_04 — diffuse and OpenGL normal, physical width 3.2 m.
- **Rock Ground**, Rob Tuytel: https://polyhaven.com/a/rock_ground — diffuse and OpenGL normal,
  physical width 1.5 m.

License: **CC0 1.0**. Poly Haven permits use, modification and redistribution, including in
commercial games: https://polyhaven.com/license and https://creativecommons.org/publicdomain/zero/1.0/

These are reusable material maps, not additional simulated geometry. Imported VRAM-compressed
sizes differ from the source JPEG sizes; no new exported package size has been measured.
The committed import settings enable mipmaps and VRAM compression; normal maps are explicitly
marked as normal data. Android texture formats and on-device tuning remain finishing work.
