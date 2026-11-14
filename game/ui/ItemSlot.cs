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
    /// <summary>Wear of the piece shown, 0 to 1 (a bar along the bottom); negative hides the bar.</summary>
    public float Durability { get; set; } = -1.0f;

    /// <summary>The inked garment glyph for a worn slot.</summary>
    public static InkIcon GlyphFor(Core.EquipSlot slot)
    {
        return slot switch
        {
            Core.EquipSlot.Head => InkIcon.Hat,
            Core.EquipSlot.Face => InkIcon.Hat,
            Core.EquipSlot.InnerTorso => InkIcon.Shirt,
            Core.EquipSlot.Torso => InkIcon.Shirt,
            Core.EquipSlot.Legs => InkIcon.Trousers,
            Core.EquipSlot.Hands => InkIcon.Glove,
            Core.EquipSlot.Feet => InkIcon.Shoe,
            Core.EquipSlot.Cape => InkIcon.Cape,
            _ => InkIcon.Backpack,
        };
    }

    private ColorRect? _paper;
    private TextureRect? _icon;
    private ShaderMaterial? _iconMaterial;
    private float _time;
    private bool _hover;
    private FootprintMark? _footprint;
    private bool _dragging;
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
        _footprint = new FootprintMark { Visible = false };
        AddChild(_footprint);
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        Resized += Layout;
        Layout();
        // Sixty of these wait in closed pages: only a slot on screen needs its icon checked each frame.
        VisibilityChanged += () => SetProcess(IsVisibleInTree());
        SetProcess(IsVisibleInTree());
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
        if (_footprint != null)
        {
            _footprint.Size = Size;
        }
        PivotOffset = Size * 0.5f;
    }

    /// <summary>Shows an item (null for empty) and a count; a new item or a bigger stack pops.</summary>
    public void SetItem(ItemDefinition? item, int count)
    {
        bool changed = item != Item || count > Count;
        Item = item;
        Count = count;
        if (_footprint != null)
        {
            _footprint.Visible = item != null && ShowFootprint;
            _footprint.Cells = item?.SizeCells ?? Vector3I.Zero;
            _footprint.QueueRedraw();
        }
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
    /// <summary>Draws the item's footprint in the pack (cells across × deep, and how many tall) in the top-right corner.</summary>
    public bool ShowFootprint { get; set; } = true;
    /// <summary>Takes items dragged from the inventory page without being draggable itself (the gear sheet).</summary>
    public bool AcceptsDrops { get; set; }
    /// <summary>Whether the last click was a double-click / had Shift held (read in Clicked handlers).</summary>
    public bool LastDoubleClick { get; private set; }
    public bool LastShift { get; private set; }
    /// <summary>Called with (from, to) slot indexes when a dragged item is dropped here.</summary>
    public System.Action<int, int>? DroppedOn { get; set; }
    /// <summary>Called with this slot's index when its item was dragged off and dropped where nothing took it.</summary>
    public System.Action<int>? DraggedAway { get; set; }

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
        _dragging = true;
        return Index;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return (Draggable || AcceptsDrops) && data.VariantType == Variant.Type.Int;
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
            if (_dragging)
            {
                _dragging = false;
                if (!GetViewport().GuiIsDragSuccessful())
                {
                    DraggedAway?.Invoke(Index);
                }
            }
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
            LastDoubleClick = button.DoubleClick;
            LastShift = button.ShiftPressed;
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
        if (Item != null && (_icon == null || !_icon.Visible) && Item.Category == ItemCategory.Clothing)
        {
            // No picture yet (or headless): their garment, inked, tinted with the item's colour.
            InkDraw.Icon(this, GlyphFor(Item.Slot), new Rect2(size * 0.15f, size * 0.7f), Index + 3);
            DrawCircle(size * 0.5f, size.X * 0.32f, new Color(Item.DebugColor, 0.25f));
        }
        else if (Item != null && (_icon == null || !_icon.Visible))
        {
            // No picture yet (or headless): an inked block in the item's colour.
            Vector2 inset = size * 0.24f;
            InkDraw.Shape(this, new[] { inset, new Vector2(size.X - inset.X, inset.Y), size - inset, new Vector2(inset.X, size.Y - inset.Y) }, Item.DebugColor, 2.0f, Index + 3);
        }
        else if (Item == null && EmptyGlyph != null)
        {
            InkDraw.Icon(this, EmptyGlyph.Value, new Rect2(size * 0.2f, size * 0.6f), Index + 5);
        }
        if (Durability >= 0.0f)
        {
            Rect2 bar = new(new Vector2(6, size.Y - 9), new Vector2(size.X - 12, 5));
            DrawRect(bar, new Color(0.1f, 0.08f, 0.06f, 0.7f));
            Color wear = UiStyle.Warning.Lerp(UiStyle.Stamina, Mathf.Clamp(Durability, 0.0f, 1.0f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(Durability, 0.0f, 1.0f), bar.Size.Y)), wear);
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
