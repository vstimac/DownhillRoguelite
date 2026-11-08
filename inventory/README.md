# inventory

The backpack: a 3D grid of 5 cm cells, items as Resources (`items/`), the auto-packer, the load calculation that balance reads, and the carrier's `CarrierInventory`.

`interaction/` holds packing mode: setting the pack down, picking items up, the ghost preview and the in-world readout.

`LoadSnapshot` describes the packed backpack for balance and its readout. `GearSnapshot` carries worn and pocket mass separately; `core/CarriedLoad.Mass` sums them for physical effort, jumping, needs and route estimates. Moving gear onto the body does not make its weight disappear.

Read `docs/inventory-design.md` first.
