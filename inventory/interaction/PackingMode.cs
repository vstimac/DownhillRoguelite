using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Repacking in the world, at a station's shelter (or anywhere with AllowAnywhere). Tab sets the pack
/// down in front of the carrier with a close-up camera;
/// click an item (in the pack or on the mat) to pick it up, aim at a column of the pack and click
/// to drop it in at the lowest spot it fits (ghost: green fits, red doesn't). Q/E turn it, R/F tip
/// it, right click puts it back on the mat. Tab again puts the pack back on.
/// While packing, GameEvents.IsPacking holds movement and balance input.
/// </summary>
public partial class PackingMode : Node3D
{
    private static readonly StringName ActionToggle = "inventory_toggle_packing";
    private static readonly StringName ActionPause = "pause";
    private static readonly StringName ActionPlace = "packing_place";
    private static readonly StringName ActionCancel = "packing_cancel";
    private static readonly StringName ActionTurnLeft = "packing_turn_left";
    private static readonly StringName ActionTurnRight = "packing_turn_right";
    private static readonly StringName ActionTipForward = "packing_tip_forward";
    private static readonly StringName ActionTipSide = "packing_tip_side";

    [Export] public CarrierInventory? Inventory { get; set; }
    [Export] public PlayerController? PlayerBody { get; set; }
    /// <summary>Camera to return to when the pack goes back on.</summary>
    [Export] public Camera3D? PlayerCamera { get; set; }
    /// <summary>Allow packing anywhere (debug scenes without stations); otherwise only in a station's shelter.</summary>
    [Export] public bool AllowAnywhere { get; set; }

    [ExportGroup("Station")]
    /// <summary>How far in front of the carrier the pack is set down, in m.</summary>
    [Export] public float StationDistance { get; set; } = 0.9f;
    /// <summary>Camera position relative to the set-down pack (+Z toward the carrier), in m.</summary>
    [Export] public Vector3 CameraOffset { get; set; } = new(0.0f, 1.3f, 1.15f);
    /// <summary>Point the camera looks at, relative to the set-down pack, in m.</summary>
    [Export] public Vector3 CameraTarget { get; set; } = new(0.0f, 0.3f, 0.3f);
    [Export] public float CameraFov { get; set; } = 50.0f;
    /// <summary>Ground is searched this far above and below the station point, in m.</summary>
    [Export] public float GroundSearch { get; set; } = 2.0f;
    /// <summary>The set-down pack floats this far above the ground so the mat doesn't flicker against it, in m.</summary>
    [Export] public float StationLift { get; set; } = 0.012f;
    /// <summary>Delay before --open-pack opens packing mode, in s.</summary>
    [Export] public float OpenPackDelay { get; set; } = 0.5f;

    public bool Active { get; private set; }

    private Node3D? _station;
    private Camera3D? _camera;
    private PackView? _view;
    private ItemDefinition? _held;
    private int _heldOrientation = GridOrientations.Identity;
    /// <summary>Mouse wheel steps the held item down into gaps lower in the pack (0: where it drops from above).</summary>
    private int _layerPick;
    private Vector2I _layerColumn = new(-1, -1);
    private Vector3I _ghostOrigin;
    private bool _ghostValid;

    public override async void _Ready()
    {
        // The pack screen keeps working while the world is stopped for it (PageFreeze).
        ProcessMode = ProcessModeEnum.Always;
        // For rendered checks (--write-movie): `-- --open-pack` sets the pack down once the carrier has landed.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--open-pack") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            TryEnter();
        }
    }

    /// <summary>The pack screen's controls, along the bottom of the screen while packing.</summary>
    [Export(PropertyHint.MultilineText)] public string HelpText { get; set; } =
        "Click an item to lift it  ·  click in the pack to place it (green fits)  ·  click away to set it on the mat\n" +
        "Q / E turn  ·  R / F tip on its side  ·  wheel: slide it lower into a gap  ·  Alt+click: the item underneath  ·  X: arrange everything  ·  Esc or Tab: done";

    private CanvasLayer? _help;

    private void ShowHelp(bool show)
    {
        if (show && _help == null)
        {
            _help = new CanvasLayer { Layer = 30 };
            Label label = new()
            {
                Text = HelpText,
                HorizontalAlignment = HorizontalAlignment.Center,
                AnchorLeft = 0.0f,
                AnchorRight = 1.0f,
                AnchorTop = 1.0f,
                AnchorBottom = 1.0f,
                OffsetTop = -70.0f,
                OffsetBottom = -14.0f,
            };
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeColorOverride("font_color", new Color(1.0f, 0.96f, 0.88f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.8f));
            label.AddThemeConstantOverride("outline_size", 6);
            _help.AddChild(label);
            AddChild(_help);
        }
        else if (!show && _help != null)
        {
            _help.QueueFree();
            _help = null;
        }
    }

    /// <summary>True while the pause menu or title screen holds the game (not the pages' own freeze).</summary>
    private bool MenuPaused => GetTree().Paused && !GameEvents.PagesFrozen;

    public override void _Input(InputEvent @event)
    {
        // Esc leaves the pack screen too, before the pause menu sees it.
        if (Active && !MenuPaused && @event.IsActionPressed(ActionPause))
        {
            Exit();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (MenuPaused)
        {
            return;
        }
        // X auto-arranges the whole pack, heaviest first, low and close to the back.
        if (Active && @event is InputEventKey key && key.Pressed && !key.Echo && key.PhysicalKeycode == Key.X)
        {
            AutoArrange();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed(ActionToggle))
        {
            if (Active)
            {
                Exit();
            }
            else
            {
                TryEnter();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Active)
        {
            return;
        }

        if (_held != null && @event is InputEventMouseButton wheel && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelDown || wheel.ButtonIndex == MouseButton.WheelUp))
        {
            // The wheel slides the held item down into a gap under other things, or back up.
            if (wheel.ButtonIndex == MouseButton.WheelDown)
            {
                _layerPick++;
            }
            else
            {
                _layerPick--;
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed(ActionPlace))
        {
            bool behind = @event is InputEventMouseButton mouse && mouse.AltPressed;
            Click(behind);
        }
        else if (@event.IsActionPressed(ActionCancel))
        {
            PutBack();
        }
        else if (@event.IsActionPressed(ActionTurnLeft))
        {
            Turn(GridOrientations.YawQuarter, 1);
        }
        else if (@event.IsActionPressed(ActionTurnRight))
        {
            Turn(GridOrientations.YawQuarter, 3);
        }
        else if (@event.IsActionPressed(ActionTipForward))
        {
            Turn(GridOrientations.TipForwardQuarter, 1);
        }
        else if (@event.IsActionPressed(ActionTipSide))
        {
            Turn(GridOrientations.TipSideQuarter, 1);
        }
        else
        {
            return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!Active || _camera == null || _station == null || _view == null)
        {
            return;
        }
        // Slid out of the fire's shelter: the pack goes back on.
        if (!GameEvents.InShelter && !AllowAnywhere)
        {
            Exit();
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Transform3D toStation = _station.GlobalTransform.AffineInverse();
        Vector3 from = toStation * _camera.ProjectRayOrigin(mouse);
        Vector3 direction = (toStation.Basis * _camera.ProjectRayNormal(mouse)).Normalized();

        if (_held == null)
        {
            _view.HideGhost();
            _view.SetHover(_view.ItemUnder(from, direction));
            return;
        }

        _view.SetHover(null);
        Vector3I size = GridOrientations.RotatedSize(_held.SizeCells, _heldOrientation);
        if (Inventory?.Grid == null || !_view.TopPlaneHit(from, direction, out Vector3 point))
        {
            _ghostValid = false;
            _view.HideGhost();
            return;
        }

        Vector2I column = _view.ColumnAt(point, size);
        if (column != _layerColumn)
        {
            _layerColumn = column;
            _layerPick = 0;
        }
        int rest = Inventory.Grid.DropHeight(size, column.X, column.Y);
        List<int> heights = Inventory.Grid.RestingHeights(size, column.X, column.Y);
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
            // Nothing fits: show it sitting on the rim, in red.
            _ghostOrigin = new Vector3I(column.X, Inventory.Grid.Size.Y - size.Y, column.Y);
        }
        _view.ShowGhost(_ghostOrigin, size, _ghostValid);
    }

    /// <summary>Opens packing if allowed here (station shelter, or AllowAnywhere); returns whether it opened.</summary>
    public bool TryOpen()
    {
        if (!Active)
        {
            TryEnter();
        }
        return Active;
    }

    /// <summary>Puts the pack back on.</summary>
    public void Close()
    {
        if (Active)
        {
            Exit();
        }
    }

    private void TryEnter()
    {
        if (Inventory?.Backpack == null || Inventory.Grid == null || PlayerBody == null)
        {
            return;
        }
        if (!AllowAnywhere && !GameEvents.InShelter)
        {
            return;
        }
        LocomotionState state = PlayerBody.State;
        if (state == LocomotionState.Fallen || state == LocomotionState.Airborne || state == LocomotionState.Sliding)
        {
            return;
        }

        Vector3 forward = new Vector3(-PlayerBody.GlobalBasis.Z.X, 0.0f, -PlayerBody.GlobalBasis.Z.Z).Normalized();
        Vector3 spot = PlayerBody.GlobalPosition + forward * StationDistance;
        spot.Y = GroundHeight(spot, PlayerBody.GlobalPosition.Y) + StationLift;

        // Station +Z points back at the carrier, so the camera looks into the pack from the carrier's side.
        Vector3 back = -forward;
        Basis basis = new(Vector3.Up.Cross(back), Vector3.Up, back);
        _station = new Node3D { Name = "PackStation", TopLevel = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(_station);
        _station.GlobalTransform = new Transform3D(basis, spot);

        _view = new PackView { Name = "PackView" };
        _station.AddChild(_view);
        _view.Build(Inventory.Backpack);

        _camera = new Camera3D { Name = "PackingCamera", Fov = CameraFov, Position = CameraOffset };
        _station.AddChild(_camera);
        _camera.LookAt(_station.ToGlobal(CameraTarget), Vector3.Up);
        _camera.MakeCurrent();

        Active = true;
        ShowHelp(true);
        _held = null;
        _view.Refresh(Inventory, _held);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameEvents.PublishPackingMode(true);
    }

    private void Exit()
    {
        _held = null;
        Active = false;
        ShowHelp(false);
        _station?.QueueFree();
        _station = null;
        _camera = null;
        _view = null;
        PlayerCamera?.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GameEvents.PublishPackingMode(false);
    }

    /// <summary>Packs everything again, heaviest first: for when Tetris isn't the mood.</summary>
    public void AutoArrange()
    {
        if (Inventory == null || _view == null)
        {
            return;
        }
        List<ItemDefinition> all = new();
        foreach (PackedItem packed in Inventory.Grid!.Items)
        {
            all.Add(packed.Definition);
        }
        all.AddRange(Inventory.LooseItems);
        if (_held != null)
        {
            all.Add(_held);
            _held = null;
        }
        all.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
        Inventory.Repack(all, 0.5f);
        _view.Refresh(Inventory, _held);
    }

    private void Click(bool behind = false)
    {
        if (Inventory == null || _view == null || _camera == null || _station == null)
        {
            return;
        }

        if (_held != null)
        {
            if (_ghostValid && Inventory.Pack(_held, _ghostOrigin, _heldOrientation) != null)
            {
                _held = null;
                _view.Refresh(Inventory, _held);
            }
            else if (!_ghostValid)
            {
                // Clicking away from the pack puts it down on the mat: that's how things come out.
                PutBack();
            }
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Transform3D toStation = _station.GlobalTransform.AffineInverse();
        Vector3 rayFrom = toStation * _camera.ProjectRayOrigin(mouse);
        Vector3 rayDirection = (toStation.Basis * _camera.ProjectRayNormal(mouse)).Normalized();
        object? target = _view.ItemUnder(rayFrom, rayDirection);
        if (behind)
        {
            target = _view.ItemBehind(rayFrom, rayDirection);
        }
        if (target is PackedItem packed)
        {
            Inventory.Unpack(packed);
            _held = packed.Definition;
            _heldOrientation = packed.Orientation;
        }
        else if (target is ItemDefinition loose)
        {
            _held = loose;
            _heldOrientation = GridOrientations.Identity;
        }
        _view.Refresh(Inventory, _held);
    }

    private void PutBack()
    {
        if (_held == null || Inventory == null || _view == null)
        {
            return;
        }
        _held = null;
        _view.Refresh(Inventory, _held);
    }

    /// <summary>Applies a quarter-turn step <paramref name="times"/> times, if the item's rule allows the result.</summary>
    private void Turn(int step, int times)
    {
        if (_held == null)
        {
            return;
        }
        int orientation = _heldOrientation;
        for (int i = 0; i < times; i++)
        {
            orientation = GridOrientations.Compose(step, orientation);
        }
        if (GridOrientations.Allows(_held.Orientation, orientation))
        {
            _heldOrientation = orientation;
        }
    }

    private float GroundHeight(Vector3 point, float fallback)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            point + Vector3.Up * GroundSearch, point + Vector3.Down * GroundSearch);
        if (PlayerBody != null)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { PlayerBody.GetRid() };
        }
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return fallback;
        }
        return hit["position"].AsVector3().Y;
    }
}
