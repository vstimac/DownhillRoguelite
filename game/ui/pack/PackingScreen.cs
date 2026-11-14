using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The packing page: open while the pack stands on the ground (GameEvents.IsPackOpen). On the left
/// the pack in 2.5D; on the right the mat with whatever lies beside it, the load's figures and the
/// controls. Click a thing to lift it, point into the pack and click to set it down where the
/// ghost is green; Q/E turn it, R/F tip it, the wheel slides it lower into a gap. With empty hands
/// Q/E turn the view and the wheel cuts the upper layers away. The page moves nothing itself: it
/// asks the inventory through GameEvents and redraws what comes back.
/// </summary>
public partial class PackingScreen : ParchmentPanel
{
    private static readonly StringName ActionTurnLeft = "packing_turn_left";
    private static readonly StringName ActionTurnRight = "packing_turn_right";
    private static readonly StringName ActionTipForward = "packing_tip_forward";
    private static readonly StringName ActionTipSide = "packing_tip_side";

    public const int MatColumns = 6;
    public const int MatRows = 3;

    [Export(PropertyHint.MultilineText)] public string HelpText { get; set; } =
        "Click a thing to lift it  ·  click in the pack to set it down (green fits)  ·  right click: let go\n" +
        "Q / E turn  ·  R / F tip on its side  ·  wheel: slide it lower into a gap  ·  Alt+click: the thing behind\n" +
        "Empty hands: Q / E turn the pack, wheel cuts the top layers away  ·  X: arrange everything  ·  Tab or Esc: done";

    public PackGridView? View { get; private set; }
    /// <summary>The copy in the hand ("" for none).</summary>
    public string HeldId { get; private set; } = "";
    public int HeldOrientation { get; private set; } = GridOrientations.Identity;

    private readonly List<ItemSlot> _matSlots = new();
    private readonly List<string> _matIds = new();
    private Label? _info;
    private Label? _figures;
    private BackpackGrid? _occupancy;
    /// <summary>Wheel steps down into gaps lower in the column (0: where it lands from above).</summary>
    private int _layerPick;
    private Vector2I _layerColumn = new(-1, -1);
    private Vector3I _ghostOrigin;
    private bool _ghostValid;

    public override void _Ready()
    {
        Title = "Packing";
        Seed = 23.0f;
        Size = new Vector2(980, 624);
        base._Ready();
        ProcessMode = ProcessModeEnum.Always;

        View = new PackGridView { Position = new Vector2(20, 50), Size = new Vector2(420, 430), MouseFilter = MouseFilterEnum.Stop };
        View.GuiInput += OnViewInput;
        AddChild(View);
        InkButton left = new() { Glyph = InkButton.GlyphKind.Left, Position = new Vector2(170, 482), Size = new Vector2(40, 34) };
        left.Pressed += () => View.TurnBy(-1);
        AddChild(left);
        InkButton right = new() { Glyph = InkButton.GlyphKind.Right, Position = new Vector2(250, 482), Size = new Vector2(40, 34) };
        right.Pressed += () => View.TurnBy(1);
        AddChild(right);

        float matX = 470.0f;
        Label matTitle = UiStyle.MakeLabel("Beside the pack", UiStyle.TextSize, true);
        matTitle.Position = new Vector2(matX, 54);
        AddChild(matTitle);
        float cell = 70.0f;
        Control mat = new() { Position = new Vector2(matX - 6, 84), Size = new Vector2(MatColumns * (cell + 6) + 6, MatRows * (cell + 6) + 6), MouseFilter = MouseFilterEnum.Stop };
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

        _info = UiStyle.MakeLabel("", UiStyle.TextSize);
        _info.Position = new Vector2(matX, 326);
        _info.Size = new Vector2(Size.X - matX - 24, 56);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_info);
        _figures = UiStyle.MakeLabel("", UiStyle.TextSize);
        _figures.Position = new Vector2(matX, 372);
        _figures.Size = new Vector2(Size.X - matX - 24, 90);
        AddChild(_figures);

        InkButton arrange = new() { Text = "Arrange all", Paper = true, Position = new Vector2(matX, 468), Size = new Vector2(150, 42) };
        arrange.Pressed += Arrange;
        AddChild(arrange);
        InkButton done = new() { Text = "Done", Paper = true, Position = new Vector2(Size.X - 150, 468), Size = new Vector2(120, 42) };
        done.Pressed += GameEvents.RequestPackClose;
        AddChild(done);

        Label help = UiStyle.MakeLabel(HelpText, UiStyle.SmallSize);
        help.Position = new Vector2(24, 526);
        help.Size = new Vector2(Size.X - 48, 70);
        AddChild(help);

        GameEvents.PackOpenChanged += OnPackOpen;
        GameEvents.PackLayoutChanged += OnLayout;
        OnLayout(GameEvents.CurrentPackLayout);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.PackOpenChanged -= OnPackOpen;
        GameEvents.PackLayoutChanged -= OnLayout;
    }

    private void OnPackOpen(bool open)
    {
        if (open)
        {
            Vector2 screen = GetViewportRect().Size;
            Position = ((screen - Size) * 0.5f).Round();
            Hold("", GridOrientations.Identity);
            View?.SetCutHeight(int.MaxValue);
            Open();
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
            side = $"{offset:0} cm to the right";
        }
        else if (offset < -0.5f)
        {
            side = $"{-offset:0} cm to the left";
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
            left = $"\n{loose} left out: they stay behind";
        }
        _figures.Text = $"{layout.PackName}  {layout.Mass:0.0} kg\nWeight sits {load.CenterOfMass.Y:0.00} m up, {side}{left}";
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || !Visible || View == null)
        {
            return;
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
            Describe(layout, hover, false);
            return;
        }

        View.SetHover("");
        Describe(layout, HeldId, true);
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

    private void Describe(PackLayout layout, string instanceId, bool held)
    {
        if (_info == null)
        {
            return;
        }
        string text = "Nothing in hand.";
        if (instanceId.Length > 0 && layout.TryFind(instanceId, out PackLayoutEntry entry) && ItemText.AtPath(entry.ItemPath) is ItemDefinition item)
        {
            text = $"{item.DisplayName}  ·  {entry.MassKg:0.0} kg";
            if (held)
            {
                text = "In hand: " + text;
            }
        }
        if (_info.Text != text)
        {
            _info.Text = text;
        }
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

    /// <summary>Takes a copy into the hand ("" lets go: it stays where it was).</summary>
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
