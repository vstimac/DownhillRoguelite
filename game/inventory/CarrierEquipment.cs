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
    [Export] public float WetnessPerHour { get; set; } = 0.4f;
    [Export] public float DryingPerHour { get; set; } = 0.15f;
    [Export] public float DirtPerHour { get; set; } = 0.025f;
    [Export] public float WetWarmthLoss { get; set; } = 0.6f;

    public CarrierInventory? Inventory { get; set; }
    public float BackpackDurability { get; private set; }
    public IReadOnlyList<ItemDefinition> PocketItems => _pocketDefinitions ??= new ItemDefinitions(_pockets);

    private readonly Dictionary<EquipSlot, ItemDefinition> _worn = new();
    private readonly Dictionary<EquipSlot, float> _durability = new();
    private readonly Dictionary<EquipSlot, ItemAppearance> _appearance = new();
    private readonly Dictionary<EquipSlot, string> _identities = new();
    public string IdentityOf(EquipSlot slot) => _identities.GetValueOrDefault(slot, "");
    internal bool OwnsInstance(string id)
    {
        foreach (string identity in _identities.Values) { if (identity == id) { return true; } }
        foreach (StoredItem copy in _pockets) { if (copy.InstanceId == id) { return true; } }
        return false;
    }
    internal IReadOnlyDictionary<EquipSlot, ItemDefinition> WornItems => _worn;
    internal IReadOnlyList<StoredItem> PocketCopies => _pockets;
    internal void SetPocketCopies(IEnumerable<StoredItem> copies) { _pockets.Clear(); _pockets.AddRange(copies); }
    public ItemAppearance AppearanceOf(EquipSlot slot) => _appearance.GetValueOrDefault(slot);
    internal void RestoreLoad(Dictionary<EquipSlot, StoredItem> worn, float backpackDurability,
        BackpackGrid grid, List<StoredItem> loose, List<StoredItem> pockets)
    {
        _worn.Clear(); _durability.Clear(); _appearance.Clear(); _identities.Clear();
        foreach ((EquipSlot slot, StoredItem copy) in worn)
        {
            _worn[slot] = copy.Definition;
            _durability[slot] = copy.Durability;
            _appearance[slot] = copy.Appearance;
            _identities[slot] = copy.InstanceId;
        }
        BackpackDurability = backpackDurability;
        Inventory!.ApplyStorage(grid, loose, pockets);
        Changed(true);
    }
    public void SetAppearance(EquipSlot slot, ItemAppearance appearance)
    {
        if (!_worn.ContainsKey(slot)) { return; }
        _appearance[slot] = appearance with { Wetness = Mathf.Clamp(appearance.Wetness, 0, 1), Dirt = Mathf.Clamp(appearance.Dirt, 0, 1), Variant = Mathf.Max(0, appearance.Variant) };
        Changed(true);
    }
    private readonly List<StoredItem> _pockets = new();
    private ItemDefinitions? _pocketDefinitions;
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
        if (Inventory?.Grid == null) { return false; }
        foreach (ItemDefinition item in Inventory.AllItems)
        {
            if (item.ItemId == itemId)
            {
                if (!item.ValidEquipment()) { return false; }
                return Transfer(item.Slot, item);
            }
        }
        return false;
    }

    private bool Transfer(EquipSlot slot, ItemDefinition? incoming)
    {
        if (Inventory?.Grid == null) { return false; }
        _worn.TryGetValue(slot, out ItemDefinition? previous);
        StoredItem? outgoing = null;
        if (previous != null) { outgoing = new StoredItem(previous, DurabilityOf(slot), AppearanceOf(slot), IdentityOf(slot)); }
        long capacity = 0;
        foreach ((EquipSlot key, ItemDefinition item) in _worn)
        {
            if (key != slot) { capacity += System.Math.Max(item.Pockets, 0); }
        }
        if (incoming != null) { capacity += System.Math.Max(incoming.Pockets, 0); }
        if (!EquipmentTransfer.Plan(Inventory, _pockets, incoming, outgoing, (int)System.Math.Min(capacity, int.MaxValue), PocketMaxCells,
            out BackpackGrid grid, out List<StoredItem> loose, out List<StoredItem> pockets, out StoredItem selected)) { return false; }
        _worn.Remove(slot);
        _durability.Remove(slot);
        _appearance.Remove(slot);
        _identities.Remove(slot);
        if (incoming != null)
        {
            _worn[slot] = incoming;
            float durability = selected.Durability;
            if (durability < 0) { durability = incoming.MaxDurability; }
            _durability[slot] = Mathf.Clamp(durability, 0, incoming.MaxDurability);
            _appearance[slot] = selected.Appearance;
            _identities[slot] = selected.InstanceId;
        }
        Inventory.ApplyStorage(grid, loose, pockets);
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
        return _worn.ContainsKey(slot) && Transfer(slot, null);
    }

    /// <summary>Puts a small item in a free pocket (the pack is full); false if no pocket takes it.</summary>
    public bool TryPocket(ItemDefinition item, float durability = -1.0f, ItemAppearance appearance = default, string instanceId = "")
    {
        Vector3I size = item.SizeCells;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0
            || (double)size.X * size.Y * size.Z > PocketMaxCells || _pockets.Count >= PocketCapacity())
        {
            return false;
        }
        if (instanceId.Length == 0) { instanceId = System.Guid.NewGuid().ToString("N"); }
        else if (OwnsInstance(instanceId)) { return false; }
        _pockets.Add(new StoredItem(item, durability, appearance, instanceId));
        Changed(true);
        return true;
    }

    /// <summary>Takes an item with this id out of a pocket (eaten, used); false if none.</summary>
    public bool TakeFromPocket(string itemId, out ItemDefinition? item)
    {
        return TakeFromPocket(itemId, out item, out _);
    }

    /// <summary>Takes one pocket copy out, retaining its condition for another container.</summary>
    public bool TakeFromPocket(string itemId, out ItemDefinition? item, out float durability)
    {
        return TakeFromPocket(itemId, out item, out durability, out _);
    }

    public bool TakeFromPocket(string itemId, out ItemDefinition? item, out float durability, out ItemAppearance appearance)
    {
        return TakeFromPocket(itemId, out item, out durability, out appearance, out _);
    }

    public bool TakeFromPocket(string itemId, out ItemDefinition? item, out float durability, out ItemAppearance appearance, out string instanceId)
    {
        int index = _pockets.FindIndex(i => i.Definition.ItemId == itemId);
        item = null;
        durability = -1.0f;
        appearance = default;
        instanceId = "";
        if (index < 0)
        {
            return false;
        }
        item = _pockets[index].Definition;
        durability = _pockets[index].Durability;
        appearance = _pockets[index].Appearance;
        instanceId = _pockets[index].InstanceId;
        _pockets.RemoveAt(index);
        Changed(true);
        return true;
    }

    /// <summary>Removes this physical definition from a pocket (cargo copies have distinct identities).</summary>
    public bool TakeFromPocket(ItemDefinition item)
    {
        int index = _pockets.FindIndex(copy => copy.Definition == item);
        if (index < 0)
        {
            return false;
        }
        _pockets.RemoveAt(index);
        Changed(true);
        return true;
    }

    public void ClearPockets()
    {
        if (_pockets.Count == 0)
        {
            return;
        }
        _pockets.Clear();
        Changed(true);
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
        PackedItem? packedMaterial = null;
        ItemDefinition? material = null;
        foreach (PackedItem packed in Inventory.Grid.Items)
        {
            if (packed.Definition.ItemId == materialId)
            {
                packedMaterial = packed;
                material = packed.Definition;
                break;
            }
        }
        if (material == null)
        {
            foreach (StoredItem pocketed in _pockets)
            {
                if (pocketed.Definition.ItemId == materialId)
                {
                    material = pocketed.Definition;
                    break;
                }
            }
        }
        if (material == null || material.MendAmount <= 0.0f)
        {
            return false;
        }
        HashSet<string> mends = new(material.MendsTypes);
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
            GameEvents.PublishMended($"Nothing {material.DisplayName} can mend", 0.0f);
            return false;
        }
        float amount = material.MendAmount;
        string name = "the pack";
        if (pack)
        {
            float before = BackpackDurability;
            BackpackDurability = Mathf.Min(BackpackMaxDurability, BackpackDurability + amount);
            amount = BackpackDurability - before;
        }
        else
        {
            ItemDefinition item = _worn[best];
            float before = _durability[best];
            _durability[best] = Mathf.Min(item.MaxDurability, _durability[best] + amount);
            amount = _durability[best] - before;
            name = item.DisplayName;
        }
        if (packedMaterial != null)
        {
            Inventory.Consume(packedMaterial);
        }
        else
        {
            TakeFromPocket(material);
            Inventory.ForgetOwned(material);
        }
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
        _appearance.Clear();
        _identities.Clear();
        _pockets.Clear();
        BackpackDurability = BackpackMaxDurability;
        Changed(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_clock != null)
        {
            float hours = _clock.HoursPerSecond * (float)delta;
            float rain = Mathf.Clamp(GameEvents.CurrentWeather.Rain + GameEvents.CurrentWeather.Snow * 0.3f, 0, 1);
            foreach ((EquipSlot slot, ItemDefinition item) in _worn)
            {
                ItemAppearance before = AppearanceOf(slot);
                float wet = Mathf.Clamp(before.Wetness + (rain * WetnessPerHour * (1.0f - Mathf.Clamp(item.RainProof, 0, 1)) - DryingPerHour * (1.0f - rain)) * hours, 0, 1);
                float dirt = before.Dirt;
                if (_moving) { dirt = Mathf.Clamp(dirt + DirtPerHour * hours, 0, 1); }
                ItemAppearance after = before with { Wetness = wet, Dirt = dirt };
                _appearance[slot] = after;
                _dirty |= before != after;
            }
        }
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
                if (wet && (slot == EquipSlot.Torso || slot == EquipSlot.InnerTorso || slot == EquipSlot.Legs || slot == EquipSlot.Cape))
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
        WornPiece[] slots = new WornPiece[EquipmentSlots.Count];
        float warmth = 0.0f;
        float rain = 0.0f;
        float grip = 0.0f;
        float wind = 0.0f;
        float wornMass = 0.0f;
        float speed = 1, stamina = 1, windProof = 0, fall = 0, physical = 0;
        foreach ((EquipSlot slot, ItemDefinition item) in _worn)
        {
            wornMass += item.MassKg;
            float durability = _durability.GetValueOrDefault(slot);
            slots[(int)slot] = new WornPiece(item.ItemId, item.DisplayName, durability, item.MaxDurability, item, AppearanceOf(slot));
            if (durability <= 0.0f)
            {
                continue;
            }
            warmth += item.Warmth * (1.0f - AppearanceOf(slot).Wetness * WetWarmthLoss);
            rain += item.RainProof;
            grip += item.GripBonus;
            wind += item.WindCatch;
            speed *= Mathf.Clamp(item.SpeedMultiplier, 0.1f, 3.0f);
            stamina *= Mathf.Clamp(item.StaminaMultiplier, 0.1f, 3.0f);
            windProof += item.WindProof;
            fall += item.FallProtection;
            physical += item.PhysicalProtection;
        }
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].ItemId == null)
            {
                slots[i] = new WornPiece("", "", 0.0f, 0.0f);
            }
        }
        float pocketMass = 0.0f;
        foreach (StoredItem item in _pockets)
        {
            pocketMass += item.Definition.MassKg;
        }
        string packName = GameEvents.CurrentLoad.Name;
        GameEvents.PublishGear(new GearSnapshot(slots, new WornPiece("backpack", packName, BackpackDurability, BackpackMaxDurability),
            Mathf.Clamp(warmth, 0, MaxWarmth), Mathf.Clamp(rain, 0, 0.9f), grip, wind, PocketCapacity(), _pockets.Count, wornMass, pocketMass,
            Mathf.Clamp(speed, 0.1f, 3.0f), Mathf.Clamp(stamina, 0.1f, 3.0f), Mathf.Clamp(windProof, 0, 0.9f), Mathf.Clamp(fall, 0, 0.8f), Mathf.Clamp(physical, 0, 0.8f)));
    }
}
