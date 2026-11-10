using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A loose prop (a crate, a log seat, a stump, a small lantern) that is a real rigid body: walk into
/// it and it slides and tips (BodyPush), the hand cart knocks it about. It waits frozen until the
/// ground under it has streamed in, so it never falls through a mountain that isn't there yet, and
/// if it ever ends up far below where it was put (through a crack, down a cliff) it is put back.
/// </summary>
public partial class LooseProp : RigidBody3D
{
    [Export(PropertyHint.Layers3DPhysics)] public uint PropLayer { get; set; } = 16;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    /// <summary>How far below its origin the ground may be for it to wake (m).</summary>
    [Export] public float GroundProbe { get; set; } = 2.0f;
    /// <summary>Put back when it is this far below where it was placed (m).</summary>
    [Export] public float ResetDrop { get; set; } = 12.0f;
    [Export] public float Friction { get; set; } = 0.9f;
    [Export] public float Bounce { get; set; } = 0.05f;

    public bool Waiting => Freeze;

    private Transform3D _home;
    private bool _haveHome;
    private PhysicsRayQueryParameters3D? _probe;

    /// <summary>Gives it a box body the size of its model (fitted size in m, standing on its origin).</summary>
    public void SetBox(Vector3 size, float footprint)
    {
        Vector3 box = new(Mathf.Max(size.X * footprint, 0.05f), Mathf.Max(size.Y, 0.05f), Mathf.Max(size.Z * footprint, 0.05f));
        AddChild(new CollisionShape3D { Name = "Box", Shape = new BoxShape3D { Size = box }, Position = new Vector3(0.0f, box.Y * 0.5f, 0.0f) });
    }

    public override void _Ready()
    {
        CollisionLayer = PropLayer;
        CollisionMask = GroundMask | PropLayer;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = Friction, Bounce = Bounce };
        LinearDamp = 0.3f;
        AngularDamp = 1.0f;
        CanSleep = true;
        FreezeMode = FreezeModeEnum.Static;
        Freeze = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_haveHome)
        {
            _home = GlobalTransform;
            _haveHome = true;
        }
        if (Freeze)
        {
            Vector3 at = GlobalPosition;
            _probe ??= new PhysicsRayQueryParameters3D { CollisionMask = GroundMask };
            _probe.From = at + Vector3.Up * 1.0f;
            _probe.To = at + Vector3.Down * GroundProbe;
            if (GetWorld3D().DirectSpaceState.IntersectRay(_probe).Count > 0)
            {
                Freeze = false;
            }
            return;
        }
        if (GlobalPosition.Y < _home.Origin.Y - ResetDrop)
        {
            Freeze = true;
            GlobalTransform = _home;
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
        }
    }
}
