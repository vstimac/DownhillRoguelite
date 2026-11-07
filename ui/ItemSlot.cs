using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// One parchment slot holding an item: its illustrated icon (or an inked placeholder glyph when
/// empty), a stack count, an optional key hint. Hovering lifts it a little; selecting it draws an
/// animated inked ring that breathes; a new item or a bigger stack makes it pop. Used by the
/// hotbar, the inventory page and the equipment panel.
/// </summary>
public partial class ItemSlot : Control
{
    [Signal] public delegate void ClickedEventHandler(ItemSlot slot, int button);

    public ItemDefinition? Item { get; private set; }
    public int Count { get; private set; }
    public string KeyHint { get; set; } = "";
    public InkIcon? EmptyGlyph { get; set; }
    public bool Selected { get; private set; }
    public int Index { get; set; }
    public float SlotSeed { get; set; }

    private ColorRect? _paper;
    private TextureRect? _icon;
    private ShaderMaterial? _iconMaterial;
    private float _time;
    private bool _hover;
    private Tween? _tween;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _paper = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_paper);
        _iconMaterial = new ShaderMaterial { Shader = UiStyle.InkIconShader };
        _icon = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Material = _iconMaterial,
        };
        AddChild(_icon);
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        Resized += Layout;
        Layout();
    }

    private void Layout()
    {
        if (_paper == null || _icon == null)
        {
            return;
        }
        _paper.Size = Size;
        _paper.Material = UiStyle.Parchment(Size, SlotSeed + Index * 1.7f, 2.5f, 2.0f);
        float pad = Size.X * 0.12f;
        _icon.Position = new Vector2(pad, pad);
        _icon.Size = Size - new Vector2(pad, pad) * 2.0f;
        PivotOffset = Size * 0.5f;
    }

    /// <summary>Shows an item (null for empty) and a count; a new item or a bigger stack pops.</summary>
    public void SetItem(ItemDefinition? item, int count)
    {
        bool changed = item != Item || count > Count;
        Item = item;
        Count = count;
        if (item != null)
        {
            TooltipText = $"{item.DisplayName}\n{Inventory.ItemText.Properties(item)}";
        }
        else if (EmptyGlyph == null)
        {
            TooltipText = "";
        }
        if (changed && item != null && IsInsideTree())
        {
            Pop();
        }
        QueueRedraw();
    }

    public void SetSelected(bool selected)
    {
        if (selected && !Selected && IsInsideTree())
        {
            Pop();
        }
        Selected = selected;
        QueueRedraw();
    }

    private void SetHover(bool on)
    {
        _hover = on;
        _tween?.Kill();
        _tween = CreateTween();
        float target = 1.0f;
        if (on)
        {
            target = 1.07f;
        }
        _tween.TweenProperty(this, "scale", Vector2.One * target, 0.12).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A quick squash and spring (picked up, moved, selected).</summary>
    public void Pop()
    {
        _tween?.Kill();
        Scale = new Vector2(1.18f, 0.86f);
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.32).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Slots that can be dragged onto each other (the inventory page).</summary>
    public bool Draggable { get; set; }
    /// <summary>Called with (from, to) slot indexes when a dragged item is dropped here.</summary>
    public System.Action<int, int>? DroppedOn { get; set; }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!Draggable || Item == null)
        {
            return default;
        }
        // The dragged picture: a copy of this slot following the mouse, tilted like a lifted card.
        ItemSlot preview = new() { Size = Size, Index = Index, SlotSeed = SlotSeed, Rotation = Mathf.DegToRad(-6.0f), Modulate = new Color(1, 1, 1, 0.85f) };
        preview.SetItem(Item, Count);
        Control holder = new();
        holder.AddChild(preview);
        preview.Position = -Size * 0.5f;
        SetDragPreview(holder);
        Modulate = new Color(1, 1, 1, 0.4f);
        return Index;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return Draggable && data.VariantType == Variant.Type.Int;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        DroppedOn?.Invoke(data.AsInt32(), Index);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            Modulate = Colors.White;
        }
    }

    /// <summary>Tooltips are scraps of parchment too.</summary>
    public override GodotObject _MakeCustomTooltip(string forText)
    {
        string text = forText;
        if (text.Length == 0 && Item != null)
        {
            text = $"{Item.DisplayName}\n{Inventory.ItemText.Properties(Item)}";
        }
        Label label = UiStyle.MakeLabel(text, UiStyle.SmallSize);
        Vector2 size = UiStyle.Hand.GetMultilineStringSize(text, HorizontalAlignment.Left, -1, UiStyle.SmallSize) + new Vector2(28, 20);
        ColorRect paper = new() { CustomMinimumSize = size, Size = size, Material = UiStyle.Parchment(size, Index + 5.0f, 2.0f, 2.0f) };
        label.Position = new Vector2(14, 8);
        paper.AddChild(label);
        return paper;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.Pressed)
        {
            EmitSignal(SignalName.Clicked, this, (int)button.ButtonIndex);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_icon == null || _iconMaterial == null)
        {
            return;
        }
        Texture2D? texture = null;
        if (Item != null && ItemIcons.Instance != null)
        {
            texture = ItemIcons.Instance.Get(Item);
        }
        _icon.Texture = texture;
        _icon.Visible = texture != null;
        if (Selected)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        Font font = UiStyle.Hand;
        if (Item != null && (_icon == null || !_icon.Visible))
        {
            // No picture yet (or headless): an inked block in the item's colour.
            Vector2 inset = size * 0.24f;
            InkDraw.Shape(this, new[] { inset, new Vector2(size.X - inset.X, inset.Y), size - inset, new Vector2(inset.X, size.Y - inset.Y) }, Item.DebugColor, 2.0f, Index + 3);
        }
        else if (Item == null && EmptyGlyph != null)
        {
            InkDraw.Icon(this, EmptyGlyph.Value, new Rect2(size * 0.2f, size * 0.6f), Index + 5);
        }
        if (Count > 1)
        {
            string text = Count.ToString();
            Vector2 at = new(size.X - font.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.SmallSize).X - 6, size.Y - 6);
            DrawString(font, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, UiStyle.SmallSize, UiStyle.Paper);
            DrawString(font, at, text, HorizontalAlignment.Left, -1, UiStyle.SmallSize, UiStyle.Ink);
        }
        if (KeyHint.Length > 0)
        {
            DrawString(font, new Vector2(6, 16), KeyHint, HorizontalAlignment.Left, -1, 14, UiStyle.InkSoft);
        }
        if (Selected)
        {
            // A breathing inked ring round the chosen slot.
            float breathe = 2.0f + Mathf.Sin(_time * 5.0f) * 1.5f;
            Vector2 a = new(-breathe, -breathe);
            Vector2 b = size + new Vector2(breathe, breathe);
            Vector2[] ring = { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y), a };
            DrawPolyline(InkDraw.Wobble(ring, 1.2f, (int)(_time * 4.0f) % 3 + 11), UiStyle.Highlight.Darkened(0.35f), 3.5f, true);
        }
    }
}
