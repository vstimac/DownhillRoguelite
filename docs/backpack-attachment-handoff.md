# Backpack and clothing-fitting coordination

Active project: `D:/Game dev/downhill-roguelite-(copy)`, branch `fix/gameplay-math-audit`,
current inspected baseline `37d5e5b4` (Claude's opt-in cargo-box debug fix). Main is unchanged.

## Combined implementation

Claude's uncommitted fitting files were inspected in `D:/Game dev/release-wt` on
`feature/signposts-mouse`, inspected HEAD `34d2c89e`. Sixteen fitting files and script UIDs were
copied into the active project, then reconciled there with the native backpack attachment.
The source hashes are in `D:/Game dev/math-audit-artifacts/clothing-fit-source-manifest.json`.
The release worktree was not edited, applied to or merged. There is no direct connected channel
or acknowledgement from Claude; this document records the actual file coordination.

The active implementation preserves Setup(bool), ShowGear, Pieces, BaseShirtKey and the setup
guards. It also resolves the interactions identified during the backpack-only task:

- Both preview controllers receive ListenToGear=false before entering the tree, so their chosen
  outfit is independent of live equipment events.
- EnsureBasePack creates the cosmetic pack before Setup(false) returns and restores it after
  equipment removal. Its saved `base_pack` correction is applied on each creation.
- ClothingFitter/ClothingFitPart delegate a pack's hand fit to BackpackAttachment.ApplyAdjustment.
  The helper stores one correction around FitAnchor, the unadjusted pack centre. Refit recomposes
  automatic placement and that correction without stacking or changing the authored model root.
- CarrierWornGear retains character-specific fits, and CarrierModel supplies CharacterId.
  Referenced character/item resource classes retain Tool annotations for editor previews.

No animation, rig, IK, ragdoll, inventory ownership/capacity, survival or movement maths was
replaced. The earlier staged DevPlayground task and unrelated user changes remain separate.

## Validation status

The combined version builds with zero warnings and errors. Focused ClothingFitTests passes 18
checks; BackpackAttachmentTests passes 45; DevPlaygroundTests passes 32. Native base-outfit and
side pack views were inspected with capture every five frames. Final combined suite passed 51
scenes / 691 checks; DescentPlaytestTests failed on Godot's uninitialized GC handle during
PackedScene.Instantiate at `environment/SetPieceNode.cs:30`. No clothing-related cause is proven.
TerrainTests passed this run. Full log: `D:/Game dev/math-audit-artifacts/clothing-fit-combined-smoke.log`;
individual logs: `C:/Users/user/AppData/Local/Temp/tmp.npz5QJUzHS`. No commit on a failing suite.

That failed descent log also reports 317,784.8 m displacement in 30 simulated seconds (the older
recorded run reported 131.4 m). Its permissive finite/alive/minimum-distance assertion does not
rule out a position jump or runaway movement. Cause is unproven; diagnose with seed, teleport/fall
counts, peak velocity and largest per-tick displacement before treating it as healthy gameplay.

Previous backpack-only validation: 45 attachment checks passed, including actual walk/brake,
sprint, jump/landing, dive and native ragdoll/recovery on three installed characters. Its last
full smoke run passed 50 scenes / 659 checks and failed `debug/tests/TerrainTests.cs:272`:
28.7 ms vegetation generation against 16 ms. BeastTests passed that run. Log:
`D:/Game dev/math-audit-artifacts/backpack-motion-smoke.log`.

The earlier `D:/Game dev/math-audit-artifacts/backpack-attachment.patch` is a backpack-only review
artifact. Its historical apply-check succeeded against the inspected release working files;
it does not contain this later reconciliation. Do not apply it over newer combined working files
or sweep unrelated staged work into a commit. Refresh review artifacts after validation.

Validated review artifacts for the combined implementation:
`D:/Game dev/math-audit-artifacts/backpack-clothing-combined.patch` is against the active branch
baseline and excludes the separately staged playground/README work.
`D:/Game dev/math-audit-artifacts/clothing-fit-reconciliation.patch` is against Claude's inspected
working files. Its `git apply --check` passes in release-wt; it has not been applied there.

## Where to test

Latest readiness run: **53 scenes / 715 checks PASS**, build zero warnings/errors, after
streaming lifetime and terrain allocation changes. Log: `plant-optimized-full-smoke.log`.
The earlier failing runs above remain the investigation history, not the current suite status.

- Editor fitting workflow and copied/modified file list: [clothing-fitting.md](clothing-fitting.md).
- Runtime native attachment, bone mappings, offsets and limits: [backpack-attachment.md](backpack-attachment.md).
- Static editor scene: `debug/ClothingFitting.tscn`.
- Playable backpack lab: `debug/TestBackpackAttachment.tscn`, F6.

Rigid packs and several garments remain prototype art. Check extreme animation poses and
provide authored fit variants for different bodies; compatible bone names alone do not prove
that arbitrary meshes will fit. Production harness art and real cloth simulation remain separate.
