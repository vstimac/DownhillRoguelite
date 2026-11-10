using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The quick inventory along the bottom: what the carrier actually owns (GameEvents.CarriedItems),
/// one parchment slot per kind of item with its stack count, in the order picked up. Keys 1–9
/// select a slot (the selection ring slides over with a pop); V uses the selected item when it can
/// be used (eating food, drinking water, through the inventory). The name of a newly selected item
/// floats up briefly above the bar.
/// </summary>
public partial class Hotbar : Control
{
    public const int Slots = 9;

    public int Selected { get; private set; }
    public IReadOnlyList<ItemSlot> SlotNodes => _slots;
    public string SelectedId
    {
        get
        {
            if (Selected < _ids.Count)
            {
                return _ids[Selected];
            }
            return "";
        }
    }

    private readonly List<ItemSlot> _slots = new();
    private readonly List<string> _ids = new();
    private string _signature = "";
    private Label? _name;
    private Tween? _nameTween;
    private InkButton? _left;
    private InkButton? _right;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        float slot = 64.0f;
        float gap = 6.0f;
        Size = new Vector2(Slots * (slot + gap) + 80.0f, slot + 34.0f);
        ColorRect strip = new() { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(30, 10), Size = new Vector2(Size.X - 60, slot + 16) };
        strip.Material = UiStyle.Parchment(strip.Size, 23.0f, 4.0f, 4.0f);
        AddChild(strip);
        for (int i = 0; i < Slots; i++)
        {
            ItemSlot s = new() { Position = new Vector2(40 + i * (slot + gap), 18), Size = new Vector2(slot, slot), KeyHint = (i + 1).ToString(), Index = i };
            s.Clicked += OnSlotClicked;
            AddChild(s);
            _slots.Add(s);
        }
        _left = new InkButton { Glyph = InkButton.GlyphKind.Left, Position = new Vector2(0, 26), Size = new Vector2(30, 40) };
        _left.Pressed += () => Select((Selected + Slots - 1) % Slots);
        AddChild(_left);
        _right = new InkButton { Glyph = InkButton.GlyphKind.Right, Position = new Vector2(Size.X - 30, 26), Size = new Vector2(30, 40) };
        _right.Pressed += () => Select((Selected + 1) % Slots);
        AddChild(_right);
        _name = UiStyle.MakeLabel("", UiStyle.TextSize, false, UiStyle.Paper);
        _name.AddThemeColorOverride("font_outline_color", UiStyle.Ink);
        _name.AddThemeConstantOverride("outline_size", 6);
        _name.Modulate = new Color(1, 1, 1, 0);
        AddChild(_name);
        Select(0);
    }

    private void OnSlotClicked(ItemSlot slot, int button)
    {
        Select(slot.Index);
        if (button == (int)MouseButton.Right)
        {
            UseSelected();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The mouse wheel steps through the hotbar while walking (pages and the map use it themselves).
        if (@event is InputEventMouseButton wheel && wheel.Pressed && GameOptions.WheelSelectsHotbar && !GameEvents.IsPacking && !GameEvents.IsScreenOpen && !GameEvents.IsMapOpen)
        {
            if (wheel.ButtonIndex == MouseButton.WheelDown)
            {
                Select((Selected + 1) % Slots);
                GetViewport().SetInputAsHandled();
            }
            else if (wheel.ButtonIndex == MouseButton.WheelUp)
            {
                Select((Selected + Slots - 1) % Slots);
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (GameEvents.IsPacking || @event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key9)
        {
            Select((int)(key.PhysicalKeycode - Key.Key1));
            GetViewport().SetInputAsHandled();
        }
        else if (key.PhysicalKeycode == Key.V)
        {
            UseSelected();
            GetViewport().SetInputAsHandled();
        }
        else if (key.PhysicalKeycode == Key.G && SelectedId.Length > 0)
        {
            // G drops the selected item on the ground.
            GameEvents.RequestDrop(SelectedId);
            GetViewport().SetInputAsHandled();
        }
    }

    public void Select(int index)
    {
        Selected = Mathf.Clamp(index, 0, Slots - 1);
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].SetSelected(i == Selected);
        }
        ShowName();
    }

    /// <summary>Uses the selected item: eat, drink, wear, mend, or (paper, pencil) open the map; returns whether it was asked.</summary>
    public bool UseSelected()
    {
        string id = SelectedId;
        if (id.Length == 0)
        {
            return false;
        }
        ItemDefinition? item = ItemText.Find(id);
        if (item == null)
        {
            return false;
        }
        if (ItemText.OpensMap(item))
        {
            ItemText.ToggleMap();
            return true;
        }
        if (item.Category == ItemCategory.Clothing)
        {
            GameEvents.RequestWear(id);
            return true;
        }
        if (item.Category == ItemCategory.Material)
        {
            GameEvents.RequestMend(id);
            return true;
        }
        if (item.Category != ItemCategory.Food && item.Category != ItemCategory.Water && !item.UsableAlone)
        {
            return false;
        }
        GameEvents.RequestUseItem(id);
        return true;
    }

    private void ShowName()
    {
        if (_name == null || _slots.Count == 0)
        {
            return;
        }
        ItemDefinition? item = _slots[Selected].Item;
        if (item == null)
        {
            return;
        }
        _name.Text = item.DisplayName;
        Vector2 size = UiStyle.Hand.GetStringSize(_name.Text, HorizontalAlignment.Left, -1, UiStyle.TextSize);
        _name.Position = new Vector2(_slots[Selected].Position.X + 32 - size.X * 0.5f, -14);
        _nameTween?.Kill();
        _nameTween = CreateTween();
        _name.Modulate = new Color(1, 1, 1, 1);
        _nameTween.TweenInterval(1.2);
        _nameTween.TweenProperty(_name, "modulate:a", 0.0f, 0.6);
    }

    public override void _Process(double delta)
    {
        IReadOnlyList<string> carried = GameEvents.CarriedItems;
        string signature = string.Join(",", carried);
        if (signature == _signature)
        {
            return;
        }
        _signature = signature;
        Dictionary<string, int> counts = new();
        foreach (string id in carried)
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        // Keep each kind's slot where it was; new kinds take the first free slot; used-up kinds leave.
        for (int i = _ids.Count - 1; i >= 0; i--)
        {
            if (!counts.ContainsKey(_ids[i]))
            {
                _ids.RemoveAt(i);
            }
        }
        foreach (string id in carried)
        {
            if (!_ids.Contains(id) && _ids.Count < Slots)
            {
                _ids.Add(id);
            }
        }
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < _ids.Count)
            {
                _slots[i].SetItem(ItemText.Find(_ids[i]), counts[_ids[i]]);
            }
            else
            {
                _slots[i].SetItem(null, 0);
            }
        }
    }
}
