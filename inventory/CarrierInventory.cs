using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// The carrier's backpack and the items not in it. Publishes a LoadSnapshot through GameEvents
/// whenever the packing changes; balance reads that. Lives on the player.
/// </summary>
public partial class CarrierInventory : Node
{
    [Export] public BackpackDefinition? Backpack { get; set; }

    /// <summary>Items the carrier owns at the start (ItemDefinition resources).</summary>
    [Export] public string[] StartingItemPaths { get; set; } =
    {
        "res://inventory/items/sleeping_bag.tres",
        "res://inventory/items/tent.tres",
        "res://inventory/items/rope.tres",
        "res://inventory/items/cookware.tres",
        "res://inventory/items/food_bag.tres",
        "res://inventory/items/stove.tres",
        "res://inventory/items/water_bottle.tres",
        "res://inventory/items/med_kit.tres",
        "res://inventory/items/fuel_canister.tres",
        "res://inventory/items/ore_sample.tres",
    };

    /// <summary>Pack the starting items automatically, heaviest first, low and centred.</summary>
    [Export] public bool AutoPackOnStart { get; set; } = true;

    public BackpackGrid? Grid { get; private set; }
    public IReadOnlyList<ItemDefinition> LooseItems => _loose;
    public IReadOnlyList<ItemDefinition> AllItems => _all;

    private readonly List<ItemDefinition> _loose = new();
    private readonly List<ItemDefinition> _all = new();

    public override void _Ready()
    {
        if (Backpack == null)
        {
            GD.PushError("CarrierInventory: Backpack is not assigned.");
            return;
        }

        Grid = new BackpackGrid(Backpack.GridSize);
        foreach (string path in StartingItemPaths)
        {
            if (ResourceLoader.Load(path) is ItemDefinition item)
            {
                _all.Add(item);
                _loose.Add(item);
            }
            else
            {
                GD.PushWarning($"CarrierInventory: {path} is not an ItemDefinition.");
            }
        }

        if (AutoPackOnStart)
        {
            List<ItemDefinition> heaviestFirst = new(_loose);
            heaviestFirst.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
            Repack(heaviestFirst, 0.5f);
        }
        else
        {
            CallDeferred(MethodName.Publish);
        }
    }

    public override void _ExitTree()
    {
        // Managed references keep resources alive until the GC runs; drop them with the node.
        _loose.Clear();
        _all.Clear();
        Grid?.Clear();
    }

    /// <summary>Packs a loose item; returns the placement, or null if it doesn't fit there.</summary>
    public PackedItem? Pack(ItemDefinition item, Vector3I origin, int orientation)
    {
        if (Grid == null || !_loose.Contains(item))
        {
            return null;
        }

        PackedItem? placed = Grid.Place(item, origin, orientation);
        if (placed != null)
        {
            _loose.Remove(item);
            Publish();
        }
        return placed;
    }

    /// <summary>Takes an item out of the pack; it becomes loose.</summary>
    public void Unpack(PackedItem item)
    {
        if (Grid == null)
        {
            return;
        }
        Grid.Remove(item);
        _loose.Add(item.Definition);
        Publish();
    }

    /// <summary>Empties the pack and auto-packs every item in the given order. Returns items that didn't fit.</summary>
    public List<ItemDefinition> Repack(IEnumerable<ItemDefinition> order, float preferredX)
    {
        List<ItemDefinition> unplaced = new();
        if (Grid == null)
        {
            return unplaced;
        }

        Grid.Clear();
        _loose.Clear();
        unplaced = AutoPacker.Pack(Grid, order, preferredX);
        _loose.AddRange(unplaced);
        Publish();
        return unplaced;
    }

    public void Publish()
    {
        if (Backpack != null && Grid != null)
        {
            GameEvents.PublishLoad(LoadCalculator.Calculate(Backpack, Grid));
        }
    }
}
