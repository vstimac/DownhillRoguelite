using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The pack's swing on the carrier's back: a damped spring that tips it about its top edge and lets
/// it settle a little. It is driven by how the pack's mount accelerates, measured in the pack's own
/// frame and smoothed (never by raw speed), so starting, stopping, turning, jumps and landings
/// move it while a steady stride doesn't push it away. The swing is bounded and always springs
/// back; it never pushes the pack into the back. Visual only: balance and load are calculated
/// elsewhere. BackpackAttachment adds it; it works the same for remote carriers in co-op.
/// </summary>
public partial class BackpackMotion : Node
{
    /// <summary>The spring's pull back to rest (1/s²).</summary>
    [Export] public float Stiffness { get; set; } = 60.0f;
    /// <summary>The spring's damping (1/s).</summary>
    [Export] public float Damping { get; set; } = 9.0f;
    /// <summary>Degrees of tip per m/s² of acceleration.</summary>
    [Export] public float TiltPerAcceleration { get; set; } = 0.7f;
    [Export] public float MaxTiltDegrees { get; set; } = 7.0f;
    /// <summary>The share of MaxTiltDegrees the pack may tip toward the back (it rests against it).</summary>
    [Export] public float InwardShare { get; set; } = 0.15f;
    /// <summary>Metres the pack sinks per m/s² of upward acceleration (a landing).</summary>
    [Export] public float DropPerAcceleration { get; set; } = 0.0012f;
    [Export] public float MaxDrop { get; set; } = 0.03f;
    /// <summary>How quickly the measured acceleration is followed (1/s); lower is smoother.</summary>
    [Export] public float AccelerationFilter { get; set; } = 8.0f;
    [Export] public float MaxAcceleration { get; set; } = 30.0f;
    /// <summary>A mount moving faster than this (m/s) was moved (a teleport): the swing starts over.</summary>
    [Export] public float TeleportSpeed { get; set; } = 40.0f;

    /// <summary>The current tip about the pack's side axis and its depth axis, in degrees (tests).</summary>
    public Vector2 TiltDegrees => new(Mathf.RadToDeg(_angle.X), Mathf.RadToDeg(_angle.Y));
    /// <summary>How far the pack has sunk, in m (tests).</summary>
    public float Drop => _drop;

    private BackpackAttachment? _pack;
    private Vector3 _hinge;
    private float _awaySign = 1.0f;
    private bool _tracking;
    private Vector3 _lastPosition;
    private Vector3 _lastVelocity;
    private Vector3 _acceleration;
    private Vector2 _angle;
    private Vector2 _angleSpeed;
    private float _drop;
    private float _dropSpeed;

    public override void _Ready()
    {
        _pack = GetParent() as BackpackAttachment;
        if (_pack == null || _pack.GetParent() is not Skeleton3D rig)
        {
            return;
        }
        Aabb box = _pack.PackBounds();
        _hinge = new Vector3(box.GetCenter().X, box.End.Y, box.GetCenter().Z);
        // Which way the pack's own depth axis points: away from the back, or into it.
        if (ClothingRig.Frame(rig, out Basis frame, out _))
        {
            Vector3 depth = rig.GlobalBasis * (_pack.RestPivot(rig).Basis * Vector3.Back);
            Vector3 back = rig.GlobalBasis * frame.Z;
            if (depth.Dot(back) < 0.0f)
            {
                _awaySign = -1.0f;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_pack == null || !IsInstanceValid(_pack) || !_pack.IsInsideTree())
        {
            return;
        }
        float dt = Mathf.Clamp((float)delta, 0.001f, 0.05f);
        Vector3 position = _pack.GlobalPosition;
        if (!_tracking)
        {
            Restart(position);
            return;
        }
        Vector3 velocity = (position - _lastPosition) / dt;
        if (!velocity.IsFinite() || velocity.Length() > TeleportSpeed)
        {
            Restart(position);
            Settle();
            return;
        }
        Vector3 raw = ((velocity - _lastVelocity) / dt).LimitLength(MaxAcceleration);
        _lastPosition = position;
        _lastVelocity = velocity;
        _acceleration = _acceleration.Lerp(raw, 1.0f - Mathf.Exp(-AccelerationFilter * dt));

        Basis pack = _pack.Pivot.GlobalBasis.Orthonormalized();
        float away = _acceleration.Dot(pack.Z) * _awaySign;
        float side = _acceleration.Dot(pack.X);
        float up = _acceleration.Dot(pack.Y);
        float most = Mathf.DegToRad(MaxTiltDegrees);
        float per = Mathf.DegToRad(TiltPerAcceleration);
        // The bottom lags behind: speeding up forward swings it out from the back, never far into it.
        float pitch = Mathf.Clamp(per * away * _awaySign, -most, most);
        if (pitch * _awaySign > most * InwardShare)
        {
            pitch = most * InwardShare * _awaySign;
        }
        float roll = Mathf.Clamp(-per * side, -most, most);
        float sink = Mathf.Clamp(-DropPerAcceleration * up, -MaxDrop, MaxDrop * 0.4f);

        Vector2 target = new(pitch, roll);
        _angleSpeed += ((target - _angle) * Stiffness - _angleSpeed * Damping) * dt;
        _angle += _angleSpeed * dt;
        _angle = _angle.Clamp(new Vector2(-most, -most), new Vector2(most, most));
        _dropSpeed += ((sink - _drop) * Stiffness - _dropSpeed * Damping) * dt;
        _drop = Mathf.Clamp(_drop + _dropSpeed * dt, -MaxDrop, MaxDrop);
        Apply();
    }

    private void Restart(Vector3 position)
    {
        _tracking = true;
        _lastPosition = position;
        _lastVelocity = Vector3.Zero;
        _acceleration = Vector3.Zero;
    }

    private void Settle()
    {
        _angle = Vector2.Zero;
        _angleSpeed = Vector2.Zero;
        _drop = 0.0f;
        _dropSpeed = 0.0f;
        _pack?.SetSway(Transform3D.Identity);
    }

    private void Apply()
    {
        if (_pack == null)
        {
            return;
        }
        // At rest the fit is exactly the fit (no leftover rounding).
        if (_angle.Length() < 0.0001f && _angleSpeed.Length() < 0.001f && Mathf.Abs(_drop) < 0.00001f && Mathf.Abs(_dropSpeed) < 0.0001f)
        {
            Settle();
            return;
        }
        Basis turn = Basis.FromEuler(new Vector3(_angle.X, 0.0f, _angle.Y));
        Vector3 hinge = _hinge + Vector3.Up * _drop;
        _pack.SetSway(new Transform3D(turn, hinge - turn * _hinge));
    }
}
