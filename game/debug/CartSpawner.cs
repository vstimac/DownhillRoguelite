using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Alt+C sets a hand cart (HandCart) down on the ground a few metres ahead of the carrier, its
/// handles toward them, for trying the cart without walking to a station. Pressing it again moves
/// the cart there (letting go of it first). The key is taken here before anything else sees C.
/// </summary>
public partial class CartSpawner : Node3D
{
    [Export] public Key SpawnKey { get; set; } = Key.C;
    [Export] public bool RequireAlt { get; set; } = true;
    /// <summary>How far ahead of the carrier the cart appears, in m.</summary>
    [Export] public float SpawnDistance { get; set; } = 4.0f;
    /// <summary>The cart is set this high above the ground and dropped onto it, in m.</summary>
    [Export] public float DropHeight { get; set; } = 0.8f;
    /// <summary>The ground is looked for this far above and below the spot, in m.</summary>
    [Export] public float GroundSearch { get; set; } = 40.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public HandCart? Current { get; private set; }

    private LocomotionSnapshot _carrier;
    private bool _carrierKnown;

    public override void _Ready()
    {
        GameEvents.LocomotionUpdated += OnLocomotion;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot;
        _carrierKnown = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != SpawnKey)
        {
            return;
        }
        if (RequireAlt && !key.AltPressed)
        {
            return;
        }
        if (GetTree().Paused || (GameEvents.IsScreenOpen && !Ui.UiLayoutEditor.OnlyAltEditing) || GameEvents.IsConversing)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        Spawn();
    }

    /// <summary>Sets the cart down ahead (moving the one already here); null if the carrier isn't known yet.</summary>
    public HandCart? Spawn()
    {
        if (!_carrierKnown)
        {
            return null;
        }
        Vector3 ahead = _carrier.Forward;
        ahead.Y = 0.0f;
        if (ahead.LengthSquared() < 0.0001f)
        {
            ahead = Vector3.Forward;
        }
        ahead = ahead.Normalized();
        Vector3 spot = _carrier.Position + ahead * SpawnDistance;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(spot + Vector3.Up * GroundSearch, spot + Vector3.Down * GroundSearch, GroundMask));
        if (hit.Count > 0)
        {
            spot = hit["position"].AsVector3();
        }
        if (Current != null && IsInstanceValid(Current))
        {
            Current.QueueFree();
        }
        Current = new HandCart { Name = "HandCart" };
        AddChild(Current);
        // Handles (-Z) toward the carrier.
        Current.GlobalTransform = new Transform3D(Basis.LookingAt(-ahead, Vector3.Up), spot + Vector3.Up * (Current.WheelRadius + DropHeight));
        return Current;
    }
}
