using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M14 gear: clothing goes from the pack onto the body and back, keeping its
/// hit points; worn gear adds warmth, rain protection, grip and wind catch, and a worn-through
/// piece adds nothing; falls wear everything down; mending only works at a station, uses up the
/// material and mends the most worn piece it can; a torn pack loses an item at a fall; pockets take
/// small items when the pack is full; a new run clears what's worn and restores the pack.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/GearTests.tscn
/// </summary>
public partial class GearTests : Node
{
    [Export] public CarrierInventory? Inventory { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<string> _lost = new();
    private float _mended;

    public override async void _Ready()
    {
        CarrierEquipment? gear = Inventory?.Equipment;
        if (Inventory == null || gear == null)
        {
            GD.PushError("GearTests: Inventory (with its Equipment) must be assigned.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.ItemLost += OnLost;
        GameEvents.Mended += OnMended;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        TestWearAndRemove(Inventory, gear);
        TestEffects(Inventory, gear);
        TestFallWear(Inventory, gear);
        TestMending(Inventory, gear);
        TestTornPack(Inventory, gear);
        TestPockets(Inventory, gear);
        await TestNewRun(Inventory, gear);

        GameEvents.ItemLost -= OnLost;
        GameEvents.Mended -= OnMended;
        GameEvents.PublishShelter(false, 0);
        if (_failures.Count == 0)
        {
            GD.Print("GEAR TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("GEAR TESTS: FAIL");
            GetTree().Quit(1);
        }
    }

    private void OnLost(string itemId) => _lost.Add(itemId);

    private void OnMended(string what, float amount) => _mended += amount;

    private static ItemDefinition Item(string id)
    {
        return GD.Load<ItemDefinition>($"res://inventory/items/{id}.tres");
    }

    private void Check(bool ok, string pass, string fail)
    {
        if (ok)
        {
            GD.Print("PASS: " + pass);
        }
        else
        {
            _failures.Add(fail);
        }
    }

    private static int Carried(CarrierInventory inventory, string id)
    {
        int count = 0;
        foreach (ItemDefinition item in inventory.AllItems)
        {
            if (item.ItemId == id)
            {
                count++;
            }
        }
        return count;
    }

    private static void Reset(CarrierInventory inventory)
    {
        GameEvents.PublishShelter(false, 0);
        GameEvents.PublishRespawnRequest();
        inventory.Clear();
    }

    private void TestWearAndRemove(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        ItemDefinition hat = Item("wool_hat");
        inventory.TryAdd(hat, -1.0f, false);
        GameEvents.RequestWear("wool_hat");
        Check(gear.WornIn(EquipSlot.Head) == hat && Carried(inventory, "wool_hat") == 0,
            "wearing a hat takes it out of the pack onto the head",
            $"hat not worn (worn: {gear.WornIn(EquipSlot.Head)?.ItemId ?? "nothing"}, still carried: {Carried(inventory, "wool_hat")})");
        Check(GameEvents.CurrentGear.In(EquipSlot.Head).ItemId == "wool_hat",
            "the gear snapshot shows the hat", "the gear snapshot doesn't show the hat");

        gear.WearDown(EquipSlot.Head, 20.0f);
        float worn = gear.DurabilityOf(EquipSlot.Head);
        GameEvents.RequestTakeOff(EquipSlot.Head);
        float packed = -2.0f;
        foreach (PackedItem item in inventory.Grid!.Items)
        {
            if (item.Definition == hat)
            {
                packed = item.Durability;
            }
        }
        Check(gear.WornIn(EquipSlot.Head) == null && Mathf.IsEqualApprox(packed, worn),
            $"taking the hat off puts it back in the pack with its wear ({packed:0}/{hat.MaxDurability:0})",
            $"take-off lost the hat or its wear (worn {worn}, packed {packed})");

        GameEvents.RequestWear("wool_hat");
        Check(Mathf.IsEqualApprox(gear.DurabilityOf(EquipSlot.Head), worn),
            "wearing it again keeps the wear", $"re-worn hat has {gear.DurabilityOf(EquipSlot.Head)}, expected {worn}");

        inventory.TryAdd(Item("sewing_kit"), -1.0f, false);
        bool food = gear.Wear("sewing_kit");
        Check(!food, "a material can't be worn", "a sewing kit was worn");
    }

    private void TestEffects(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        Check(GameEvents.CurrentGear.Warmth == 0.0f && GameEvents.CurrentGear.Grip == 0.0f,
            "nothing worn gives no warmth or grip", "bare carrier has warmth or grip");
        foreach (string id in new[] { "wool_hat", "oilskin_coat", "hobnail_boots", "fur_cape" })
        {
            inventory.TryAdd(Item(id), -1.0f, false);
            gear.Wear(id);
        }
        GearSnapshot dressed = GameEvents.CurrentGear;
        Check(dressed.Warmth > 0.1f && dressed.RainProof >= 0.5f && dressed.Grip >= 0.15f && dressed.WindCatch > 0.3f,
            $"dressed: warmth {dressed.Warmth:0.00}, rain {dressed.RainProof:0.00}, grip +{dressed.Grip:0.00}, wind catch {dressed.WindCatch:0.00}",
            $"gear effects missing: warmth {dressed.Warmth}, rain {dressed.RainProof}, grip {dressed.Grip}, wind {dressed.WindCatch}");
        Check(dressed.Warmth <= gear.MaxWarmth, "warmth is capped", $"warmth {dressed.Warmth} above the cap {gear.MaxWarmth}");

        gear.WearDown(EquipSlot.Feet, 1000.0f);
        GearSnapshot broken = GameEvents.CurrentGear;
        Check(broken.Grip == 0.0f && broken.In(EquipSlot.Feet).Broken,
            "worn-through boots give no grip", $"broken boots still give grip {broken.Grip}");
    }

    private void TestFallWear(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        inventory.TryAdd(Item("wool_trousers"), -1.0f, false);
        gear.Wear("wool_trousers");
        float before = gear.DurabilityOf(EquipSlot.Legs);
        float pack = gear.BackpackDurability;
        GameEvents.PublishDowned(true);
        GameEvents.PublishDowned(false);
        Check(gear.DurabilityOf(EquipSlot.Legs) < before && gear.BackpackDurability < pack,
            $"a fall wears clothes ({before:0} to {gear.DurabilityOf(EquipSlot.Legs):0}) and the pack ({pack:0} to {gear.BackpackDurability:0})",
            "a fall didn't wear the gear");
        float soft = gear.DurabilityOf(EquipSlot.Legs);
        GameEvents.PublishLanded(gear.HardLanding - 1.0f);
        float afterSoft = gear.DurabilityOf(EquipSlot.Legs);
        GameEvents.PublishLanded(gear.HardLanding + 4.0f);
        Check(Mathf.IsEqualApprox(afterSoft, soft) && gear.DurabilityOf(EquipSlot.Legs) < afterSoft,
            "soft landings cost nothing, hard landings wear gear", "landing wear is wrong");
    }

    private void TestMending(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        inventory.TryAdd(Item("oilskin_coat"), -1.0f, false);
        gear.Wear("oilskin_coat");
        gear.WearDown(EquipSlot.Torso, 60.0f);
        gear.WearDown(EquipSlot.None, 20.0f);
        inventory.TryAdd(Item("sewing_kit"), -1.0f, false);
        float coat = gear.DurabilityOf(EquipSlot.Torso);

        bool outside = gear.Mend("sewing_kit");
        Check(!outside && Carried(inventory, "sewing_kit") == 1 && Mathf.IsEqualApprox(gear.DurabilityOf(EquipSlot.Torso), coat),
            "mending out on the slope does nothing", "mended without shelter");

        GameEvents.PublishShelter(true, 1);
        _mended = 0.0f;
        GameEvents.RequestMend("sewing_kit");
        Check(gear.DurabilityOf(EquipSlot.Torso) > coat && Carried(inventory, "sewing_kit") == 0 && _mended > 0.0f,
            $"at a station the sewing kit mends the most worn piece, the coat ({coat:0} to {gear.DurabilityOf(EquipSlot.Torso):0}), and is used up",
            $"mending failed (coat {coat} to {gear.DurabilityOf(EquipSlot.Torso)}, kits left {Carried(inventory, "sewing_kit")})");

        float pack = gear.BackpackDurability;
        inventory.TryAdd(Item("cord"), -1.0f, false);
        gear.Mend("cord");
        Check(gear.BackpackDurability > pack, "cord mends the pack", "cord didn't mend the pack");

        // The pack is whole again and the coat is cloth: a leather patch has nothing to mend.
        inventory.TryAdd(Item("leather_patch"), -1.0f, false);
        float coatNow = gear.DurabilityOf(EquipSlot.Torso);
        bool patched = gear.Mend("leather_patch");
        Check(!patched && Mathf.IsEqualApprox(gear.DurabilityOf(EquipSlot.Torso), coatNow) && Carried(inventory, "leather_patch") == 1,
            "a leather patch doesn't mend a cloth coat and isn't used up", $"leather mended cloth (patched: {patched})");
        GameEvents.PublishShelter(false, 0);
    }

    private void TestTornPack(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        inventory.TryAdd(Item("sewing_kit"), -1.0f, false);
        inventory.TryAdd(Item("cord"), -1.0f, false);
        GameEvents.PublishDowned(true);
        GameEvents.PublishDowned(false);
        Check(_lost.Count == 0 && inventory.AllItems.Count == 2, "a whole pack keeps everything at a fall", "a whole pack lost an item");
        gear.WearDown(EquipSlot.None, 1000.0f);
        GameEvents.PublishDowned(true);
        GameEvents.PublishDowned(false);
        Check(_lost.Count == 1 && inventory.AllItems.Count == 1, "a torn pack loses an item at a fall", $"torn pack lost {_lost.Count} items");
    }

    private void TestPockets(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        ItemDefinition kit = Item("sewing_kit");
        int added = 0;
        while (added < 2000 && inventory.TryAdd(kit, -1.0f, false))
        {
            added++;
        }
        Check(gear.PocketItems.Count == 0, "with no pockets a full pack takes nothing more", "pocketed without pockets");

        // Make room for the coat, wear it, then fill its pockets.
        int freed = 0;
        BackpackGrid grid = inventory.Grid!;
        while (freed < 200 && Carried(inventory, "oilskin_coat") == 0 && grid.Items.Count > 0)
        {
            inventory.Consume(grid.Items[0]);
            freed++;
            inventory.TryAdd(Item("oilskin_coat"), -1.0f, false);
        }
        gear.Wear("oilskin_coat");
        int extra = 0;
        while (extra < 200 && inventory.TryAdd(kit, -1.0f, false))
        {
            extra++;
        }
        Check(gear.PocketCapacity() == 2 && gear.PocketItems.Count == 2,
            "the coat's two pockets take small items once the pack is full",
            $"pockets: capacity {gear.PocketCapacity()}, used {gear.PocketItems.Count}");
        Check(!inventory.TryAdd(Item("oilskin_coat"), -1.0f, false), "a coat doesn't fit a pocket", "a coat went into a pocket");
        Check(!gear.TakeOff(EquipSlot.Torso) && gear.WornIn(EquipSlot.Torso) != null,
            "the coat stays on when there's no room in the pack", "coat came off into a full pack");

        int before = gear.PocketItems.Count;
        Check(gear.TakeFromPocket("sewing_kit", out ItemDefinition? taken) && taken == kit && gear.PocketItems.Count == before - 1,
            "items come back out of a pocket", "couldn't take an item out of a pocket");
    }

    private async Task TestNewRun(CarrierInventory inventory, CarrierEquipment gear)
    {
        Reset(inventory);
        inventory.TryAdd(Item("leather_gloves"), -1.0f, false);
        gear.Wear("leather_gloves");
        gear.WearDown(EquipSlot.None, 50.0f);
        GameEvents.PublishRespawnRequest();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(gear.WornIn(EquipSlot.Hands) == null && Mathf.IsEqualApprox(gear.BackpackDurability, gear.BackpackMaxDurability)
            && GameEvents.CurrentGear.In(EquipSlot.Hands).Empty,
            "a new run starts with nothing worn and a whole pack", "gear carried over into a new run");
    }
}
