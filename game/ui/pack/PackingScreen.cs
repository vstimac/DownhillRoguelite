using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The one pack page: open while the pack stands on the ground (GameEvents.IsPackOpen), docked at the
/// right so the carrier and the pack stay in sight on the left and the world goes on. Left column:
/// the pack in 2.5D and its figures. Right column: the mat with whatever lies beside the pack, what
/// the carrier wears and has in the pockets, and the chosen thing's description with what can be done
/// with it (eat, drink, wear, mend, drop). Nothing chosen, it shows the carrier's own figures.
/// Click a thing to lift it, point into the pack and click to set it down where the ghost is green;
/// Q/E turn it, R/F tip it, the wheel slides it lower into a gap. With empty hands Q/E turn the view
/// and the wheel cuts the upper layers away. The page moves nothing itself: it asks the inventory
/// through GameEvents and redraws what comes back.
/// </summary>
public partial class PackingScreen : ParchmentPanel
{
    private static readonly StringName ActionTurnLeft = "packing_turn_left";
    private static readonly StringName ActionTurnRight = "packing_turn_right";
    private static readonly StringName ActionTipForward = "packing_tip_forward";
    private static readonly StringName ActionTipSide = "packing_tip_side";

    public const int MatColumns = 7;
    public const int MatRows = 2;
    public const int PocketSlots = 6;
    public static readonly Vector2 PageSize = new(960, 760);

    private static readonly EquipSlot[] WornOrder =
    {
        EquipSlot.Head, EquipSlot.Face, EquipSlot.Torso, EquipSlot.InnerTorso, EquipSlot.Hands, EquipSlot.Waist,
        EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Back, EquipSlot.Accessory,
    };

    [Export(PropertyHint.MultilineText)] public string HelpText { get; set; } =
        "Click: lift  ·  click in the pack: set it down (green fits)  ·  right click: let go  ·  Q / E turn  ·  R / F tip\n" +
        "Empty hands: Q / E turn the pack, wheel cuts the top away  ·  walk, Tab or Esc: shoulder it";

    /// <summary>How far from the screen's right edge the page sits, in px.</summary>
    [Export] public float DockMargin { get; set; } = 24.0f;

    public PackGridView? View { get; private set; }
    /// <summary>The copy in the hand ("" for none).</summary>
    public string HeldId { get; private set; } = "";
    public int HeldOrientation { get; private set; } = GridOrientations.Identity;
    /// <summary>The kind of thing chosen for the description and the buttons ("" for none: the carrier's figures).</summary>
    public string SelectedItemId { get; private set; } = "";
    /// <summary>What the description shows now, for checks.</summary>
    public string DetailTitle => _detailTitle?.Text ?? "";
    /// <summary>The kinds in the pockets, in slot order.</summary>
    public IReadOnlyList<string> PocketIds => _pocketIds;
    public InkButton? UseButton => _use;

    private readonly List<ItemSlot> _matSlots = new();
    private readonly List<string> _matIds = new();
    private readonly List<ItemSlot> _pocketSlots = new();
    private readonly List<string> _pocketIds = new();
    private readonly Dictionary<EquipSlot, ItemSlot> _worn = new();
    private ItemSlot? _packSlot;
    private Label? _matHint;
    private Label? _noPockets;
    private Label? _figures;
    private BalanceLine? _balance;
    private ItemSlot? _detailIcon;
    private Label? _detailTitle;
    private Label? _detailSub;
    private ScrollContainer? _detailScroll;
    private Label? _detailBody;
    private Label? _detailValues;
    private InkButton? _use;
    private InkButton? _drop;
    private BackpackGrid? _occupancy;
    private string _carriedSignature = "";
    private string _shown = "?";
    private VitalsSnapshot? _vitals;
    private RunState? _run;
    private float _sheetAge;
    /// <summary>Wheel steps down into gaps lower in the column (0: where it lands from above).</summary>
    private int _layerPick;
    private Vector2I _layerColumn = new(-1, -1);
    private Vector3I _ghostOrigin;
    private bool _ghostValid;

    public override void _Ready()
    {
        Title = "Pack";
        Seed = 23.0f;
        Size = PageSize;
        // Over the world, but the world is meant to be seen beside it: nearly solid.
        Opacity = UiStyle.PageOpacity;
        base._Ready();
        ProcessMode = ProcessModeEnum.Always;

        // Left column: the pack itself.
        View = new PackGridView { Position = new Vector2(20, 56), Size = new Vector2(430, 400), MouseFilter = MouseFilterEnum.Stop };
        View.GuiInput += OnViewInput;
        AddChild(View);
        InkButton left = new() { Glyph = InkButton.GlyphKind.Left, Position = new Vector2(190, 462), Size = new Vector2(40, 32), TooltipText = "Turn the pack (←)" };
        left.Pressed += () => View.TurnBy(-1);
        AddChild(left);
        InkButton right = new() { Glyph = InkButton.GlyphKind.Right, Position = new Vector2(240, 462), Size = new Vector2(40, 32), TooltipText = "Turn the pack (→)" };
        right.Pressed += () => View.TurnBy(1);
        AddChild(right);
        _figures = UiStyle.MakeLabel("", UiStyle.TextSize);
        _figures.Position = new Vector2(24, 506);
        _figures.Size = new Vector2(426, 90);
        _figures.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_figures);
        _balance = new BalanceLine { Position = new Vector2(24, 574), Size = new Vector2(426, 30) };
        AddChild(_balance);
        InkButton arrange = new() { Text = "Arrange all  ·  X", Paper = true, Position = new Vector2(24, 636), Size = new Vector2(200, 40), TooltipText = "Pack everything as tight and low as it goes" };
        arrange.Pressed += Arrange;
        AddChild(arrange);

        // Right column: beside the pack, worn and pockets, the chosen thing.
        float x = 476.0f;
        AddChild(Heading("Beside the pack", new Vector2(x, 52)));
        float cell = 56.0f;
        Control mat = new() { Position = new Vector2(x - 6, 78), Size = new Vector2(MatColumns * (cell + 6) + 6, MatRows * (cell + 6) + 6), MouseFilter = MouseFilterEnum.Stop };
        mat.GuiInput += OnMatInput;
        AddChild(mat);
        for (int i = 0; i < MatColumns * MatRows; i++)
        {
            ItemSlot slot = new()
            {
                Position = new Vector2(6 + (i % MatColumns) * (cell + 6), 6 + (i / MatColumns) * (cell + 6)),
                Size = new Vector2(cell, cell),
                Index = i,
                SlotSeed = 9.0f,
            };
            slot.Clicked += OnMatSlotClicked;
            mat.AddChild(slot);
            _matSlots.Add(slot);
            _matIds.Add("");
        }
        // Beside the heading while the mat is empty: what it is for.
        _matHint = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.Muted);
        _matHint.Position = new Vector2(x + 150, 54);
        _matHint.Size = new Vector2(PageSize.X - x - 150 - 20, 24);
        _matHint.HorizontalAlignment = HorizontalAlignment.Right;
        _matHint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_matHint);

        AddChild(Heading("Worn  ·  click to take off", new Vector2(x, 220)));
        float small = 44.0f;
        _packSlot = new ItemSlot { Position = new Vector2(x, 246), Size = new Vector2(small, small), EmptyGlyph = InkIcon.Backpack, Index = 90, ShowFootprint = false };
        AddChild(_packSlot);
        for (int i = 0; i < WornOrder.Length; i++)
        {
            EquipSlot equip = WornOrder[i];
            int place = i + 1;
            ItemSlot slot = new()
            {
                Position = new Vector2(x + (place % 6) * (small + 6), 246 + (place / 6) * (small + 6)),
                Size = new Vector2(small, small),
                EmptyGlyph = ItemSlot.GlyphFor(equip),
                Index = (int)equip + 40,
                ShowFootprint = false,
                TooltipText = $"{equip}: nothing worn",
            };
            slot.Clicked += (s, button) => OnWornClicked(equip);
            AddChild(slot);
            _worn[equip] = slot;
        }
        float pocketsX = x + 6 * (small + 6) + 14;
        AddChild(Heading("Pockets", new Vector2(pocketsX, 220)));
        for (int i = 0; i < PocketSlots; i++)
        {
            ItemSlot slot = new()
            {
                Position = new Vector2(pocketsX + (i % 3) * (small + 6), 246 + (i / 3) * (small + 6)),
                Size = new Vector2(small, small),
                Index = 70 + i,
                SlotSeed = 5.0f,
                ShowFootprint = false,
            };
            slot.Clicked += OnPocketClicked;
            AddChild(slot);
            _pocketSlots.Add(slot);
            _pocketIds.Add("");
        }
        _noPockets = UiStyle.MakeLabel("None in what you wear", UiStyle.SmallSize, false, UiStyle.Muted);
        _noPockets.Position = new Vector2(pocketsX, 250);
        _noPockets.Size = new Vector2(PageSize.X - pocketsX - 20, 40);
        _noPockets.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _noPockets.Visible = false;
        AddChild(_noPockets);

        _detailIcon = new ItemSlot { Position = new Vector2(x, 356), Size = new Vector2(60, 60), Index = 99, Visible = false, ShowFootprint = false };
        AddChild(_detailIcon);
        _detailTitle = UiStyle.MakeLabel("", 22, true);
        _detailTitle.Position = new Vector2(x, 354);
        _detailTitle.Size = new Vector2(460, 30);
        _detailTitle.ClipText = true;
        AddChild(_detailTitle);
        _detailSub = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.Muted);
        _detailSub.Position = new Vector2(x, 386);
        _detailSub.Size = new Vector2(460, 24);
        _detailSub.ClipText = true;
        AddChild(_detailSub);
        _detailScroll = new ScrollContainer
        {
            Position = new Vector2(x, 428),
            Size = new Vector2(PageSize.X - x - 20, 192),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(_detailScroll);
        Control body = new() { CustomMinimumSize = new Vector2(_detailScroll.Size.X - 14, 0) };
        _detailScroll.AddChild(body);
        _detailBody = UiStyle.MakeLabel("", UiStyle.SmallSize);
        _detailBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailBody.Size = new Vector2(_detailScroll.Size.X - 14, 0);
        body.AddChild(_detailBody);
        // The carrier's figures stand in a right-aligned column over the same space, so the numbers line up.
        _detailValues = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.Ink);
        _detailValues.HorizontalAlignment = HorizontalAlignment.Right;
        _detailValues.Size = new Vector2(_detailScroll.Size.X - 24, 0);
        body.AddChild(_detailValues);
        _detailBody.Resized += () => body.CustomMinimumSize = new Vector2(body.CustomMinimumSize.X, Mathf.Max(_detailBody.Size.Y, _detailValues.Size.Y));
        _use = new InkButton { Text = "Eat", Paper = true, Position = new Vector2(x, 636), Size = new Vector2(120, 40), Visible = false };
        _use.Pressed += UseSelected;
        AddChild(_use);
        _drop = new InkButton { Text = "Drop", Paper = true, Position = new Vector2(x + 130, 636), Size = new Vector2(100, 40), Visible = false, TooltipText = "Put one down on the ground" };
        _drop.Pressed += DropSelected;
        AddChild(_drop);
        InkButton done = new() { Text = "Shoulder it  ·  Tab", Paper = true, Position = new Vector2(PageSize.X - 230, 636), Size = new Vector2(210, 40), TooltipText = "Put the pack back on (walking off does it too)" };
        done.Pressed += GameEvents.RequestPackClose;
        AddChild(done);

        ColorRect rule = new() { Color = new Color(UiStyle.Line, 0.8f), Position = new Vector2(22, 690), Size = new Vector2(PageSize.X - 44, 1), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(rule);
        Label help = UiStyle.MakeLabel(HelpText, UiStyle.SmallSize, false, UiStyle.Muted);
        help.Position = new Vector2(24, 700);
        help.Size = new Vector2(PageSize.X - 48, 50);
        AddChild(help);

        GameEvents.PackOpenChanged += OnPackOpen;
        GameEvents.PackLayoutChanged += OnLayout;
        GameEvents.GearChanged += OnGear;
        GameEvents.VitalsUpdated += OnVitals;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        OnLayout(GameEvents.CurrentPackLayout);
        OnGear(GameEvents.CurrentGear);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.PackOpenChanged -= OnPackOpen;
        GameEvents.PackLayoutChanged -= OnLayout;
        GameEvents.GearChanged -= OnGear;
        GameEvents.VitalsUpdated -= OnVitals;
    }

    private void OnVitals(VitalsSnapshot vitals) => _vitals = vitals;

    private static Label Heading(string text, Vector2 at)
    {
        Label label = UiStyle.MakeLabel(text, UiStyle.SmallSize + 1, true, UiStyle.Muted);
        label.Position = at;
        return label;
    }

    /// <summary>Docked at the screen's right, centred in its height; scaled down to fit a small window.</summary>
    public void Dock()
    {
        Vector2 screen = GetViewportRect().Size;
        float fit = Fit();
        Vector2 shown = PageSize * fit;
        Position = new Vector2(screen.X - DockMargin - shown.X, (screen.Y - shown.Y) * 0.5f).Round();
        PivotOffset = Vector2.Zero;
        Scale = new Vector2(fit, fit);
    }

    /// <summary>The page's scale: 1, or less when the window is too small for it.</summary>
    private float Fit()
    {
        Vector2 screen = GetViewportRect().Size;
        return Mathf.Clamp(Mathf.Min((screen.X - DockMargin * 2.0f) / PageSize.X, (screen.Y - DockMargin * 2.0f) / PageSize.Y), 0.01f, 1.0f);
    }

    private void OnPackOpen(bool open)
    {
        if (open)
        {
            Dock();
            Hold("", GridOrientations.Identity);
            View?.SetCutHeight(int.MaxValue);
            _carriedSignature = "";
            _shown = "?";
            Open();
            // Open() resets the scale and pivot for its unfold (it kept the docked place): scale it again.
            PivotOffset = Vector2.Zero;
            Scale = new Vector2(Fit(), Fit());
        }
        else
        {
            Hold("", GridOrientations.Identity);
            Close();
        }
    }

    private void OnLayout(PackLayout layout)
    {
        if (HeldId.Length > 0 && !layout.TryFind(HeldId, out PackLayoutEntry _))
        {
            Hold("", GridOrientations.Identity);
        }
        _occupancy = PackGeometry.Occupancy(layout, HeldId);
        RefreshMat(layout);
        RefreshFigures(layout);
        _carriedSignature = "";
    }

    private void RefreshMat(PackLayout layout)
    {
        int next = 0;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (entry.Packed || entry.InstanceId == HeldId || next >= _matSlots.Count)
            {
                continue;
            }
            _matSlots[next].SetItem(ItemText.AtPath(entry.ItemPath), 1);
            _matIds[next] = entry.InstanceId;
            next++;
        }
        for (int i = next; i < _matSlots.Count; i++)
        {
            _matSlots[i].SetItem(null, 0);
            _matIds[i] = "";
        }
        if (_matHint != null)
        {
            _matHint.Visible = next == 0;
            _matHint.Text = HeldId.Length > 0 ? "Click here to lay it beside the pack" : "Lift a thing, click here: out of the pack";
        }
    }

    /// <summary>Pockets: whatever is carried that isn't in the pack's layout (in it or beside it), one slot per kind.</summary>
    private void RefreshPockets()
    {
        Dictionary<string, int> counts = new();
        List<string> order = new();
        foreach (string id in GameEvents.CarriedItems)
        {
            if (!counts.ContainsKey(id))
            {
                order.Add(id);
            }
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        foreach (PackLayoutEntry entry in GameEvents.CurrentPackLayout.Entries)
        {
            if (counts.ContainsKey(entry.ItemId))
            {
                counts[entry.ItemId]--;
            }
        }
        int next = 0;
        foreach (string id in order)
        {
            if (counts[id] <= 0 || next >= _pocketSlots.Count)
            {
                continue;
            }
            _pocketSlots[next].SetItem(ItemText.Find(id), counts[id]);
            _pocketSlots[next].TooltipText = "";
            _pocketIds[next] = id;
            next++;
        }
        // As many pockets as the clothes worn have (things already in one always show).
        int have = Mathf.Clamp(Mathf.Max(GameEvents.CurrentGear.Pockets, next), 0, _pocketSlots.Count);
        for (int i = next; i < _pocketSlots.Count; i++)
        {
            _pocketSlots[i].SetItem(null, 0);
            _pocketSlots[i].TooltipText = "Empty pocket";
            _pocketIds[i] = "";
        }
        for (int i = 0; i < _pocketSlots.Count; i++)
        {
            _pocketSlots[i].Visible = i < have;
        }
        if (_noPockets != null)
        {
            _noPockets.Visible = have == 0;
        }
    }

    private void RefreshFigures(PackLayout layout)
    {
        if (_figures == null)
        {
            return;
        }
        LoadSnapshot load = GameEvents.CurrentLoad;
        string side = "centred";
        float offset = load.CenterOfMass.X * 100.0f;
        if (offset > 0.5f)
        {
            side = $"{offset:0} cm right";
        }
        else if (offset < -0.5f)
        {
            side = $"{-offset:0} cm left";
        }
        int loose = 0;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (!entry.Packed)
            {
                loose++;
            }
        }
        string left = "";
        if (loose > 0)
        {
            left = $"\n{loose} beside the pack: left behind if you walk off";
        }
        _figures.Text = $"{layout.PackName}  ·  {layout.Mass:0.0} kg\nCentre of weight {load.CenterOfMass.Y:0.00} m up, {side}{left}";
        if (_balance != null)
        {
            _balance.Offset = load.CenterOfMass.X;
            _balance.QueueRedraw();
        }
    }

    /// <summary>
    /// Where the load's weight sits from side to side, as a marker on a line: the middle is even, and
    /// a few centimetres off pulls the carrier that way on every step (BalanceController).
    /// </summary>
    private partial class BalanceLine : Control
    {
        /// <summary>The centre of mass's sideways offset, in m (positive: right).</summary>
        public float Offset { get; set; }
        /// <summary>The ends of the line stand for this much offset, in m.</summary>
        public float FullScale { get; set; } = 0.08f;
        /// <summary>Beyond this the marker turns to the warning colour, in m.</summary>
        public float Uneven { get; set; } = 0.025f;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            TooltipText = "Where the weight sits from side to side: off the middle, the load pulls you that way";
        }

        public override void _Draw()
        {
            Font font = UiStyle.Hand;
            int size = UiStyle.SmallSize;
            float y = Size.Y * 0.5f;
            DrawString(font, new Vector2(0.0f, y + 5.0f), "L", HorizontalAlignment.Left, -1, size, UiStyle.Muted);
            DrawString(font, new Vector2(Size.X - 10.0f, y + 5.0f), "R", HorizontalAlignment.Left, -1, size, UiStyle.Muted);
            float left = 22.0f;
            float right = Size.X - 22.0f;
            float middle = (left + right) * 0.5f;
            DrawLine(new Vector2(left, y), new Vector2(right, y), UiStyle.Line, 2.0f);
            DrawLine(new Vector2(middle, y - 7.0f), new Vector2(middle, y + 7.0f), UiStyle.InkSoft, 2.0f);
            float share = Mathf.Clamp(Offset / Mathf.Max(FullScale, 0.001f), -1.0f, 1.0f);
            Vector2 at = new(middle + share * (right - middle), y);
            Color colour = Mathf.Abs(Offset) > Uneven ? UiStyle.Warning : UiStyle.Highlight;
            DrawCircle(at, 6.5f, colour);
            DrawArc(at, 6.5f, 0.0f, Mathf.Tau, 20, UiStyle.Paper, 1.5f, true);
        }
    }

    private void OnGear(GearSnapshot gear)
    {
        foreach ((EquipSlot equip, ItemSlot slot) in _worn)
        {
            WornPiece piece = gear.In(equip);
            ItemDefinition? item = null;
            if (!piece.Empty)
            {
                item = ItemText.Find(piece.ItemId);
            }
            slot.SetItem(item, 1);
            if (item == null)
            {
                slot.Durability = -1.0f;
                slot.TooltipText = $"{equip}: nothing worn";
            }
            else
            {
                slot.Durability = piece.Fraction;
                string state = $"{piece.Durability:0} / {piece.MaxDurability:0}";
                if (piece.Broken)
                {
                    state = "worn through: mend it at a station";
                }
                slot.TooltipText = $"{piece.Name}\n{state}\nclick to take off";
            }
            slot.QueueRedraw();
        }
        if (_packSlot != null && gear.Backpack.MaxDurability > 0.0f)
        {
            _packSlot.Durability = gear.Backpack.Fraction;
            _packSlot.TooltipText = $"Backpack\n{gear.Backpack.Durability:0} / {gear.Backpack.MaxDurability:0}";
            _packSlot.QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || !Visible || View == null)
        {
            return;
        }
        string carried = string.Join(",", GameEvents.CarriedItems);
        if (carried != _carriedSignature)
        {
            _carriedSignature = carried;
            RefreshPockets();
            RefreshFigures(GameEvents.CurrentPackLayout);
            if (SelectedItemId.Length > 0 && !GameEvents.CarriedItems.Contains(SelectedItemId) && !Worn(SelectedItemId))
            {
                SelectedItemId = "";
            }
            _shown = "?";
        }
        PackLayout layout = GameEvents.CurrentPackLayout;
        Vector2 at = View.GetLocalMousePosition();
        bool inside = new Rect2(Vector2.Zero, View.Size).HasPoint(at);
        if (HeldId.Length == 0)
        {
            View.HideGhost();
            string hover = "";
            if (inside && View.EntryAt(at, false, out PackLayoutEntry under))
            {
                hover = under.InstanceId;
            }
            View.SetHover(hover);
            ShowDetail(hover, (float)delta);
            return;
        }

        View.SetHover("");
        ShowDetail(HeldId, (float)delta);
        _ghostValid = false;
        if (!inside || _occupancy == null || !HeldItem(layout, out ItemDefinition? item) || item == null)
        {
            View.HideGhost();
            return;
        }
        Vector3I size = GridOrientations.RotatedSize(item.SizeCells, HeldOrientation);
        if (!View.ColumnAt(at, size, out Vector2I column))
        {
            View.HideGhost();
            return;
        }
        if (column != _layerColumn)
        {
            _layerColumn = column;
            _layerPick = 0;
        }
        int rest = _occupancy.DropHeight(size, column.X, column.Y);
        List<int> heights = _occupancy.RestingHeights(size, column.X, column.Y);
        if (heights.Count > 0)
        {
            int start = Mathf.Max(0, heights.IndexOf(rest));
            _layerPick = Mathf.Clamp(_layerPick, -start, heights.Count - 1 - start);
            rest = heights[start + _layerPick];
        }
        _ghostValid = rest >= 0;
        if (_ghostValid)
        {
            _ghostOrigin = new Vector3I(column.X, rest, column.Y);
        }
        else
        {
            // Nothing fits in this column: show it sitting on the rim, in red.
            _ghostOrigin = new Vector3I(column.X, Mathf.Max(0, layout.GridSize.Y - size.Y), column.Y);
        }
        View.ShowGhost(_ghostOrigin, size, HeldOrientation, _ghostValid);
    }

    private static bool Worn(string itemId)
    {
        GearSnapshot gear = GameEvents.CurrentGear;
        foreach (EquipSlot equip in WornOrder)
        {
            if (gear.In(equip).ItemId == itemId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The description: the thing in the hand, else the thing pointed at in the pack, else the chosen
    /// kind, else the carrier's figures. Redrawn only when what it shows changes (the figures each second).
    /// </summary>
    private void ShowDetail(string instanceId, float delta)
    {
        PackLayout layout = GameEvents.CurrentPackLayout;
        ItemDefinition? item = null;
        string where = "";
        float mass = -1.0f;
        if (instanceId.Length > 0 && layout.TryFind(instanceId, out PackLayoutEntry entry))
        {
            item = ItemText.AtPath(entry.ItemPath);
            mass = entry.MassKg;
            where = entry.Packed ? "In the pack" : "Beside the pack";
            if (instanceId == HeldId)
            {
                where = "In hand";
            }
        }
        else if (SelectedItemId.Length > 0)
        {
            item = ItemText.Find(SelectedItemId);
            where = WhereIs(SelectedItemId);
        }
        string key = item == null ? "carrier" : item.ItemId + "|" + where + "|" + GameEvents.InShelter;
        _sheetAge += delta;
        if (key == _shown && !(item == null && _sheetAge > 1.0f))
        {
            return;
        }
        _shown = key;
        _sheetAge = 0.0f;
        Describe(item, where, mass);
    }

    private string WhereIs(string itemId)
    {
        if (Worn(itemId))
        {
            return "Worn";
        }
        if (_pocketIds.Contains(itemId))
        {
            return "In a pocket";
        }
        foreach (PackLayoutEntry entry in GameEvents.CurrentPackLayout.Entries)
        {
            if (entry.ItemId == itemId)
            {
                return entry.Packed ? "In the pack" : "Beside the pack";
            }
        }
        return "";
    }

    private void Describe(ItemDefinition? item, string where, float mass)
    {
        if (_detailTitle == null || _detailSub == null || _detailBody == null || _detailValues == null || _detailIcon == null || _use == null || _drop == null)
        {
            return;
        }
        if (item == null)
        {
            _detailIcon.Visible = false;
            _detailTitle.Position = new Vector2(_detailIcon.Position.X, _detailTitle.Position.Y);
            _detailSub.Position = new Vector2(_detailIcon.Position.X, _detailSub.Position.Y);
            _detailTitle.Text = "The carrier";
            _detailSub.Text = "Choose a thing to read about it";
            (string names, string values) = CarrierSheet.Columns(CarrierSheet.Text(_vitals, _run));
            _detailBody.Text = names;
            _detailValues.Text = values;
            _use.Visible = false;
            _drop.Visible = false;
            return;
        }
        float textX = _detailIcon.Position.X + _detailIcon.Size.X + 12.0f;
        _detailIcon.Visible = true;
        _detailIcon.SetItem(item, 1);
        _detailTitle.Position = new Vector2(textX, _detailTitle.Position.Y);
        _detailSub.Position = new Vector2(textX, _detailSub.Position.Y);
        _detailTitle.Text = item.DisplayName;
        if (mass < 0.0f)
        {
            mass = item.MassKg;
        }
        _detailSub.Text = where.Length > 0 ? $"{where}  ·  {mass:0.0} kg" : $"{mass:0.0} kg";
        _detailValues.Text = "";
        string text = ItemText.Describe(item) + "\n" + ItemText.Properties(item) + "\n\n" + ItemText.HowToUse(item);
        _use.Visible = item.Category is ItemCategory.Food or ItemCategory.Water or ItemCategory.Clothing or ItemCategory.Material || ItemText.OpensMap(item) || item.UsableAlone;
        _use.Text = UseVerb(item);
        if (item.Cookable && !GameEvents.InShelter)
        {
            text += "\n(cook it at a station)";
        }
        else if (item.Category == ItemCategory.Material && !GameEvents.InShelter)
        {
            text += "\n(mending: only at a station)";
        }
        _detailBody.Text = text;
        if (_detailScroll != null)
        {
            _detailScroll.ScrollVertical = 0;
        }
        _drop.Visible = where != "Worn";
        _use.QueueRedraw();
        _drop.QueueRedraw();
    }

    private static string UseVerb(ItemDefinition item)
    {
        if (ItemText.OpensMap(item))
        {
            return "Map";
        }
        if (item.Category == ItemCategory.Water)
        {
            return "Drink";
        }
        if (item.Category == ItemCategory.Clothing)
        {
            return "Wear";
        }
        if (item.Cookable && GameEvents.InShelter)
        {
            return "Cook";
        }
        if (item.Category == ItemCategory.Material)
        {
            return "Mend";
        }
        if (item.UsableAlone && item.Category != ItemCategory.Food)
        {
            return "Use";
        }
        return "Eat";
    }

    /// <summary>Chooses a kind of thing for the description and the buttons ("" for the carrier's figures); its packed copies are outlined.</summary>
    public void SelectItem(string itemId)
    {
        SelectedItemId = itemId;
        _shown = "?";
        List<string> copies = new();
        if (itemId.Length > 0)
        {
            foreach (PackLayoutEntry entry in GameEvents.CurrentPackLayout.Entries)
            {
                if (entry.Packed && entry.ItemId == itemId)
                {
                    copies.Add(entry.InstanceId);
                }
            }
        }
        View?.SetMarked(copies);
        if (IsOpen && Visible)
        {
            ShowDetail(HeldId, 0.0f);
        }
    }

    /// <summary>Eats, drinks, wears, mends or reads the chosen kind (one of it).</summary>
    public void UseSelected()
    {
        ItemDefinition? item = ItemText.Find(SelectedItemId);
        if (item == null)
        {
            return;
        }
        if (ItemText.OpensMap(item))
        {
            ItemText.ToggleMap();
        }
        else if (item.Cookable && GameEvents.InShelter)
        {
            GameEvents.RequestCook(item.ItemId);
        }
        else if (item.Category == ItemCategory.Food || item.Category == ItemCategory.Water || item.UsableAlone)
        {
            GameEvents.RequestUseItem(item.ItemId);
        }
        else if (item.Category == ItemCategory.Clothing)
        {
            Hold("", GridOrientations.Identity);
            GameEvents.RequestWear(item.ItemId);
        }
        else if (item.Category == ItemCategory.Material)
        {
            GameEvents.RequestMend(item.ItemId);
        }
        _detailIcon?.Pop();
        _shown = "?";
    }

    /// <summary>Puts one of the chosen kind down on the ground in front of the carrier.</summary>
    public void DropSelected()
    {
        if (SelectedItemId.Length == 0)
        {
            return;
        }
        Hold("", GridOrientations.Identity);
        GameEvents.RequestDrop(SelectedItemId);
        _shown = "?";
    }

    private bool HeldItem(PackLayout layout, out ItemDefinition? item)
    {
        item = null;
        if (HeldId.Length == 0 || !layout.TryFind(HeldId, out PackLayoutEntry entry))
        {
            return false;
        }
        item = ItemText.AtPath(entry.ItemPath);
        return item != null;
    }

    /// <summary>Takes a copy into the hand ("" lets go: it stays where it was); the thing in hand is the chosen one.</summary>
    public void Hold(string instanceId, int orientation)
    {
        HeldId = instanceId;
        HeldOrientation = orientation;
        _layerPick = 0;
        _layerColumn = new Vector2I(-1, -1);
        _ghostValid = false;
        View?.SetHeld(instanceId);
        PackLayout layout = GameEvents.CurrentPackLayout;
        _occupancy = PackGeometry.Occupancy(layout, HeldId);
        RefreshMat(layout);
        if (instanceId.Length > 0 && layout.TryFind(instanceId, out PackLayoutEntry entry))
        {
            SelectItem(entry.ItemId);
        }
    }

    /// <summary>Sets the held copy down at a cell; it stays in the hand if it doesn't fit there. Returns whether it went in.</summary>
    public bool PlaceHeld(Vector3I origin)
    {
        if (HeldId.Length == 0)
        {
            return false;
        }
        string id = HeldId;
        GameEvents.RequestPackPlace(id, origin, HeldOrientation);
        if (GameEvents.CurrentPackLayout.TryFind(id, out PackLayoutEntry now) && now.Packed && now.Origin == origin && now.Orientation == HeldOrientation)
        {
            Hold("", GridOrientations.Identity);
            return true;
        }
        return false;
    }

    /// <summary>Lays the held copy on the mat (taking it out of the pack if it was in).</summary>
    public void PutHeldOnMat()
    {
        if (HeldId.Length == 0)
        {
            return;
        }
        string id = HeldId;
        Hold("", GridOrientations.Identity);
        GameEvents.RequestPackTakeOut(id);
    }

    /// <summary>Turns the held copy by a quarter step, if its rule allows the result.</summary>
    public void TurnHeld(int step, int times)
    {
        if (!HeldItem(GameEvents.CurrentPackLayout, out ItemDefinition? item) || item == null)
        {
            return;
        }
        int orientation = HeldOrientation;
        for (int i = 0; i < times; i++)
        {
            orientation = GridOrientations.Compose(step, orientation);
        }
        if (GridOrientations.Allows(item.Orientation, orientation))
        {
            HeldOrientation = orientation;
        }
    }

    private void Arrange()
    {
        Hold("", GridOrientations.Identity);
        GameEvents.RequestPackArrange();
    }

    private void OnViewInput(InputEvent @event)
    {
        if (View == null || @event is not InputEventMouseButton mouse || !mouse.Pressed)
        {
            return;
        }
        if (mouse.ButtonIndex == MouseButton.WheelDown || mouse.ButtonIndex == MouseButton.WheelUp)
        {
            int step = 1;
            if (mouse.ButtonIndex == MouseButton.WheelUp)
            {
                step = -1;
            }
            if (HeldId.Length > 0)
            {
                _layerPick += step;
            }
            else
            {
                int top = GameEvents.CurrentPackLayout.GridSize.Y;
                View.SetCutHeight(Mathf.Min(View.CutHeight, top) - step);
            }
        }
        else if (mouse.ButtonIndex == MouseButton.Right)
        {
            Hold("", GridOrientations.Identity);
        }
        else if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (HeldId.Length > 0)
            {
                if (_ghostValid)
                {
                    PlaceHeld(_ghostOrigin);
                }
            }
            else if (View.EntryAt(mouse.Position, mouse.AltPressed, out PackLayoutEntry entry))
            {
                Hold(entry.InstanceId, entry.Orientation);
            }
        }
        AcceptEvent();
    }

    private void OnMatInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && (mouse.ButtonIndex == MouseButton.Left || mouse.ButtonIndex == MouseButton.Right))
        {
            PutHeldOnMat();
        }
    }

    private void OnMatSlotClicked(ItemSlot slot, int button)
    {
        if (HeldId.Length > 0)
        {
            PutHeldOnMat();
            return;
        }
        if (button == (int)MouseButton.Left && slot.Index < _matIds.Count && _matIds[slot.Index].Length > 0)
        {
            Hold(_matIds[slot.Index], GridOrientations.Identity);
        }
    }

    /// <summary>A pocket: chooses that kind (double or right click uses it).</summary>
    private void OnPocketClicked(ItemSlot slot, int button)
    {
        int index = slot.Index - 70;
        if (index < 0 || index >= _pocketIds.Count || _pocketIds[index].Length == 0)
        {
            return;
        }
        Hold("", GridOrientations.Identity);
        SelectItem(_pocketIds[index]);
        slot.Pop();
        if (button == (int)MouseButton.Right || slot.LastDoubleClick)
        {
            UseSelected();
        }
    }

    /// <summary>A worn slot: with clothes in hand they are put on; otherwise what is worn there comes off.</summary>
    private void OnWornClicked(EquipSlot equip)
    {
        if (HeldItem(GameEvents.CurrentPackLayout, out ItemDefinition? held) && held != null && held.Category == ItemCategory.Clothing)
        {
            Hold("", GridOrientations.Identity);
            GameEvents.RequestWear(held.ItemId);
            return;
        }
        GameEvents.RequestTakeOff(equip);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || !Visible || View == null || !GameEvents.IsPackOpen)
        {
            return;
        }
        bool holding = HeldId.Length > 0;
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.PhysicalKeycode == Key.X)
        {
            Arrange();
        }
        else if (@event is InputEventKey arrow && arrow.Pressed && !arrow.Echo && arrow.PhysicalKeycode == Key.Left)
        {
            View.TurnBy(-1);
        }
        else if (@event is InputEventKey other && other.Pressed && !other.Echo && other.PhysicalKeycode == Key.Right)
        {
            View.TurnBy(1);
        }
        else if (@event.IsActionPressed(ActionTurnLeft))
        {
            if (holding)
            {
                TurnHeld(GridOrientations.YawQuarter, 1);
            }
            else
            {
                View.TurnBy(-1);
            }
        }
        else if (@event.IsActionPressed(ActionTurnRight))
        {
            if (holding)
            {
                TurnHeld(GridOrientations.YawQuarter, 3);
            }
            else
            {
                View.TurnBy(1);
            }
        }
        else if (@event.IsActionPressed(ActionTipForward))
        {
            TurnHeld(GridOrientations.TipForwardQuarter, 1);
        }
        else if (@event.IsActionPressed(ActionTipSide))
        {
            TurnHeld(GridOrientations.TipSideQuarter, 1);
        }
        else
        {
            return;
        }
        GetViewport().SetInputAsHandled();
    }
}
