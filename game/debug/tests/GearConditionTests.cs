using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Per-copy wear and owned item counts must survive every container transition.</summary>
public partial class GearConditionTests : Node3D
{
    private CarrierInventory _inventory = null!;
    private ItemDefinition _gloves = null!;
    private int _failures;

    public override async void _Ready()
    {
        _inventory = new CarrierInventory
        {
            Name = "Inventory",
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = System.Array.Empty<string>(),
            AutoPackOnStart = false,
        };
        _gloves = new ItemDefinition { ItemId = "condition_gloves", Category = ItemCategory.Clothing,
            Slot = EquipSlot.Hands, MaxDurability = 100.0f, MassKg = 0.4f };
        AddChild(_inventory);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        PackedItem original = Packed();
        _inventory.Unpack(original);
        PackedItem? replaced = _inventory.Pack(_gloves, original.Origin, original.Orientation);
        Check("unpacking and packing preserve wear", replaced != null && Near(replaced.Durability, 13.0f));

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        original = Packed();
        _inventory.Unpack(original);
        _inventory.Unpack(original);
        Check("a stale unpack cannot duplicate a loose item", _inventory.LooseItems.Count == 1 && _inventory.AllItems.Count == 1);

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        _inventory.TryAdd(_gloves, 27.0f, false);
        original = Packed();
        _inventory.Consume(original);
        _inventory.Consume(original);
        Check("a stale consume cannot remove another copy's ownership", _inventory.AllItems.Count == 1 && _inventory.Grid!.Items.Count == 1);

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        _inventory.TryAdd(_gloves, 27.0f, false);
        _inventory.Repack(new List<ItemDefinition>(_inventory.AllItems), 0.5f);
        float[] wear = _inventory.Grid!.Items.Select(i => i.Durability).OrderBy(v => v).ToArray();
        Check("auto-packing preserves the condition of duplicate copies", wear.Length == 2 && Near(wear[0], 13.0f) && Near(wear[1], 27.0f));

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        _inventory.Repack(new[] { _gloves, _gloves, _gloves }, 0.5f);
        Check("repacking cannot create copies beyond those owned", _inventory.Grid!.Items.Count == 1 && _inventory.AllItems.Count == 1);

        PreparePocket(13.0f);
        _inventory.Repack(new List<ItemDefinition>(_inventory.AllItems), 0.5f);
        int physical = _inventory.Grid!.Items.Count(p => p.Definition == _gloves)
            + _inventory.LooseItems.Count(d => d == _gloves) + _inventory.Equipment!.PocketItems.Count(d => d == _gloves);
        Check("repacking an owned-item list cannot duplicate a pocket item", physical == 1 && _inventory.AllItems.Count(d => d == _gloves) == 1);

        PreparePocket(13.0f);
        _inventory.Clear();
        Check("clearing owned items clears their pockets too", _inventory.AllItems.Count == 0 && _inventory.Equipment!.PocketItems.Count == 0);

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        GameEvents.RequestDrop(_gloves.ItemId);
        bool picked = _inventory.LastDropped!.Interact(this);
        Check("dropping and picking up gear preserves wear", picked && Near(Packed().Durability, 13.0f));

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        _inventory.Unpack(Packed());
        GameEvents.RequestDrop(_gloves.ItemId);
        picked = _inventory.LastDropped!.Interact(this);
        Check("dropping an unpacked item preserves wear", picked && Near(Packed().Durability, 13.0f));

        Reset();
        _inventory.TryAdd(_gloves, 13.0f, false);
        GameEvents.RequestThrow(_gloves.ItemId);
        picked = _inventory.LastDropped!.Interact(this);
        Check("throwing and picking up gear preserves wear", picked && Near(Packed().Durability, 13.0f));

        foreach (float durability in new[] { 13.0f, 0.0f })
        {
            PreparePocket(durability);
            bool worn = _inventory.Equipment!.Wear(_gloves.ItemId);
            Check($"wearing pocketed gear keeps {durability:0} hit points", worn && Near(_inventory.Equipment.DurabilityOf(EquipSlot.Hands), durability));
        }

        PreparePocket(13.0f);
        _inventory.Consume(_inventory.Grid!.Items[0]);
        bool removed = _inventory.Equipment!.TakeOff(EquipSlot.Torso);
        Check("a pocket spilling into the backpack preserves wear", removed && Near(Packed().Durability, 13.0f));

        PreparePocket(13.0f);
        GameEvents.RequestDrop(_gloves.ItemId);
        picked = _inventory.LastDropped!.Interact(this);
        bool dressed = _inventory.Equipment!.Wear(_gloves.ItemId);
        Check("pocket-to-world-to-pocket transfers preserve wear", picked && dressed && Near(_inventory.Equipment.DurabilityOf(EquipSlot.Hands), 13.0f));

        GD.Print($"GEAR CONDITION TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void Reset() => GameEvents.PublishRespawnRequest();
    private PackedItem Packed() => _inventory.Grid!.Items.First(i => i.Definition == _gloves);

    private void PreparePocket(float durability)
    {
        Reset();
        ItemDefinition coat = GD.Load<ItemDefinition>("res://inventory/items/oilskin_coat.tres");
        _inventory.TryAdd(coat, -1.0f, false);
        _inventory.Equipment!.Wear(coat.ItemId);
        _inventory.TryAdd(new ItemDefinition { ItemId = "condition_filler", SizeCells = _inventory.Grid!.Size, MassKg = 0.0f }, -1.0f, false);
        _inventory.TryAdd(_gloves, durability, false);
    }

    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.001f;
    private void Check(string name, bool passed)
    {
        if (passed)
        {
            GD.Print("PASS  " + name);
        }
        else
        {
            GD.Print("FAIL  " + name);
            _failures++;
        }
    }
}
