using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>Explicit carried-load snapshots. Validate the entire file before replacing any current equipment/storage.</summary>
public static class EquipmentPersistence
{
    private sealed record Copy(string Path, string Place, int Slot, int[] Cell, int Orientation, float Durability,
        float Wetness, float Dirt, int Variant, float[] Tint, string CargoId);
    private sealed record Snapshot(int Version, int[] Grid, float BackpackDurability, List<Copy> Items);

    public static bool Save(CarrierInventory inventory, string path, out string problem)
    {
        problem = "";
        if (inventory.Grid == null || inventory.Equipment == null) { problem = "Inventory is not ready"; return false; }
        try
        {
            List<Copy> copies = new();
            Copy Entry(StoredItem copy, string place, int slot, Vector3I cell, int orientation)
            {
                string source = copy.Definition.ResourcePath;
                if (copy.Definition.SourceItemPath.Length > 0) { source = copy.Definition.SourceItemPath; }
                if (source.Length == 0) { throw new InvalidDataException("Item has no persistent resource path: " + copy.Definition.ItemId); }
                ItemAppearance look = copy.Appearance;
                return new Copy(source, place, slot, new[] { cell.X, cell.Y, cell.Z }, orientation, copy.Durability,
                    look.Wetness, look.Dirt, look.Variant, new[] { look.Tint.R, look.Tint.G, look.Tint.B, look.Tint.A }, copy.Definition.CargoInstanceId);
            }
            foreach (PackedItem item in inventory.Grid.Items)
            { copies.Add(Entry(new StoredItem(item.Definition, item.Durability, item.Appearance), "pack", 0, item.Origin, item.Orientation)); }
            foreach (StoredItem item in inventory.LooseCopies) { copies.Add(Entry(item, "loose", 0, Vector3I.Zero, 0)); }
            foreach (StoredItem item in inventory.Equipment.PocketCopies) { copies.Add(Entry(item, "pocket", 0, Vector3I.Zero, 0)); }
            foreach ((EquipSlot slot, ItemDefinition item) in inventory.Equipment.WornItems)
            { copies.Add(Entry(new StoredItem(item, inventory.Equipment.DurabilityOf(slot), inventory.Equipment.AppearanceOf(slot)), "worn", (int)slot, Vector3I.Zero, 0)); }
            Vector3I size = inventory.Grid.Size;
            Snapshot snapshot = new(1, new[] { size.X, size.Y, size.Z }, inventory.Equipment.BackpackDurability, copies);
            string absolute = ProjectSettings.GlobalizePath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute + ".tmp", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(absolute + ".tmp", absolute, true);
            return true;
        }
        catch (Exception error) { problem = error.Message; return false; }
    }

    public static bool Load(CarrierInventory inventory, string path, out string problem)
    {
        problem = "";
        if (inventory.Grid == null || inventory.Equipment == null) { problem = "Inventory is not ready"; return false; }
        try
        {
            Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(ProjectSettings.GlobalizePath(path)))
                ?? throw new InvalidDataException("Empty equipment file");
            Vector3I size = inventory.Grid.Size;
            if (snapshot.Version != 1 || snapshot.Grid.Length != 3 || snapshot.Grid[0] != size.X || snapshot.Grid[1] != size.Y || snapshot.Grid[2] != size.Z)
            { throw new InvalidDataException("Unsupported equipment version or backpack dimensions"); }
            if (!float.IsFinite(snapshot.BackpackDurability) || snapshot.BackpackDurability < 0 || snapshot.BackpackDurability > inventory.Equipment.BackpackMaxDurability)
            { throw new InvalidDataException("Invalid backpack condition"); }
            BackpackGrid grid = new(size);
            List<StoredItem> loose = new(), pockets = new();
            Dictionary<EquipSlot, StoredItem> worn = new();
            HashSet<string> cargo = new();
            foreach (Copy entry in snapshot.Items)
            {
                if (!entry.Path.StartsWith("res://inventory/items/", StringComparison.Ordinal) || !ResourceLoader.Exists(entry.Path)
                    || ResourceLoader.Load(entry.Path) is not ItemDefinition definition)
                { throw new InvalidDataException("Missing item resource: " + entry.Path); }
                if (entry.CargoId.Length > 0)
                {
                    if (!cargo.Add(entry.CargoId)) { throw new InvalidDataException("Duplicate cargo identity"); }
                    definition = (ItemDefinition)definition.Duplicate();
                    definition.CargoInstanceId = entry.CargoId;
                    definition.SourceItemPath = entry.Path;
                }
                if (!float.IsFinite(entry.Durability) || entry.Durability < -1 || entry.Durability > definition.MaxDurability && definition.MaxDurability > 0
                    || !float.IsFinite(entry.Wetness) || entry.Wetness < 0 || entry.Wetness > 1
                    || !float.IsFinite(entry.Dirt) || entry.Dirt < 0 || entry.Dirt > 1 || entry.Variant < 0 || entry.Tint.Length != 4)
                { throw new InvalidDataException("Invalid item condition"); }
                foreach (float channel in entry.Tint)
                { if (!float.IsFinite(channel) || channel < 0 || channel > 1) { throw new InvalidDataException("Invalid colour"); } }
                StoredItem copy = new(definition, entry.Durability, new ItemAppearance(entry.Wetness, entry.Dirt, entry.Variant,
                    new Color(entry.Tint[0], entry.Tint[1], entry.Tint[2], entry.Tint[3])));
                if (entry.Place == "pack")
                {
                    if (entry.Cell.Length != 3 || entry.Orientation < 0 || entry.Orientation >= GridOrientations.All.Count) { throw new InvalidDataException("Invalid placement"); }
                    PackedItem? item = grid.Place(definition, new Vector3I(entry.Cell[0], entry.Cell[1], entry.Cell[2]), entry.Orientation);
                    if (item == null) { throw new InvalidDataException("Overlapping or out-of-bounds placement"); }
                    item.Durability = copy.Durability; item.Appearance = copy.Appearance;
                }
                else if (entry.Place == "loose") { loose.Add(copy); }
                else if (entry.Place == "pocket") { pockets.Add(copy); }
                else if (entry.Place == "worn")
                {
                    EquipSlot slot = (EquipSlot)entry.Slot;
                    if (!EquipmentSlots.Valid(slot) || definition.Slot != slot || definition.MaxDurability <= 0 || entry.Durability < 0 || !worn.TryAdd(slot, copy))
                    { throw new InvalidDataException("Invalid or repeated worn slot"); }
                }
                else { throw new InvalidDataException("Unknown item location"); }
            }
            long capacity = 0;
            foreach (StoredItem item in worn.Values) { capacity += Math.Max(item.Definition.Pockets, 0); }
            if (pockets.Count > capacity) { throw new InvalidDataException("Pocket contents exceed worn capacity"); }
            foreach (StoredItem item in pockets)
            {
                Vector3I box = item.Definition.SizeCells;
                if (box.X <= 0 || box.Y <= 0 || box.Z <= 0 || (double)box.X * box.Y * box.Z > inventory.Equipment.PocketMaxCells)
                { throw new InvalidDataException("Item does not fit a pocket"); }
            }
            inventory.Equipment.RestoreLoad(worn, snapshot.BackpackDurability, grid, loose, pockets);
            return true;
        }
        catch (Exception error) { problem = error.Message; return false; }
    }
}
