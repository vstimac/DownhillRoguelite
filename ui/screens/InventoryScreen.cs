using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The inventory page (I): everything the carrier owns, one parchment slot per kind of item with
/// its count, on a grid that can be rearranged by dragging (the order is the page's own; packing
/// itself still happens in the 3D pack at stations). Clicking a slot opens its details: picture,
/// description, real properties and the actions that exist for it (eat, drink). The load's weight
/// is shown on a scale at the bottom. Unfolds on open, folds away on close.
/// </summary>
public partial class InventoryScreen : ParchmentPanel
{
    public const int Columns = 5;
    public const int Rows = 4;

    public IReadOnlyList<string> Order => _order;
    public int SelectedIndex { get; private set; } = -1;

    private readonly List<ItemSlot> _slots = new();
    private readonly List<string> _order = new();
    private readonly Dictionary<string, int> _counts = new();
    private string _signature = "";
    private ItemSlot? _detailIcon;
    private Label? _detailTitle;
    private Label? _detailBody;
    private InkButton? _use;
    private InkButton? _drop;
    private WeightScale? _weight;
    private Label? _stats;
    private VitalsSnapshot? _vitals;
    private RunState? _run;
    /// <summary>Where the carrier's stats column starts on the page, in px.</summary>
    private const float StatsX = 652.0f;

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitals;
    }

    private void OnVitals(VitalsSnapshot vitals) => _vitals = vitals;

    /// <summary>
    /// The character sheet beside the pack: body, load, then each ability as its total with what it
    /// is made of (base, what the gear adds, what this run's boons add), and where the carrier is.
    /// </summary>
    public string StatsText()
    {
        List<string> lines = new() { "THE CARRIER", "" };
        RunModifiers? mods = _run?.Modifiers;
        if (_vitals is VitalsSnapshot v)
        {
            lines.Add($"Health  {v.Health:0} / {v.MaxHealth:0}");
            lines.Add($"Hunger {v.Hunger:0}  ·  Thirst {v.Thirst:0}");
            float baseStamina = v.MaxStamina;
            if (mods != null && mods.StaminaMax > 0.01f)
            {
                baseStamina = v.MaxStamina / mods.StaminaMax;
            }
            lines.Add($"Stamina  {v.Stamina:0} / {v.MaxStamina:0}" + Parts(baseStamina, 0.0f, v.MaxStamina - baseStamina, ""));
            lines.Add("");
        }
        GearSnapshot gear = GameEvents.CurrentGear;
        lines.Add($"Load  {GameEvents.CurrentLoad.Mass:0.0} kg");
        if (gear.Backpack.MaxDurability > 0.0f)
        {
            lines.Add($"Pack  {gear.Backpack.Durability:0} / {gear.Backpack.MaxDurability:0}");
        }
        lines.Add($"Pockets  {gear.PocketsUsed} / {gear.Pockets}");
        lines.Add("");
        float boonGrip = 0.0f;
        float boonBalance = 0.0f;
        float boonSpeed = 0.0f;
        float boonCold = 0.0f;
        float boonRecovery = 0.0f;
        float boonNeeds = 0.0f;
        if (mods != null)
        {
            boonGrip = (mods.Grip - 1.0f) * 100.0f;
            boonBalance = (mods.Balance - 1.0f) * 100.0f;
            boonSpeed = (mods.WalkSpeed - 1.0f) * 100.0f;
            boonCold = mods.ColdResistance * 100.0f;
            boonRecovery = (mods.StaminaRecovery - 1.0f) * 100.0f;
            boonNeeds = (mods.NeedsDrain - 1.0f) * 100.0f;
        }
        lines.Add(Stat("Grip", 100.0f, gear.Grip * 100.0f, boonGrip));
        lines.Add(Stat("Balance", 100.0f, 0.0f, boonBalance));
        lines.Add(Stat("Walk speed", 100.0f, 0.0f, boonSpeed));
        lines.Add(Stat("Warmth", 0.0f, gear.Warmth * 100.0f, boonCold));
        lines.Add(Stat("Rain cover", 0.0f, gear.RainProof * 100.0f, 0.0f));
        lines.Add(Stat("Recovery", 100.0f, 0.0f, boonRecovery));
        lines.Add(Stat("Hunger rate", 100.0f, 0.0f, boonNeeds));
        if (gear.WindCatch > 0.0f)
        {
            lines.Add($"Catches wind  +{gear.WindCatch:0.00} m²");
        }
        if (_run != null)
        {
            lines.Add("");
            lines.Add($"{_run.Depth} m down, {_run.Circles.NameOf(_run.Circle)}");
            if (_run.Boons.Count > 0)
            {
                lines.Add("Boons: " + string.Join(", ", _run.Boons));
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>"Grip 125%  (100 +15 gear +10 boons)": the total, then its parts when there are bonuses.</summary>
    private static string Stat(string name, float baseValue, float gear, float boons)
    {
        float total = baseValue + gear + boons;
        return $"{name}  {total:0}%" + Parts(baseValue, gear, boons, "");
    }

    private static string Parts(float baseValue, float gear, float boons, string unit)
    {
        if (Mathf.Abs(gear) < 0.5f && Mathf.Abs(boons) < 0.5f)
        {
            return "";
        }
        string parts = $"  ({baseValue:0}{unit}";
        if (Mathf.Abs(gear) >= 0.5f)
        {
            parts += $" {Signed(gear)} gear";
        }
        if (Mathf.Abs(boons) >= 0.5f)
        {
            parts += $" {Signed(boons)} boons";
        }
        return parts + ")";
    }

    private static string Signed(float value)
    {
        if (value >= 0.0f)
        {
            return $"+{value:0}";
        }
        return $"{value:0}";
    }

    public override void _Ready()
    {
        Title = "Inventory";
        Closable = true;
        Seed = 41.0f;
        Size = new Vector2(880, 600);
        base._Ready();
        float cell = 72.0f;
        for (int i = 0; i < Columns * Rows; i++)
        {
            ItemSlot slot = new()
            {
                Position = new Vector2(24 + (i % Columns) * (cell + 6), 60 + (i / Columns) * (cell + 6)),
                Size = new Vector2(cell, cell),
                Index = i,
                SlotSeed = 3.0f,
                Draggable = true,
            };
            slot.Clicked += OnClicked;
            slot.DroppedOn = OnDropped;
            slot.DraggedAway = OnDraggedAway;
            AddChild(slot);
            _slots.Add(slot);
        }
        float detailX = 24 + Columns * (cell + 6) + 12;
        _detailIcon = new ItemSlot { Position = new Vector2(detailX, 64), Size = new Vector2(96, 96), Index = 99 };
        AddChild(_detailIcon);
        _detailTitle = UiStyle.MakeLabel("", 26, true);
        _detailTitle.Position = new Vector2(detailX, 168);
        AddChild(_detailTitle);
        _detailBody = UiStyle.MakeLabel("Choose something to read about it.", UiStyle.SmallSize);
        _detailBody.Position = new Vector2(detailX, 204);
        _detailBody.Size = new Vector2(StatsX - detailX - 16, 230);
        _detailBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_detailBody);
        _use = new InkButton { Text = "Eat", Paper = true, Position = new Vector2(detailX, 448), Size = new Vector2(110, 40), Visible = false };
        _use.Pressed += UseSelected;
        AddChild(_use);
        _drop = new InkButton { Text = "Drop", Paper = true, Position = new Vector2(detailX + 120, 448), Size = new Vector2(90, 40), Visible = false };
        _drop.Pressed += DropSelected;
        AddChild(_drop);
        _stats = UiStyle.MakeLabel("", UiStyle.SmallSize);
        _stats.Position = new Vector2(StatsX, 60);
        _stats.Size = new Vector2(Size.X - StatsX - 20, 470);
        _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_stats);
        GameEvents.VitalsUpdated += OnVitals;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _weight = new WeightScale { Position = new Vector2(24, Size.Y - 64), Size = new Vector2(StatsX - 48, 40) };
        AddChild(_weight);
        SetOpenNow(false);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }
        IReadOnlyList<string> carried = GameEvents.CarriedItems;
        string signature = string.Join(",", carried);
        if (signature != _signature)
        {
            _signature = signature;
            Refresh(carried);
        }
        if (_weight != null)
        {
            _weight.Mass = GameEvents.CurrentLoad.Mass;
            _weight.QueueRedraw();
        }
        if (_stats != null)
        {
            _stats.Text = StatsText();
        }
    }

    private void Refresh(IReadOnlyList<string> carried)
    {
        _counts.Clear();
        foreach (string id in carried)
        {
            _counts[id] = _counts.GetValueOrDefault(id) + 1;
        }
        _order.RemoveAll(id => !_counts.ContainsKey(id));
        foreach (string id in carried)
        {
            if (!_order.Contains(id))
            {
                _order.Add(id);
            }
        }
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < _order.Count)
            {
                _slots[i].SetItem(ItemText.Find(_order[i]), _counts[_order[i]]);
            }
            else
            {
                _slots[i].SetItem(null, 0);
            }
        }
        if (SelectedIndex >= _order.Count)
        {
            Select(-1);
        }
        else
        {
            Select(SelectedIndex);
        }
    }

    /// <summary>Click selects; double-click or right-click uses (eat, drink, wear, mend, map); Shift+right-click drops.</summary>
    private void OnClicked(ItemSlot slot, int button)
    {
        Select(slot.Index);
        if (button == (int)MouseButton.Right && slot.LastShift)
        {
            DropSelected();
        }
        else if (button == (int)MouseButton.Right || (button == (int)MouseButton.Left && slot.LastDoubleClick))
        {
            UseSelected();
        }
    }

    /// <summary>The item id in a slot of the page, or "" (the gear sheet asks when something is dragged onto it).</summary>
    public string ItemIdAt(int index)
    {
        if (index >= 0 && index < _order.Count)
        {
            return _order[index];
        }
        return "";
    }

    /// <summary>Moves the item dragged from one slot onto another (swapping), and keeps it selected.</summary>
    private void OnDropped(int from, int to)
    {
        if (from == to || from >= _order.Count)
        {
            return;
        }
        to = Mathf.Min(to, _order.Count - 1);
        (_order[from], _order[to]) = (_order[to], _order[from]);
        _signature = "";
        SelectedIndex = to;
        _slots[to].Pop();
    }

    public void Select(int index)
    {
        SelectedIndex = index;
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].SetSelected(i == index && index < _order.Count);
        }
        ItemDefinition? item = null;
        if (index >= 0 && index < _order.Count)
        {
            item = ItemText.Find(_order[index]);
        }
        if (_detailIcon == null || _detailTitle == null || _detailBody == null || _use == null)
        {
            return;
        }
        if (item == null)
        {
            _detailIcon.SetItem(null, 0);
            _detailTitle.Text = "";
            _detailBody.Text = "Choose something to read about it.";
            _use.Visible = false;
            if (_drop != null)
            {
                _drop.Visible = false;
            }
            return;
        }
        _detailIcon.SetItem(item, _counts.GetValueOrDefault(item.ItemId));
        _detailTitle.Text = item.DisplayName;
        _detailBody.Text = ItemText.Describe(item) + "\n" + ItemText.Properties(item) + "\n\n" + ItemText.HowToUse(item);
        _use.Visible = item.Category is ItemCategory.Food or ItemCategory.Water or ItemCategory.Clothing or ItemCategory.Material || ItemText.OpensMap(item);
        _use.Text = "Eat";
        if (ItemText.OpensMap(item))
        {
            _use.Text = "Map";
        }
        if (item.Category == ItemCategory.Water)
        {
            _use.Text = "Drink";
        }
        else if (item.Category == ItemCategory.Clothing)
        {
            _use.Text = "Wear";
        }
        else if (item.Category == ItemCategory.Material)
        {
            _use.Text = "Mend";
            if (!GameEvents.InShelter)
            {
                _detailBody.Text += "\n(only at a station)";
            }
        }
        _use.QueueRedraw();
        if (_drop != null)
        {
            _drop.Visible = true;
            _drop.QueueRedraw();
        }
    }

    /// <summary>Dropped where nothing took it: anywhere off the page throws it out of the way.</summary>
    private void OnDraggedAway(int index)
    {
        if (!GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            ThrowSlot(index);
        }
    }

    /// <summary>Throws one of the item in a slot out of the way (it was dragged off the page).</summary>
    public void ThrowSlot(int index)
    {
        if (index < 0 || index >= _order.Count)
        {
            return;
        }
        GameEvents.RequestThrow(_order[index]);
    }

    /// <summary>Puts one of the selected item down on the ground in front of the carrier.</summary>
    public void DropSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _order.Count)
        {
            return;
        }
        _slots[SelectedIndex].Pop();
        GameEvents.RequestDrop(_order[SelectedIndex]);
    }

    private void UseSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _order.Count)
        {
            return;
        }
        ItemDefinition? item = ItemText.Find(_order[SelectedIndex]);
        if (item == null)
        {
            return;
        }
        _slots[SelectedIndex].Pop();
        if (ItemText.OpensMap(item))
        {
            ItemText.ToggleMap();
        }
        else if (item.Category == ItemCategory.Food || item.Category == ItemCategory.Water)
        {
            GameEvents.RequestUseItem(item.ItemId);
        }
        else if (item.Category == ItemCategory.Clothing)
        {
            GameEvents.RequestWear(item.ItemId);
        }
        else if (item.Category == ItemCategory.Material)
        {
            GameEvents.RequestMend(item.ItemId);
        }
    }

    /// <summary>A hand-drawn balance and a bar for the load's weight.</summary>
    private partial class WeightScale : Control
    {
        public float Mass { get; set; }
        /// <summary>The bar is full at this weight (a heavy expedition load), in kg.</summary>
        public float FullMass { get; set; } = 40.0f;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            Vector2 p = new(4, 6);
            DrawLine(p + new Vector2(14, 0), p + new Vector2(14, 26), UiStyle.Ink, 2.5f);
            DrawLine(p + new Vector2(0, 6), p + new Vector2(28, 6), UiStyle.Ink, 2.5f);
            InkDraw.Arc(this, p + new Vector2(3, 14), 6.0f, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 2.0f, UiStyle.Ink);
            InkDraw.Arc(this, p + new Vector2(25, 14), 6.0f, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 2.0f, UiStyle.Ink);
            Rect2 bar = new(new Vector2(48, 10), new Vector2(Size.X - 200, 16));
            InkDraw.Shape(this, new[] { bar.Position, new Vector2(bar.End.X, bar.Position.Y), bar.End, new Vector2(bar.Position.X, bar.End.Y) }, UiStyle.PaperDark, 2.0f, 51);
            float fill = Mathf.Clamp(Mass / FullMass, 0.0f, 1.0f);
            if (fill > 0.0f)
            {
                DrawRect(new Rect2(bar.Position + new Vector2(2, 2), new Vector2((bar.Size.X - 4) * fill, bar.Size.Y - 4)), new Color(0.45f, 0.3f, 0.18f));
            }
            DrawString(UiStyle.Hand, new Vector2(bar.End.X + 14, 26), $"{Mass:0.0} / {FullMass:0} kg", HorizontalAlignment.Left, -1, UiStyle.TextSize, UiStyle.Ink);
        }
    }
}
