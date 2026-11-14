# Inventory design decisions

Decided 2026-11-06, after reviewing an outside proposal for a free-placement 3D inventory against the development plan. This supersedes the "shaped-item grid" wording in plan section 3 where they differ.

## Core principle

The player isn't managing inventory slots. They're managing a physical load strapped to a body descending a mountain. Packing is balance.

## Packing model: snapped 3D grid

- The backpack interior is a 3D grid of small cells (default 5 cm, an `[Export]` on the backpack definition). A 30 × 20 × 60 cm pack is 6 × 4 × 12 cells.
- Each item occupies a set of cells (its shape). For the first prototype, shapes are boxes measured in cells; irregular shapes come later.
- Items rotate in 90° steps on any axis (24 orientations). Some items restrict orientation, e.g. upright-only liquids.
- Fit check is a cell lookup: every cell the item would occupy must be inside the pack and empty. No physics queries are needed for packing.
- Items keep real positions and real centres of mass, so the snapped placement still feeds a continuous COM.
- Why not free placement: it's fiddly to manipulate with a mouse, hard to read what fits, and the validation through physics queries is fragile. The grid keeps the physical feel and makes packing readable.

## External cargo

- Outside the grid. External items connect to attachment points on the backpack (top, left, right, bottom, rear) and need a strap.
- External cargo is usually higher and further from the spine, so it costs more balance per kilo. That is the intended trade-off: carry more, be less stable.

## Inventory states

1. **Carrying:** during descent. A short press of Tab opens the quick look: the pack in 2.5D on a small sheet, weight and centre of mass, nothing can be moved, the world goes on. The player can stabilise, tighten straps or drop cargo.
2. **Packing:** anywhere, at the price of time. Holding Tab makes the carrier stop and set the pack down (1.5 s in the live world, cancelled by letting go or by a fall); then the packing page opens: grab / rotate / place items, live weight and COM. Closing it shoulders the pack again (another 1.5 s). The wait is what keeps repacking from being spammed in the middle of trouble; stations are simply the safe place to do it.
3. **Unpacked:** things taken out lie on the mat beside the pack, to swap and compare; whatever is still there when the pack goes back on is left out of the load.

Decided 2026-11-13 with the director: packing is no longer station-only (it was, in the first version), and the pack is shown in 2.5D (the real 3D grid seen at a slant, item models inked like the icons, the view turning in quarter steps and the upper layers cut away on the wheel) instead of a separate in-world camera with coloured boxes. The grid, the fit check and the load calculation did not change.

## Cargo simulation levels

| Level | When | Simulation |
| --- | --- | --- |
| Secured | Packed in the grid or strapped | Follows the pack. No physics. Contributes mass, COM, inertia. |
| Loose | Unsecured item (later) | Damped spring relative to its slot; sways on turns and stops. |
| Dropped | After a fall or deliberate drop | Becomes a `RigidBody3D`, inherits the player's velocity, can be picked up again. |

Never simulate every carried item as a rigid body during normal movement.

## Mass properties and balance

- Recalculated only when the load changes: total mass, load COM and inertia, in backpack space, then converted to character space through the backpack mount transform. Don't mix coordinate spaces.
- Published to balance as an event / through RunState. Inventory never moves the character.
- Balance uses the **load's** COM relative to the hips/feet, not a combined body + load COM, which a 70 kg body would dilute.
- Height must matter. The balance model (M2) needs torque terms that grow with load height, e.g. slope tilt ≈ m·g·h·sin(θ) and braking ≈ m·a·h, plus the lateral offset. A lateral-only lean model is not enough.

## Item definition (Resource)

Fields for the first version: `ItemId`, `DisplayName`, `MassKg`, shape in cells, `LocalCenterOfMass` (defaults to the shape's centre), tags as a flags enum (`Fragile`, `Liquid`, `Bulky`, `Valuable`), fragility, orientation rule, can-attach-externally, optional `WorldScene` (`PackedScene?`).

Code notes: pure maths (mass, COM, fit checks) lives in static classes, not Nodes. Data that doesn't need Godot is plain C#, not `RefCounted`. Optional resource references are nullable.

## Equipment scope

First version: backpack, boots, poles, the gear that changes balance and footing. Clothing, gloves and head gear come later, because each one adds art.

## Inventory track

These run as standalone debug scenes alongside the main milestones and connect to balance once M2 exists. Labelled I1–I6 so they don't clash with M1–M10.

| Phase | Scope | Done when |
| --- | --- | --- |
| I1 Packing prototype | Grid backpack in a debug scene, 10 primitive items, grab / move / rotate with snapping, fit check, COM marker, weight readout | 10 different items packed without overlap; moving a heavy item visibly moves the COM marker |
| I2 Mass to balance | Load mass properties published to the balance system (needs M2) | The A-vs-B test below passes |
| I3 Physical cargo | Straps, external attachment points, drop and scatter, pick up again | Cargo reacts to a fall and can be recovered |
| I4 Equipment | Backpack, boots, poles; stats feed movement and balance | Equipment changes both feel and appearance |
| I5 Camp interaction | Set pack down, packing camera, diegetic info, save/restore layout | The whole load can be managed at a camp |
| I6 Integration | Loot, rarity, contracts, upgrades (bigger grids, pockets, straps that lock items) | Inventory works inside a run |

## Success test (A vs B)

Two players carry exactly the same items. Player A packs heavy items high and to one side; Player B packs them low and close to the back. Player B must have a noticeably easier time descending a steep slope. If that isn't obvious in play, the core idea isn't working yet.
