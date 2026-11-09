using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Pocket supplies must support the same actions as packed supplies, without losing unrelated items.</summary>
public partial class PocketUseTests : Node
{
    private CarrierInventory _inventory = null!;
    private int _failures;
    private string _consumed = "";
    private float _mended;

    public override async void _Ready()
    {
        _inventory = new CarrierInventory
        {
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = System.Array.Empty<string>(),
            AutoPackOnStart = false,
        };
        AddChild(_inventory);
        GameEvents.ItemConsumed += OnConsumed;
        GameEvents.Mended += OnMended;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        ItemDefinition ration = Item("ration");
        Prepare();
        _inventory.TryAdd(ration, -1.0f, false);
        GameEvents.RequestConsume(SupplyNeed.Hunger, 50.0f);
        Check("eat request finds food in a pocket", _consumed == ration.ItemId && !_inventory.AllItems.Contains(ration));

        Prepare();
        ItemDefinition water = new() { ItemId = "pocket_test_water", Category = ItemCategory.Water, Hydration = 20.0f };
        _inventory.TryAdd(water, -1.0f, false);
        GameEvents.RequestConsume(SupplyNeed.Thirst, 30.0f);
        Check("drink request finds water in a pocket", _consumed == water.ItemId && !_inventory.AllItems.Contains(water));

        Prepare();
        ItemDefinition kit = Item("sewing_kit");
        _inventory.TryAdd(kit, -1.0f, false);
        GameEvents.RequestUseItem(kit.ItemId);
        Check("hotbar use cannot delete a nonconsumable pocket item", _inventory.AllItems.Contains(kit)
            && _inventory.Equipment!.PocketItems.Contains(kit) && _consumed.Length == 0);

        Prepare();
        ItemDefinition pencil = Item("pencil");
        _inventory.TryAdd(pencil, -1.0f, false);
        GameEvents.RequestItemUse(pencil.ItemId);
        Check("using up a pocketed tool removes it exactly once", !_inventory.AllItems.Contains(pencil)
            && !_inventory.Equipment!.PocketItems.Contains(pencil));

        Prepare();
        _inventory.TryAdd(ration, -1.0f, false);
        EmptyPack();
        ItemDefinition large = new() { ItemId = "pocket_test_meal", Category = ItemCategory.Food, Nutrition = 80.0f };
        _inventory.TryAdd(large, -1.0f, false);
        GameEvents.RequestConsume(SupplyNeed.Hunger, 30.0f);
        Check("best fitting food is chosen across pack and pockets", _consumed == ration.ItemId && _inventory.AllItems.Contains(large));

        Prepare();
        _inventory.TryAdd(large, -1.0f, false);
        EmptyPack();
        _inventory.TryAdd(ration, -1.0f, false);
        GameEvents.RequestConsume(SupplyNeed.Hunger, 95.0f);
        Check("when nothing covers the deficit, the largest carried food is chosen", _consumed == large.ItemId
            && _inventory.AllItems.Contains(ration));

        Prepare();
        ItemDefinition firstCargo = new() { ItemId = "pocket_parcel", Category = ItemCategory.Cargo, CargoInstanceId = "pocket-job-a" };
        ItemDefinition secondCargo = new() { ItemId = "pocket_parcel", Category = ItemCategory.Cargo, CargoInstanceId = "pocket-job-b" };
        _inventory.TryAdd(firstCargo, -1.0f, false);
        _inventory.TryAdd(secondCargo, -1.0f, false);
        bool handed = _inventory.HandOverCargo(secondCargo.CargoInstanceId);
        Check("delivery removes the requested pocket parcel, preserving another of the same kind", handed
            && _inventory.OwnsCargo(firstCargo.CargoInstanceId) && !_inventory.OwnsCargo(secondCargo.CargoInstanceId));
        Check("a pocket parcel cannot be handed over twice", !_inventory.HandOverCargo(secondCargo.CargoInstanceId));

        Prepare();
        _inventory.TryAdd(kit, -1.0f, false);
        GameEvents.PublishShelter(true, 1);
        _inventory.Equipment!.WearDown(EquipSlot.Torso, 40.0f);
        bool repaired = _inventory.Equipment.Mend(kit.ItemId);
        Check("mending can use a material from a pocket", repaired && !_inventory.AllItems.Contains(kit)
            && Near(_inventory.Equipment.DurabilityOf(EquipSlot.Torso), Item("oilskin_coat").MaxDurability));

        Prepare();
        EmptyPack();
        _inventory.TryAdd(kit, -1.0f, false);
        GameEvents.PublishShelter(true, 1);
        _inventory.Equipment!.WearDown(EquipSlot.Torso, 40.0f);
        float before = _inventory.Equipment.DurabilityOf(EquipSlot.Torso);
        _inventory.Equipment.Mend(kit.ItemId);
        float restored = _inventory.Equipment.DurabilityOf(EquipSlot.Torso) - before;
        Check("mending reports actual restoration rather than the kit's uncapped amount", restored > 0.0f && Near(_mended, restored));

        GameEvents.ItemConsumed -= OnConsumed;
        GameEvents.Mended -= OnMended;
        GameEvents.PublishShelter(false, 0);
        GD.Print($"POCKET USE TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void Prepare()
    {
        GameEvents.PublishRespawnRequest();
        GameEvents.PublishShelter(false, 0);
        _consumed = "";
        _mended = 0.0f;
        ItemDefinition coat = Item("oilskin_coat");
        _inventory.TryAdd(coat, -1.0f, false);
        _inventory.Equipment!.Wear(coat.ItemId);
        _inventory.TryAdd(new ItemDefinition { ItemId = "pocket_test_filler", SizeCells = _inventory.Grid!.Size, MassKg = 0.0f }, -1.0f, false);
    }

    private void EmptyPack() => _inventory.Consume(_inventory.Grid!.Items[0]);
    private void OnConsumed(SupplyNeed need, string id, float restore) => _consumed = id;
    private void OnMended(string name, float restored) => _mended = restored;
    private static ItemDefinition Item(string id) => GD.Load<ItemDefinition>($"res://inventory/items/{id}.tres");
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
