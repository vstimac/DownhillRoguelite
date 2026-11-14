# inventory

The backpack: a 3D grid of 5 cm cells, items as Resources (`items/`), the auto-packer, the load calculation that balance reads, and the carrier's `CarrierInventory`.

`interaction/` holds `PackingMode`: a short press of Tab asks for the quick look, holding it sets the pack down (which takes time), and only while the pack stands open do the placement requests go through. The pack is drawn by the pages in `ui/pack/` (`PackGridView`: the grid in 2.5D with item models; `PackQuickView`; `PackingScreen`), which read `GameEvents.CurrentPackLayout` (`core/PackLayout`) and ask through `GameEvents.RequestPackPlace` / `RequestPackTakeOut` / `RequestPackArrange`. `PackGeometry` is the shared pointing maths. Debug scene: `debug/TestPacking.tscn` (`-- --open-pack` or `-- --pack-look` for rendered checks); tests: `debug/tests/PackViewTests.tscn`.

`LoadSnapshot` describes the packed backpack for balance and its readout. `GearSnapshot` carries worn and pocket mass separately; `core/CarriedLoad.Mass` sums them for physical effort, jumping, needs and route estimates. Moving gear onto the body does not make its weight disappear.

Pocket supplies support eating/drinking, explicit tool consumption, mending and cargo hand-over. Food selection considers both containers; hand-over removes the exact cargo copy. Hotbar use leaves nonconsumables alone, and repair messages report actual restored hit points.

`StoredItem` retains per-copy durability in loose supplies and pockets. Packing, auto-arranging, dressing, dropping, throwing and picking up preserve it, including broken gear at zero durability. Repack orders can select only owned backpack/loose copies; stale or repeated entries cannot duplicate items or remove pocket contents.

Read `docs/inventory-design.md` first.

Modular clothing extends the same equipment manager with ten unique slots, atomic storage
preflight, stable physical copy identities and explicit carried-load persistence. Per-copy
appearance travels with durability through every container. See [the implementation and asset
pipeline](../docs/modular-clothing.md); open `debug/TestClothing.tscn` for a working workshop.
