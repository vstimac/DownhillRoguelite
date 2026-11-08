using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>Pure preflight: failed swaps and lost pocket capacity never modify or discard owned copies.</summary>
internal static class EquipmentTransfer
{
    public static bool Plan(CarrierInventory inventory, IReadOnlyList<StoredItem> currentPockets,
        ItemDefinition? incoming, StoredItem? outgoing, int capacity, int maxPocketCells,
        out BackpackGrid grid, out List<StoredItem> loose, out List<StoredItem> pockets, out StoredItem selected)
    {
        grid = new BackpackGrid(inventory.Grid!.Size);
        loose = new List<StoredItem>(inventory.LooseCopies);
        pockets = new List<StoredItem>(currentPockets);
        selected = default;
        bool found = incoming == null;
        foreach (PackedItem copy in inventory.Grid.Items)
        {
            if (!found && copy.Definition == incoming)
            {
                selected = new StoredItem(copy.Definition, copy.Durability, copy.Appearance, copy.InstanceId);
                found = true;
                continue;
            }
            PackedItem placed = grid.Place(copy.Definition, copy.Origin, copy.Orientation)!;
            placed.Durability = copy.Durability;
            placed.Appearance = copy.Appearance;
            placed.InstanceId = copy.InstanceId;
        }
        if (!found) { found = Extract(loose, incoming!, out selected); }
        if (!found) { found = Extract(pockets, incoming!, out selected); }
        if (!found) { return false; }
        if (outgoing.HasValue && !Store(grid, pockets, outgoing.Value, capacity, maxPocketCells)) { return false; }
        while (pockets.Count > capacity)
        {
            StoredItem spill = pockets[^1];
            pockets.RemoveAt(pockets.Count - 1);
            if (!Pack(grid, spill)) { return false; }
        }
        return true;
    }

    private static bool Extract(List<StoredItem> list, ItemDefinition definition, out StoredItem item)
    {
        int index = list.FindIndex(copy => copy.Definition == definition);
        item = default;
        if (index < 0) { return false; }
        item = list[index];
        list.RemoveAt(index);
        return true;
    }

    private static bool Store(BackpackGrid grid, List<StoredItem> pockets, StoredItem item, int capacity, int maxCells)
    {
        if (Pack(grid, item)) { return true; }
        Vector3I size = item.Definition.SizeCells;
        if (pockets.Count >= capacity || size.X <= 0 || size.Y <= 0 || size.Z <= 0
            || (double)size.X * size.Y * size.Z > maxCells) { return false; }
        pockets.Add(item);
        return true;
    }

    private static bool Pack(BackpackGrid grid, StoredItem copy)
    {
        if (!AutoPacker.TryBestSpot(grid, copy.Definition, 0.5f, out Vector3I at, out int orientation)) { return false; }
        PackedItem item = grid.Place(copy.Definition, at, orientation)!;
        item.Durability = copy.Durability;
        item.Appearance = copy.Appearance;
        item.InstanceId = copy.InstanceId;
        return true;
    }
}
