using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// The carrier's backpack and the items not in it. Publishes a LoadSnapshot through GameEvents
/// whenever the packing changes; balance reads that. Takes items picked up in the world (TryAdd),
/// answers eat/drink requests with the best-fitting packed item, and empties when the carrier dies
/// (a new run starts with an empty pack). Lives on the player.
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

    /// <summary>Empty the pack when the carrier dies: a new run starts with nothing.</summary>
    [Export] public bool ClearOnDeath { get; set; } = true;
    /// <summary>Contract cargo with this many cells of other things (or padding) on every side counts as fully protected from knocks.</summary>
    [Export] public int ProtectedPaddingCells { get; set; } = 2;

    /// <summary>Side of the pack picked-up items are placed toward (0 left, 0.5 centre, 1 right).</summary>
    [Export] public float PickupPreferredX { get; set; } = 0.5f;

    public BackpackGrid? Grid { get; private set; }
    /// <summary>What the carrier wears (M14); created with the inventory.</summary>
    public CarrierEquipment? Equipment { get; private set; }
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
        Equipment = new CarrierEquipment { Name = "Equipment", Inventory = this };
        AddChild(Equipment);
        GameEvents.ConsumeRequested += OnConsumeRequested;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.ItemsGranted += OnItemsGranted;
        GameEvents.ItemUseRequested += OnItemUseRequested;
        GameEvents.DropRequested += OnDropRequested;
        GameEvents.ThrowRequested += OnThrowRequested;
        GameEvents.UseItemRequested += OnUseItemRequested;
        GameEvents.CargoGrantRequested += OnCargoGrantRequested;
        GameEvents.CargoHandOverRequested += OnCargoHandOverRequested;
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

        // For rendered checks: `-- --start-unpacked` leaves everything on the mat.
        bool startUnpacked = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--start-unpacked") >= 0;
        if (AutoPackOnStart && !startUnpacked)
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
        GameEvents.ConsumeRequested -= OnConsumeRequested;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.ItemsGranted -= OnItemsGranted;
        GameEvents.ItemUseRequested -= OnItemUseRequested;
        GameEvents.DropRequested -= OnDropRequested;
        GameEvents.ThrowRequested -= OnThrowRequested;
        GameEvents.UseItemRequested -= OnUseItemRequested;
        GameEvents.CargoGrantRequested -= OnCargoGrantRequested;
        GameEvents.CargoHandOverRequested -= OnCargoHandOverRequested;
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

    /// <summary>
    /// Packs an item into the best free spot, or a free pocket when the pack is full and it is small;
    /// false if there's no room. Worn gear keeps its hit points (<paramref name="durability"/>, -1: new);
    /// <paramref name="announce"/> tells the game it was picked up.
    /// </summary>
    public bool TryAdd(ItemDefinition item, float durability = -1.0f, bool announce = true)
    {
        if (Grid == null)
        {
            return false;
        }
        PackedItem? placed = null;
        if (AutoPacker.TryBestSpot(Grid, item, PickupPreferredX, out Vector3I origin, out int orientation))
        {
            placed = Grid.Place(item, origin, orientation);
        }
        if (placed != null)
        {
            placed.Durability = durability;
        }
        else if (Equipment == null || !Equipment.TryPocket(item))
        {
            return false;
        }
        _all.Add(item);
        Publish();
        if (announce)
        {
            GameEvents.PublishItemPickedUp(item.ItemId, item.Category.ToString());
        }
        return true;
    }

    /// <summary>Drops an item from the owned list (it moved to the body or out of a pocket).</summary>
    public void ForgetOwned(ItemDefinition item)
    {
        _all.Remove(item);
        Publish();
    }

    /// <summary>
    /// The packed item of a category that best covers <paramref name="deficit"/>: the smallest one
    /// that covers it all, or the biggest if none does. Null if there's nothing of that category.
    /// </summary>
    public PackedItem? BestConsumable(ItemCategory category, float deficit)
    {
        if (Grid == null)
        {
            return null;
        }
        PackedItem? covering = null;
        PackedItem? biggest = null;
        foreach (PackedItem item in Grid.Items)
        {
            if (item.Definition.Category != category)
            {
                continue;
            }
            float restore = RestoreOf(item.Definition);
            if (restore >= deficit && (covering == null || restore < RestoreOf(covering.Definition)))
            {
                covering = item;
            }
            if (biggest == null || restore > RestoreOf(biggest.Definition))
            {
                biggest = item;
            }
        }
        return covering ?? biggest;
    }

    /// <summary>Removes a packed item for good (eaten, drunk, used up).</summary>
    public void Consume(PackedItem item)
    {
        if (Grid == null)
        {
            return;
        }
        Grid.Remove(item);
        _all.Remove(item.Definition);
        Publish();
    }

    /// <summary>Removes everything: pack, loose items and the record of owned items.</summary>
    public void Clear()
    {
        Grid?.Clear();
        _loose.Clear();
        _all.Clear();
        Publish();
    }

    private void OnConsumeRequested(SupplyNeed need, float deficit)
    {
        ItemCategory category = ItemCategory.Water;
        if (need == SupplyNeed.Hunger)
        {
            category = ItemCategory.Food;
        }

        PackedItem? item = BestConsumable(category, deficit);
        ItemDefinition? best = item?.Definition;
        ItemDefinition? pocketChoice = null;
        if (Equipment != null)
        {
            foreach (ItemDefinition pocketed in Equipment.PocketItems)
            {
                if (pocketed.Category == category && BetterConsumable(pocketed, best, deficit))
                {
                    best = pocketed;
                    pocketChoice = pocketed;
                }
            }
        }
        if (pocketChoice != null)
        {
            if (RemovePocketItem(pocketChoice))
            {
                GameEvents.PublishItemConsumed(need, pocketChoice.ItemId, RestoreOf(pocketChoice));
                return;
            }
            GameEvents.PublishConsumeFailed(need);
            return;
        }
        if (item == null)
        {
            GameEvents.PublishConsumeFailed(need);
            return;
        }
        Consume(item);
        GameEvents.PublishItemConsumed(need, item.Definition.ItemId, RestoreOf(item.Definition));
    }

    private static bool BetterConsumable(ItemDefinition candidate, ItemDefinition? best, float deficit)
    {
        if (best == null)
        {
            return true;
        }
        float restore = RestoreOf(candidate);
        float previous = RestoreOf(best);
        if (restore >= deficit)
        {
            return previous < deficit || restore < previous;
        }
        return previous < deficit && restore > previous;
    }

    private bool RemovePocketItem(ItemDefinition item)
    {
        if (Equipment == null || !Equipment.TakeFromPocket(item))
        {
            return false;
        }
        _all.Remove(item);
        Publish();
        return true;
    }

    /// <summary>Items given to the carrier (the starting kit): packed where they fit best.</summary>
    private void OnItemsGranted(string[] itemPaths)
    {
        foreach (string path in itemPaths)
        {
            if (ResourceLoader.Load(path) is ItemDefinition item)
            {
                TryAdd(item);
            }
        }
    }

    /// <summary>
    /// Packs contract cargo: a copy of the item at <paramref name="itemPath"/> of its own, tagged
    /// with <paramref name="cargoId"/>, so two parcels of one kind are never the same parcel. False
    /// (and nothing packed) if the path isn't an item, that cargo is already owned, or there's no room.
    /// </summary>
    public bool GrantCargo(string itemPath, string cargoId)
    {
        if (cargoId.Length == 0 || OwnsCargo(cargoId))
        {
            return false;
        }
        if (ResourceLoader.Load(itemPath) is not ItemDefinition kind)
        {
            GD.PushWarning("CarrierInventory: contract cargo " + itemPath + " is not an ItemDefinition.");
            return false;
        }
        if (kind.Duplicate() is not ItemDefinition cargo)
        {
            return false;
        }
        cargo.CargoInstanceId = cargoId;
        return TryAdd(cargo, -1.0f, false);
    }

    /// <summary>Whether the carrier owns the cargo with this id (packed, loose or in a pocket).</summary>
    public bool OwnsCargo(string cargoId)
    {
        foreach (ItemDefinition item in _all)
        {
            if (item.CargoInstanceId == cargoId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Removes the carried cargo with this id for good (handed over); false if it isn't carried.</summary>
    public bool HandOverCargo(string cargoId)
    {
        if (cargoId.Length == 0)
        {
            return false;
        }
        if (Grid != null)
        {
            foreach (PackedItem packed in Grid.Items)
            {
                if (packed.Definition.CargoInstanceId == cargoId)
                {
                    Consume(packed);
                    return true;
                }
            }
        }
        if (Equipment != null)
        {
            foreach (ItemDefinition pocketed in Equipment.PocketItems)
            {
                if (pocketed.CargoInstanceId == cargoId)
                {
                    return RemovePocketItem(pocketed);
                }
            }
        }
        for (int i = 0; i < _loose.Count; i++)
        {
            if (_loose[i].CargoInstanceId == cargoId)
            {
                _all.Remove(_loose[i]);
                _loose.RemoveAt(i);
                Publish();
                return true;
            }
        }
        return false;
    }

    private void OnCargoGrantRequested(string itemPath, string cargoId)
    {
        GameEvents.PublishCargoGranted(cargoId, GrantCargo(itemPath, cargoId));
    }

    private void OnCargoHandOverRequested(string cargoId)
    {
        GameEvents.PublishCargoHandedOver(cargoId, HandOverCargo(cargoId));
    }

    /// <summary>How far in front of the carrier a dropped item lands, in m.</summary>
    [Export] public float DropDistance { get; set; } = 0.9f;
    [Export(PropertyHint.Layers3DPhysics)] public uint DropGroundMask { get; set; } = 1;
    /// <summary>A thrown item lands this far ahead, after an arc this high and this long, in m and s.</summary>
    [Export] public float ThrowDistance { get; set; } = 3.2f;
    [Export] public float ThrowHeight { get; set; } = 1.2f;
    [Export] public float ThrowTime { get; set; } = 0.55f;

    /// <summary>The last thing thrown or dropped (tests).</summary>
    public WorldItem? LastDropped { get; private set; }

    private void OnDropRequested(string itemId)
    {
        PutDown(itemId, false);
    }

    private void OnThrowRequested(string itemId)
    {
        PutDown(itemId, true);
    }

    /// <summary>
    /// Puts one carried item with this id (pack, loose or pocket) on the ground: dropped at the
    /// carrier's feet, or thrown a few metres ahead, flying there in an arc from shoulder height.
    /// </summary>
    private void PutDown(string itemId, bool thrown)
    {
        ItemDefinition? item = null;
        if (Grid != null)
        {
            foreach (PackedItem packed in Grid.Items)
            {
                if (packed.Definition.ItemId == itemId)
                {
                    item = packed.Definition;
                    Consume(packed);
                    break;
                }
            }
        }
        if (item == null)
        {
            for (int i = 0; i < _loose.Count; i++)
            {
                if (_loose[i].ItemId == itemId)
                {
                    item = _loose[i];
                    _all.Remove(item);
                    _loose.RemoveAt(i);
                    Publish();
                    break;
                }
            }
        }
        if (item == null && Equipment != null && Equipment.TakeFromPocket(itemId, out ItemDefinition? pocketed) && pocketed != null)
        {
            item = pocketed;
            ForgetOwned(pocketed);
        }
        if (item == null)
        {
            return;
        }
        Node3D? carrier = GetParent() as Node3D;
        Vector3 from = GameEvents.CarrierPosition;
        Vector3 forward = Vector3.Forward;
        if (carrier != null)
        {
            Vector3 facing = -carrier.GlobalBasis.Z;
            facing.Y = 0.0f;
            if (!facing.IsZeroApprox())
            {
                forward = facing.Normalized();
            }
        }
        float distance = DropDistance;
        if (thrown)
        {
            distance = ThrowDistance;
        }
        Vector3 at = from + forward * distance;
        WorldItem dropped = new() { Definition = item, Name = $"Dropped_{item.ItemId}" };
        Node parent = GetTree().CurrentScene ?? GetTree().Root;
        parent.AddChild(dropped);
        if (carrier != null && carrier.IsInsideTree())
        {
            Godot.Collections.Dictionary hit = carrier.GetWorld3D().DirectSpaceState.IntersectRay(
                PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 2.0f, at + Vector3.Down * 4.0f, DropGroundMask));
            if (hit.Count > 0)
            {
                at = (Vector3)hit["position"];
            }
        }
        dropped.GlobalPosition = at;
        LastDropped = dropped;
        if (thrown)
        {
            // It flies there: from the hand, up in an arc, down where it lands.
            Vector3 hand = from + Vector3.Up * 1.4f + forward * 0.3f;
            Vector3 landing = at;
            float height = ThrowHeight;
            dropped.GlobalPosition = hand;
            Tween flight = dropped.CreateTween();
            flight.TweenMethod(Callable.From<float>(t =>
            {
                if (IsInstanceValid(dropped))
                {
                    dropped.GlobalPosition = hand.Lerp(landing, t) + Vector3.Up * (4.0f * height * t * (1.0f - t));
                }
            }), 0.0f, 1.0f, ThrowTime);
        }
        GameEvents.PublishItemDropped(item.ItemId);
    }

    /// <summary>Uses up one carried item with this id (packed or loose).</summary>
    private void OnItemUseRequested(string itemId)
    {
        if (Grid != null)
        {
            foreach (PackedItem item in Grid.Items)
            {
                if (item.Definition.ItemId == itemId)
                {
                    Consume(item);
                    return;
                }
            }
        }
        if (Equipment != null)
        {
            foreach (ItemDefinition pocketed in Equipment.PocketItems)
            {
                if (pocketed.ItemId == itemId)
                {
                    RemovePocketItem(pocketed);
                    return;
                }
            }
        }
        for (int i = 0; i < _loose.Count; i++)
        {
            if (_loose[i].ItemId == itemId)
            {
                _all.Remove(_loose[i]);
                _loose.RemoveAt(i);
                Publish();
                return;
            }
        }
    }

    /// <summary>Eats or drinks one carried item with this id (the hotbar's use); worn gear goes on.</summary>
    private void OnUseItemRequested(string itemId)
    {
        if (Grid == null)
        {
            return;
        }
        ItemDefinition? kind = ItemText.Find(itemId);
        if (kind != null && kind.Slot != EquipSlot.None)
        {
            Equipment?.Wear(itemId);
            return;
        }
        if (Equipment != null)
        {
            foreach (ItemDefinition pocketed in Equipment.PocketItems)
            {
                if (pocketed.ItemId != itemId)
                {
                    continue;
                }
                if (pocketed.Category != ItemCategory.Food && pocketed.Category != ItemCategory.Water)
                {
                    return;
                }
                if (RemovePocketItem(pocketed))
                {
                    SupplyNeed need = SupplyNeed.Hunger;
                    if (pocketed.Category == ItemCategory.Water)
                    {
                        need = SupplyNeed.Thirst;
                    }
                    GameEvents.PublishItemConsumed(need, itemId, RestoreOf(pocketed));
                }
                return;
            }
        }
        foreach (PackedItem item in Grid.Items)
        {
            if (item.Definition.ItemId != itemId)
            {
                continue;
            }
            if (item.Definition.Category == ItemCategory.Food)
            {
                Consume(item);
                GameEvents.PublishItemConsumed(SupplyNeed.Hunger, itemId, RestoreOf(item.Definition));
            }
            else if (item.Definition.Category == ItemCategory.Water)
            {
                Consume(item);
                GameEvents.PublishItemConsumed(SupplyNeed.Thirst, itemId, RestoreOf(item.Definition));
            }
            return;
        }
    }

    private void OnRespawnRequested()
    {
        if (ClearOnDeath)
        {
            Clear();
        }
    }

    private static float RestoreOf(ItemDefinition item)
    {
        if (item.Category == ItemCategory.Food)
        {
            return item.Nutrition;
        }
        return item.Hydration;
    }

    public void Publish()
    {
        if (Backpack != null && Grid != null)
        {
            GameEvents.PublishLoad(LoadCalculator.Calculate(Backpack, Grid));
        }
        List<string> ids = new();
        List<string> cargo = new();
        foreach (ItemDefinition item in _all)
        {
            ids.Add(item.ItemId);
            if (item.IsContractCargo)
            {
                cargo.Add(item.CargoInstanceId);
            }
        }
        GameEvents.PublishCarriedItems(ids);
        GameEvents.PublishCarriedCargo(cargo);
        GameEvents.PublishCargoExposure(CargoExposure());
    }

    /// <summary>
    /// How exposed each carried contract cargo is to knocks: 0 with at least ProtectedPaddingCells of
    /// pack between it and every outside face, 1 touching a face. Cargo outside the pack (a pocket,
    /// the hands) is fully exposed.
    /// </summary>
    public Dictionary<string, float> CargoExposure()
    {
        Dictionary<string, float> exposure = new();
        foreach (ItemDefinition item in _all)
        {
            if (item.IsContractCargo)
            {
                exposure[item.CargoInstanceId] = 1.0f;
            }
        }
        if (Grid == null)
        {
            return exposure;
        }
        foreach (PackedItem packed in Grid.Items)
        {
            if (!packed.Definition.IsContractCargo)
            {
                continue;
            }
            float protectedShare = 1.0f;
            for (int axis = 0; axis < 3; axis++)
            {
                protectedShare = Mathf.Min(protectedShare, AxisShare(Grid.Size[axis], packed.Origin[axis], packed.Size[axis]));
            }
            exposure[packed.Definition.CargoInstanceId] = 1.0f - protectedShare;
        }
        return exposure;
    }

    /// <summary>
    /// How well one axis protects an item: the padding on its thinner side against the most this axis
    /// allows (ProtectedPaddingCells, or centred if the pack is narrower), 0 to 1. An item filling the
    /// axis can't do better, so that axis counts as protecting it.
    /// </summary>
    private float AxisShare(int packCells, int origin, int itemCells)
    {
        int padding = System.Math.Min(origin, packCells - (origin + itemCells));
        int best = System.Math.Min(System.Math.Max(ProtectedPaddingCells, 1), (packCells - itemCells) / 2);
        if (best <= 0)
        {
            return 1.0f;
        }
        return Mathf.Clamp((float)padding / best, 0.0f, 1.0f);
    }
}
