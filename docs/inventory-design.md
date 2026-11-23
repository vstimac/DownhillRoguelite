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

One key, one pack: Tab on the keyboard, Y on a controller (`inventory_toggle_packing`); I (`inventory_open`) is the same without holding.

1. **Carrying:** during descent. A short press of Tab opens the quick look: the pack in 2.5D on a small sheet, weight and centre of mass, a reminder that the hotbar eats and drinks on the move; nothing can be moved, the world goes on and the carrier keeps walking. The player can stabilise, tighten straps or drop cargo.
2. **Packing:** anywhere, at the price of time. Holding Tab makes the carrier set the pack down: a ring fills round the key's name (1.2 s in the live world). Letting go before it is full leaves the pack on the back and nothing changes. I does the same by itself (I again calls it off). Then the one pack page opens, docked at the right: the pack (grab / rotate / place, live weight and COM) and the mat on its left half; what is worn, the pockets and the chosen thing's description with Eat / Drink / Wear / Mend / Drop on the right; with nothing chosen it shows the carrier's figures. The camera comes down over the carrier's shoulder onto the pack on the ground, so worn gear changes on the carrier in view, and time goes on (an option can stop it in single player; co-op never stops). Tab, I, Esc or Done shoulders the pack again (1.0 s).
   - **Nothing locks the carrier longer than it looks.** Walking or jumping at any moment ends it at once: a set-down is called off, an open or half-shouldered pack is on the back immediately while its picture swings up as the carrier walks (`PackSetDown`, 0.35 s). A press during a transition is kept: holding the key while the pack is being shouldered turns it back to the ground from where it is, a tap then gives the quick look once it is on, and I pressed mid-fall or mid-slide sets the pack down as soon as the carrier stands (kept 1 s). `PackViewTests` measures this in frames.
   - The wait is what keeps repacking from being spammed in the middle of trouble; stations are simply the safe place to do it.
3. **Unpacked:** things taken out lie on the mat beside the pack, to swap and compare; whatever is still there when the pack goes back on is left out of the load.

Decided 2026-11-13 with the director: packing is no longer station-only (it was, in the first version), and the pack is shown in 2.5D (the real 3D grid seen at a slant, item models inked like the icons, the view turning in quarter steps and the upper layers cut away on the wheel) instead of a separate in-world camera with coloured boxes. The grid, the fit check and the load calculation did not change.

Changed 2026-11-23 ("one key, one pack"): the separate inventory page is gone; I opens the pack page by setting the pack down, and the page took over its description, actions and carrier figures. The world no longer stops for sorting by default.

Added 2026-11-14: the inventory page (I) showed the same 2.5D pack at its left, looking only (replaced 2026-11-23, above). While the pack is off the back the real backpack model stands on the ground in front of the carrier, who crouches over it (`player/PackSetDown`, picture only). Models in the 2.5D view are laid along their block of cells and stretched a little (never far) so a full pack reads as full (`PackGeometry.FillBlock`).

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
