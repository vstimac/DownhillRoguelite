using System;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;
namespace DownhillRoguelite.Debug;

public partial class ModularEquipmentTests : Node3D
{
    private int _failures;
    public override void _Ready()
    {
        CarrierInventory inventory = new() { Name = "Inventory", Backpack = new BackpackDefinition(), StartingItemPaths = Array.Empty<string>() };
        AddChild(inventory);
        CarrierEquipment gear = inventory.Equipment!;
        ItemDefinition shirt = GD.Load<ItemDefinition>("res://inventory/items/trail_shirt.tres");
        ItemDefinition pack = GD.Load<ItemDefinition>("res://inventory/items/trail_backpack.tres");
        ItemAppearance appearance = new(0.4f, 0.2f, 1, new Color(0.3f, 0.5f, 0.3f));
        float emptyMass = CarriedLoad.Mass;
        inventory.TryAdd(shirt, 37, false, appearance);
        Check("a shirt equips from storage without losing wear or appearance", gear.Wear(shirt.ItemId)
            && gear.WornIn(EquipSlot.InnerTorso) == shirt && gear.DurabilityOf(EquipSlot.InnerTorso) == 37
            && gear.AppearanceOf(EquipSlot.InnerTorso) == appearance && inventory.AllItems.Count == 0);
        Check("equipping retains total carried weight", Mathf.IsEqualApprox(CarriedLoad.Mass, emptyMass + shirt.MassKg));
        inventory.TryAdd(pack, 70, false);
        Check("a backpack attaches in Back independently of the inner shirt", gear.Wear(pack.ItemId)
            && gear.WornIn(EquipSlot.Back) == pack && gear.WornIn(EquipSlot.InnerTorso) == shirt);
        Check("all ten unique slots publish safely with legacy aliases", EquipmentSlots.All.Distinct().Count() == 10
            && (int)EquipSlot.Torso == 2 && (int)EquipSlot.Cape == 6 && GameEvents.CurrentGear.Slots.Length == EquipmentSlots.Count);
        Check("taking off restores the same per-copy appearance", gear.TakeOff(EquipSlot.InnerTorso)
            && inventory.Grid!.Items.Any(copy => copy.Definition == shirt && copy.Durability == 37 && copy.Appearance == appearance));
        ItemDefinition invalid = new() { ItemId = "invalid_slot", Slot = (EquipSlot)99, MaxDurability = 10 };
        inventory.TryAdd(invalid, -1, false);
        int owned = inventory.AllItems.Count;
        Check("invalid slots leave ownership unchanged", !gear.Wear(invalid.ItemId) && inventory.AllItems.Count == owned);
        gear.TakeOff(EquipSlot.Back);
        inventory.Clear();
        foreach (EquipSlot slot in EquipmentSlots.All)
        {
            ItemDefinition piece = new() { ItemId = "slot_" + (int)slot, Slot = slot, MaxDurability = 10, MassKg = 0.1f };
            inventory.TryAdd(piece, -1, false);
            Check("slot " + slot + " equips and removes independently", gear.Wear(piece.ItemId) && gear.TakeOff(slot));
            inventory.Clear();
        }
        inventory.TryAdd(shirt, 37, false, appearance);
        inventory.TryAdd(pack, 70, false);
        gear.Wear(shirt.ItemId); gear.Wear(pack.ItemId);
        float mass = CarriedLoad.Mass;
        string savePath = "res://../math-audit-artifacts/equipment-regression.json";
        Check("equipment and carried storage save with validated resource identities", EquipmentPersistence.Save(inventory, savePath, out _));
        gear.TakeOff(EquipSlot.InnerTorso); gear.TakeOff(EquipSlot.Back); inventory.Clear();
        Check("load restores worn slots, condition and appearance", EquipmentPersistence.Load(inventory, savePath, out _)
            && gear.WornIn(EquipSlot.InnerTorso) == shirt && gear.DurabilityOf(EquipSlot.InnerTorso) == 37
            && gear.AppearanceOf(EquipSlot.InnerTorso) == appearance && Mathf.IsEqualApprox(CarriedLoad.Mass, mass));
        Check("loading the same snapshot twice cannot stack gear or weight", EquipmentPersistence.Load(inventory, savePath, out _)
            && Mathf.IsEqualApprox(CarriedLoad.Mass, mass) && inventory.AllItems.Count == 0);
        Check("missing saves leave the current equipment untouched", !EquipmentPersistence.Load(inventory, savePath + ".missing", out _)
            && gear.WornIn(EquipSlot.InnerTorso) == shirt && Mathf.IsEqualApprox(CarriedLoad.Mass, mass));
        Check("equipment modifiers are rebuilt once and broken items lose their benefit", Mathf.IsEqualApprox(GameEvents.CurrentGear.SpeedFactor, 0.98f));
        gear.WearDown(EquipSlot.Back, 1000);
        Check("broken clothing retains mass while losing modifiers", Mathf.IsEqualApprox(GameEvents.CurrentGear.SpeedFactor, 1)
            && Mathf.IsEqualApprox(CarriedLoad.Mass, mass));
        gear.TakeOff(EquipSlot.InnerTorso);
        GameEvents.RequestDrop("trail_shirt");
        Check("world drops and pickups preserve visual condition", inventory.LastDropped?.Appearance == appearance
            && inventory.LastDropped.Interact(this)
            && inventory.Grid!.Items.Any(copy => copy.Definition == shirt && copy.Appearance == appearance && copy.Durability == 37));
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private void Check(string name, bool pass)
    {
        if (pass) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
