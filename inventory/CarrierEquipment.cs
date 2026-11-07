using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// What the carrier wears (M14): one piece per slot (head, torso, legs, hands, feet, cape), taken
/// out of the pack to wear and put back to take off, each with hit points. Gear works while it
/// isn't broken: warmth and rain protection against cold and chill, extra grip from boots, a cape
/// that catches the wind, and pockets for small items when the pack is full. Everything wears down:
/// walking (boots faster on scree and rock, clothes faster in rain and snow), hard landings and
/// falls; the backpack too, and a torn pack loses an item at every fall. Mending uses found
/// materials and only works at a station. Publishes GearSnapshot (GameEvents.CurrentGear); a new
/// run starts with nothing worn and a whole pack. Created by CarrierInventory.
/// </summary>
public partial class CarrierEquipment : Node
{
    [Export] public float BackpackMaxDurability { get; set; } = 120.0f;
    /// <summary>Hit points worn gear loses per in-game hour of walking.</summary>
    [Export] public float WearPerHour { get; set; } = 1.5f;
    [Export] public float BackpackWearPerHour { get; set; } = 0.5f;
    /// <summary>Boots wear this much faster on scree and rock; clothes in rain and snow.</summary>
    [Export] public float RoughGroundWear { get; set; } = 3.0f;
    [Export] public float WetWear { get; set; } = 2.0f;
    /// <summary>Landings faster than this wear gear, in m/s; per m/s above it.</summary>
    [Export] public float HardLanding { get; set; } = 6.0f;
    [Export] public float LandingWearPerMs { get; set; } = 1.2f;
    /// <summary>A fall (going down) costs every worn piece and the pack this much.</summary>
    [Export] public float FallWear { get; set; } = 8.0f;
    [Export] public float FallPackWear { get; set; } = 12.0f;
    /// <summary>Most warmth and rain protection all gear together can give.</summary>
    [Export] public float MaxWarmth { get; set; } = 0.8f;
    /// <summary>Items up to this many cells fit a pocket.</summary>
    [Export] public int PocketMaxCells { get; set; } = 8;

    public CarrierInventory? Inventory { get; set; }
    public float BackpackDurability { get; private set; }
    public IReadOnlyList<ItemDefinition> PocketItems => _pockets;

    private readonly Dictionary<EquipSlot, ItemDefinition> _worn = new();
    private readonly Dictionary<EquipSlot, float> _durability = new();
    private readonly List<ItemDefinition> _pockets = new();
    private readonly RandomNumberGenerator _random = new();
    private WorldClock? _clock;
    private bool _moving;
    private string _surface = "";
    private bool _dirty = true;
    private float _publishTimer;

    public override void _Ready()
    {
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _random.Randomize();
        BackpackDurability = BackpackMaxDurability;
        GameEvents.WearRequested += OnWear;
        GameEvents.TakeOffRequested += OnTakeOff;
        GameEvents.MendRequested += OnMend;
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.Landed += OnLanded;
        GameEvents.DownedChanged += OnDowned;
        GameEvents.RespawnRequested += OnNewRun;
        CallDeferred(MethodName.PublishNow);
    }

    public override void _ExitTree()
    {
        GameEvents.WearRequested -= OnWear;
        GameEvents.TakeOffRequested -= OnTakeOff;
        GameEvents.MendRequested -= OnMend;
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.Landed -= OnLanded;
        GameEvents.DownedChanged -= OnDowned;
        GameEvents.RespawnRequested -= OnNewRun;
        _worn.Clear();
        _pockets.Clear();
    }

    public ItemDefinition? WornIn(EquipSlot slot)
    {
        _worn.TryGetValue(slot, out ItemDefinition? item);
        return item;
    }

    public float DurabilityOf(EquipSlot slot)
    {
        return _durability.GetValueOrDefault(slot);
    }

    /// <summary>Puts on a carried item (from the pack or a pocket); what was worn there goes back. False if it can't.</summary>
    public bool Wear(string itemId)
    {
        if (Inventory?.Grid == null)
        {
            return false;
        }
        PackedItem? packed = null;
        foreach (PackedItem item in Inventory.Grid.Items)
        {
            if (item.Definition.ItemId == itemId)
            {
                packed = item;
                break;
            }
        }
        ItemDefinition? definition = packed?.Definition;
        int pocket = -1;
        if (definition == null)
        {
            pocket = _pockets.FindIndex(i => i.ItemId == itemId);
            if (pocket >= 0)
            {
                definition = _pockets[pocket];
            }
        }
        if (definition == null || definition.Slot == EquipSlot.None || definition.MaxDurability <= 0.0f)
        {
            return false;
        }
        float durability = definition.MaxDurability;
        if (packed != null && packed.Durability >= 0.0f)
        {
            durability = packed.Durability;
        }
        if (packed != null)
        {
            Inventory.Consume(packed);
        }
        else
        {
            _pockets.RemoveAt(pocket);
            Inventory.ForgetOwned(definition);
        }
        if (_worn.ContainsKey(definition.Slot) && !TakeOff(definition.Slot))
        {
            // No room for what was worn: put the new piece back where it was.
            Inventory.TryAdd(definition, durability, false);
            return false;
        }
        _worn[definition.Slot] = definition;
        _durability[definition.Slot] = durability;
        Changed(true);
        return true;
    }

    private void OnWear(string itemId)
    {
        Wear(itemId);
    }

    private void OnMend(string materialId)
    {
        Mend(materialId);
    }

    private void OnTakeOff(EquipSlot slot)
    {
        TakeOff(slot);
    }

    /// <summary>Takes off what's worn in a slot into the pack (or a pocket); false if there's no room.</summary>
    public bool TakeOff(EquipSlot slot)
    {
        if (Inventory == null || !_worn.TryGetValue(slot, out ItemDefinition? item))
        {
            return false;
        }
        float durability = _durability.GetValueOrDefault(slot);
        _worn.Remove(slot);
        _durability.Remove(slot);
        // Pockets of what comes off go with it: spill them into the pack first.
        if (!Inventory.TryAdd(item, durability, false))
        {
            _worn[slot] = item;
            _durability[slot] = durability;
            return false;
        }
        while (_pockets.Count > PocketCapacity())
        {
            ItemDefinition spilled = _pockets[^1];
            _pockets.RemoveAt(_pockets.Count - 1);
            Inventory.ForgetOwned(spilled);
            if (!Inventory.TryAdd(spilled, -1.0f, false))
            {
                GameEvents.PublishItemLost(spilled.ItemId);
            }
        }
        Changed(true);
        return true;
    }

    /// <summary>Puts a small item in a free pocket (the pack is full); false if no pocket takes it.</summary>
    public bool TryPocket(ItemDefinition item)
    {
        Vector3I size = item.SizeCells;
        if (size.X * size.Y * size.Z > PocketMaxCells || _pockets.Count >= PocketCapacity())
        {
            return false;
        }
        _pockets.Add(item);
        Changed(true);
        return true;
    }

    /// <summary>Takes an item with this id out of a pocket (eaten, used); false if none.</summary>
    public bool TakeFromPocket(string itemId, out ItemDefinition? item)
    {
        int index = _pockets.FindIndex(i => i.ItemId == itemId);
        item = null;
        if (index < 0)
        {
            return false;
        }
        item = _pockets[index];
        _pockets.RemoveAt(index);
        Changed(true);
        return true;
    }

    public int PocketCapacity()
    {
        int pockets = 0;
        foreach (ItemDefinition item in _worn.Values)
        {
            pockets += item.Pockets;
        }
        return pockets;
    }

    /// <summary>Mends the most worn thing a carried material can mend; only at a station.</summary>
    public bool Mend(string materialId)
    {
        if (Inventory?.Grid == null)
        {
            return false;
        }
        if (!GameEvents.InShelter)
        {
            GameEvents.PublishMended("Mending needs a station's shelter", 0.0f);
            return false;
        }
        PackedItem? material = null;
        foreach (PackedItem packed in Inventory.Grid.Items)
        {
            if (packed.Definition.ItemId == materialId)
            {
                material = packed;
                break;
            }
        }
        if (material == null || material.Definition.MendAmount <= 0.0f)
        {
            return false;
        }
        HashSet<string> mends = new(material.Definition.MendsTypes);
        EquipSlot best = EquipSlot.None;
        bool pack = false;
        float worst = 1.0f;
        foreach ((EquipSlot slot, ItemDefinition item) in _worn)
        {
            float fraction = _durability.GetValueOrDefault(slot) / item.MaxDurability;
            if (mends.Contains(item.MendType) && fraction < worst)
            {
                worst = fraction;
                best = slot;
                pack = false;
            }
        }
        float packFraction = BackpackDurability / BackpackMaxDurability;
        if (mends.Contains("backpack") && packFraction < worst)
        {
            worst = packFraction;
            pack = true;
        }
        if (!pack && best == EquipSlot.None)
        {
            GameEvents.PublishMended($"Nothing {material.Definition.DisplayName} can mend", 0.0f);
            return false;
        }
        float amount = material.Definition.MendAmount;
        string name = "the pack";
        if (pack)
        {
            BackpackDurability = Mathf.Min(BackpackMaxDurability, BackpackDurability + amount);
        }
        else
        {
            ItemDefinition item = _worn[best];
            _durability[best] = Mathf.Min(item.MaxDurability, _durability[best] + amount);
            name = item.DisplayName;
        }
        Inventory.Consume(material);
        Changed(true);
        GameEvents.PublishMended(name, amount);
        return true;
    }

    /// <summary>Wears a slot (or the pack, with None) down by an amount; for tests and debugging too.</summary>
    public void WearDown(EquipSlot slot, float amount)
    {
        if (slot == EquipSlot.None)
        {
            BackpackDurability = Mathf.Max(0.0f, BackpackDurability - amount);
        }
        else if (_durability.ContainsKey(slot))
        {
            _durability[slot] = Mathf.Max(0.0f, _durability[slot] - amount);
        }
        Changed(true);
    }

    private void WearAll(float amount, float packAmount)
    {
        foreach (EquipSlot slot in new List<EquipSlot>(_durability.Keys))
        {
            _durability[slot] = Mathf.Max(0.0f, _durability[slot] - amount);
        }
        BackpackDurability = Mathf.Max(0.0f, BackpackDurability - packAmount);
        Changed(false);
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _moving = new Vector2(snapshot.Velocity.X, snapshot.Velocity.Z).Length() > 0.5f;
        _surface = snapshot.Surface;
    }

    private void OnLanded(float speed)
    {
        if (speed > HardLanding)
        {
            float wear = (speed - HardLanding) * LandingWearPerMs;
            WearAll(wear, wear * 1.25f);
        }
    }

    private void OnDowned(bool downed)
    {
        if (!downed)
        {
            return;
        }
        bool torn = BackpackDurability <= 0.0f;
        WearAll(FallWear, FallPackWear);
        // A torn pack loses something at every fall.
        if (torn && Inventory?.Grid != null && Inventory.Grid.Items.Count > 0)
        {
            PackedItem lost = Inventory.Grid.Items[_random.RandiRange(0, Inventory.Grid.Items.Count - 1)];
            Inventory.Consume(lost);
            GameEvents.PublishItemLost(lost.Definition.ItemId);
        }
    }

    private void OnNewRun()
    {
        _worn.Clear();
        _durability.Clear();
        _pockets.Clear();
        BackpackDurability = BackpackMaxDurability;
        Changed(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_moving && _clock != null)
        {
            float hours = _clock.HoursPerSecond * (float)delta;
            WeatherSnapshot weather = GameEvents.CurrentWeather;
            bool wet = weather.Rain + weather.Snow > 0.3f;
            bool rough = _surface == "Scree" || _surface == "Rock";
            foreach (EquipSlot slot in new List<EquipSlot>(_durability.Keys))
            {
                float rate = WearPerHour;
                if (slot == EquipSlot.Feet && rough)
                {
                    rate *= RoughGroundWear;
                }
                if (wet && (slot == EquipSlot.Torso || slot == EquipSlot.Legs || slot == EquipSlot.Cape))
                {
                    rate *= WetWear;
                }
                _durability[slot] = Mathf.Max(0.0f, _durability[slot] - rate * hours);
            }
            BackpackDurability = Mathf.Max(0.0f, BackpackDurability - BackpackWearPerHour * hours);
            _dirty = true;
        }
        _publishTimer -= (float)delta;
        if (_dirty && _publishTimer <= 0.0f)
        {
            PublishNow();
        }
    }

    private void Changed(bool now)
    {
        _dirty = true;
        if (now)
        {
            PublishNow();
        }
    }

    private void PublishNow()
    {
        _dirty = false;
        _publishTimer = 0.5f;
        WornPiece[] slots = new WornPiece[7];
        float warmth = 0.0f;
        float rain = 0.0f;
        float grip = 0.0f;
        float wind = 0.0f;
        foreach ((EquipSlot slot, ItemDefinition item) in _worn)
        {
            float durability = _durability.GetValueOrDefault(slot);
            slots[(int)slot] = new WornPiece(item.ItemId, item.DisplayName, durability, item.MaxDurability);
            if (durability <= 0.0f)
            {
                continue;
            }
            warmth += item.Warmth;
            rain += item.RainProof;
            grip += item.GripBonus;
            wind += item.WindCatch;
        }
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].ItemId == null)
            {
                slots[i] = new WornPiece("", "", 0.0f, 0.0f);
            }
        }
        string packName = GameEvents.CurrentLoad.Name;
        GameEvents.PublishGear(new GearSnapshot(slots, new WornPiece("backpack", packName, BackpackDurability, BackpackMaxDurability),
            Mathf.Min(warmth, MaxWarmth), Mathf.Min(rain, 0.9f), grip, wind, PocketCapacity(), _pockets.Count));
    }
}
