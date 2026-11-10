using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Conservation of carried burden through packing, dressing, pockets and consumption.</summary>
public partial class CarriedMassTests : Node
{
    private int _failures;
    private CarrierInventory _inventory = null!;

    public override async void _Ready()
    {
        _inventory = new CarrierInventory
        {
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = System.Array.Empty<string>(),
            AutoPackOnStart = false,
        };
        AddChild(_inventory);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Reset();
        float empty = _inventory.Backpack!.EmptyMass;
        Check("empty carried burden is the backpack shell", Near(CarriedLoad.Mass, empty));
        ItemDefinition hat = Item("wool_hat");
        _inventory.TryAdd(hat, -1.0f, false);
        float packedMass = empty + hat.MassKg;
        Check("a packed item adds its actual mass", Near(CarriedLoad.Mass, packedMass));
        CarrierStamina stamina = new() { ProcessMode = ProcessModeEnum.Disabled };
        AddChild(stamina);
        float packedDrain = stamina.WalkDrainPerSecond(CarriedLoad.Mass, 0.0f, false, true);
        bool worn = _inventory.Equipment!.Wear(hat.ItemId);
        Check("wearing an item preserves carried mass", worn && Near(CarriedLoad.Mass, packedMass));
        Check("worn clothing is not counted as cargo on the back", Near(GameEvents.CurrentLoad.Mass, empty));
        Check("moving a hat onto the head does not reduce sprint effort",
            Near(stamina.WalkDrainPerSecond(CarriedLoad.Mass, 0.0f, false, true), packedDrain));
        _inventory.Equipment.WearDown(EquipSlot.Head, 1000.0f);
        Check("broken clothing still has mass", Near(CarriedLoad.Mass, packedMass));
        _inventory.Equipment.TakeOff(EquipSlot.Head);
        Check("taking clothing off preserves carried mass", Near(CarriedLoad.Mass, packedMass));

        Reset();
        ItemDefinition coat = Item("oilskin_coat");
        ItemDefinition ration = Item("ration");
        _inventory.TryAdd(coat, -1.0f, false);
        _inventory.Equipment.Wear(coat.ItemId);
        FillPack();
        bool pocketed = _inventory.TryAdd(ration, -1.0f, false);
        float dressedMass = empty + coat.MassKg + ration.MassKg;
        Check("food in a coat pocket contributes to carried burden", pocketed && _inventory.Equipment.PocketItems.Count == 1
            && Near(CarriedLoad.Mass, dressedMass));
        Check("pocket items do not move the backpack centre of mass", Near(GameEvents.CurrentLoad.Mass, empty));
        float beforeEating = CarriedLoad.Mass;
        GameEvents.RequestUseItem(ration.ItemId);
        Check("eating pocketed food removes exactly that item's mass", Near(beforeEating - CarriedLoad.Mass, ration.MassKg)
            && Near(CarriedLoad.Mass, empty + coat.MassKg));

        _inventory.TryAdd(ration, -1.0f, false);
        PackedItem filler = _inventory.Grid!.Items[0];
        _inventory.Consume(filler);
        float beforeTakeOff = CarriedLoad.Mass;
        bool removed = _inventory.Equipment.TakeOff(EquipSlot.Torso);
        Check("taking off a coat and moving its pockets into the pack conserves mass", removed
            && Near(beforeTakeOff, dressedMass) && Near(CarriedLoad.Mass, dressedMass));

        Reset();
        Check("a new run clears clothing and pocket burden", Near(CarriedLoad.Mass, empty));
        GD.Print($"CARRIED MASS TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void Reset()
    {
        GameEvents.PublishRespawnRequest();
        _inventory.Publish();
    }

    private void FillPack()
    {
        ItemDefinition filler = new() { ItemId = "mass_test_filler", SizeCells = _inventory.Grid!.Size, MassKg = 0.0f };
        _inventory.TryAdd(filler, -1.0f, false);
    }

    private void Check(string name, bool passed)
    {
        if (passed)
        {
            GD.Print("PASS  " + name);
        }
        else
        {
            GD.Print($"FAIL  {name}: carried {CarriedLoad.Mass:0.000} kg, pack {GameEvents.CurrentLoad.Mass:0.000} kg");
            _failures++;
        }
    }

    private static ItemDefinition Item(string id) => GD.Load<ItemDefinition>($"res://inventory/items/{id}.tres");
    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.001f;
}
