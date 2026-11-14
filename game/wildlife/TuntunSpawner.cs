using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Alt+T brings Tuntun Sahur (TuntunSahur): he stands on the ground ten metres ahead of the carrier,
/// where the camera looks, and comes running. Pressing it again replaces him. The key is taken here
/// before the debug keys see it, so Alt+T doesn't also skip an hour (T).
/// </summary>
public partial class TuntunSpawner : Node3D
{
    [Export] public Key SpawnKey { get; set; } = Key.T;
    [Export] public bool RequireAlt { get; set; } = true;
    /// <summary>How far ahead of the carrier he appears, in m.</summary>
    [Export] public float SpawnDistance { get; set; } = 10.0f;
    /// <summary>The ground is looked for this far above and below the spot, in m.</summary>
    [Export] public float GroundSearch { get; set; } = 40.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public TuntunSahur? Current { get; private set; }

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
        if (GetTree().Paused || GameEvents.IsScreenOpen || GameEvents.IsConversing)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        Spawn();
    }

    /// <summary>Brings him (replacing the one already here); null if the carrier isn't known yet.</summary>
    public TuntunSahur? Spawn()
    {
        if (!GameEvents.MaySimulateWorld) { return null; }
        if (!_carrierKnown)
        {
            return null;
        }
        Vector3 ahead = Ahead();
        Vector3 spot = Ground(_carrier.Position + ahead * SpawnDistance);
        if (Current != null && IsInstanceValid(Current))
        {
            Current.QueueFree();
        }
        Current = new TuntunSahur { Name = "TuntunSahur" };
        AddChild(Current);
        Current.GlobalPosition = spot;
        // Facing the carrier from the start.
        Current.Rotation = new Vector3(0.0f, Mathf.Atan2(ahead.X, ahead.Z), 0.0f);
        Current.Track(_carrier.Position);
        return Current;
    }

    /// <summary>Where the camera looks, flattened; the carrier's facing without a camera.</summary>
    private Vector3 Ahead()
    {
        Vector3 ahead = _carrier.Forward;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            ahead = -camera.GlobalBasis.Z;
        }
        ahead.Y = 0.0f;
        if (ahead.LengthSquared() < 0.0001f)
        {
            ahead = Vector3.Forward;
        }
        return ahead.Normalized();
    }

    private Vector3 Ground(Vector3 spot)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            spot + Vector3.Up * GroundSearch, spot + Vector3.Down * GroundSearch, GroundMask);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            return hit["position"].AsVector3();
        }
        return spot;
    }
}
