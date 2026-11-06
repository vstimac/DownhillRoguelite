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
    private Vector3I _ghostOrigin;
    private bool _ghostValid;

    public override async void _Ready()
    {
        // For rendered checks (--write-movie): `-- --open-pack` sets the pack down once the carrier has landed.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--open-pack") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            TryEnter();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
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

        if (@event.IsActionPressed(ActionPlace))
        {
            Click();
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
        int rest = Inventory.Grid.DropHeight(size, column.X, column.Y);
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
        _held = null;
        _view.Refresh(Inventory, _held);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameEvents.PublishPackingMode(true);
    }

    private void Exit()
    {
        _held = null;
        Active = false;
        _station?.QueueFree();
        _station = null;
        _camera = null;
        _view = null;
        PlayerCamera?.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GameEvents.PublishPackingMode(false);
    }

    private void Click()
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
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Transform3D toStation = _station.GlobalTransform.AffineInverse();
        object? target = _view.ItemUnder(toStation * _camera.ProjectRayOrigin(mouse), (toStation.Basis * _camera.ProjectRayNormal(mouse)).Normalized());
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
