using System;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The front end's camera: it frames the real mountain behind the menu. Vista looks past the carrier
/// down the descent with a slow sideways drift; Portrait comes round to the carrier's face for the
/// character page; Game flies into the run's own camera, so Start is one continuous move. It knows
/// where the carrier is only from GameEvents (LocomotionUpdated), never from the player node.
/// </summary>
public partial class MenuCamera : Camera3D
{
    public enum Shot
    {
        Vista,
        Portrait,
        Game,
    }

    /// <summary>Camera position in the carrier's frame: x right, y up, z forward (downhill).</summary>
    [Export] public Vector3 VistaOffset { get; set; } = new(-3.5f, 3.0f, -7.5f);
    /// <summary>Where the vista looks, in the carrier's frame.</summary>
    [Export] public Vector3 VistaLook { get; set; } = new(-4.0f, -4.0f, 30.0f);
    [Export] public Vector3 PortraitOffset { get; set; } = new(0.4f, 1.45f, 3.7f);
    [Export] public Vector3 PortraitLook { get; set; } = new(-0.9f, 1.05f, 0.0f);
    [Export] public float VistaFov { get; set; } = 58.0f;
    [Export] public float PortraitFov { get; set; } = 42.0f;
    /// <summary>How far the vista swings either way, in degrees, and how long one swing takes.</summary>
    [Export] public float DriftDegrees { get; set; } = 9.0f;
    [Export] public float DriftSeconds { get; set; } = 50.0f;
    [Export] public float PortraitDriftDegrees { get; set; } = 4.0f;
    /// <summary>Seconds to move between shots, and to fly into the run's camera.</summary>
    [Export] public float BlendSeconds { get; set; } = 1.3f;
    [Export] public float GameBlendSeconds { get; set; } = 1.8f;

    /// <summary>The run's camera to hand over to at the end of the Game shot.</summary>
    public Camera3D? GameCamera { get; set; }
    public Shot CurrentShot { get; private set; } = Shot.Vista;
    public bool Blending => _blend < 1.0f;
    /// <summary>Raised once the Game shot is reached (the run's camera has been made current).</summary>
    public event Action? Arrived;

    private Vector3 _carrier;
    private Vector3 _forward = Vector3.Forward;
    private bool _haveCarrier;
    private Transform3D _from;
    private float _fromFov;
    private float _blend = 1.0f;
    private float _blendSeconds = 1.0f;
    private float _time;
    private bool _arrived;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        TopLevel = true;
        Fov = VistaFov;
        GameEvents.LocomotionUpdated += OnLocomotion;
        if (GameEvents.CarrierPosition != Vector3.Zero)
        {
            _carrier = GameEvents.CarrierPosition;
            _haveCarrier = true;
        }
        else if (GameCamera != null && GameCamera.IsInsideTree())
        {
            // Until the carrier reports in, stand where the run's camera stands.
            _carrier = GameCamera.GlobalPosition;
            Vector3 ahead = -GameCamera.GlobalBasis.Z;
            ahead.Y = 0.0f;
            if (ahead.LengthSquared() > 0.01f)
            {
                _forward = ahead.Normalized();
            }
        }
        GlobalTransform = Pose(CurrentShot);
        _from = GlobalTransform;
        _fromFov = Fov;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot.Position;
        if (snapshot.Forward.LengthSquared() > 0.01f)
        {
            _forward = snapshot.Forward.Normalized();
        }
        if (!_haveCarrier)
        {
            _haveCarrier = true;
            if (!Blending)
            {
                GlobalTransform = Pose(CurrentShot);
            }
        }
    }

    /// <summary>Moves to a shot (smoothly, unless <paramref name="instant"/>).</summary>
    public void GoTo(Shot shot, bool instant = false)
    {
        if (shot == CurrentShot && !Blending)
        {
            return;
        }
        _from = GlobalTransform;
        _fromFov = Fov;
        CurrentShot = shot;
        _blendSeconds = BlendSeconds;
        if (shot == Shot.Game)
        {
            _blendSeconds = GameBlendSeconds;
            _arrived = false;
        }
        _blend = 0.0f;
        if (instant)
        {
            _blend = 1.0f;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (_blend < 1.0f)
        {
            _blend = Mathf.Min(1.0f, _blend + dt / Mathf.Max(_blendSeconds, 0.01f));
        }
        float t = Mathf.SmoothStep(0.0f, 1.0f, _blend);
        // Orthonormalised so the run camera's (possibly scaled) rig can't upset the blend.
        Transform3D target = Pose(CurrentShot).Orthonormalized();
        if (_blend >= 1.0f)
        {
            GlobalTransform = target;
        }
        else
        {
            GlobalTransform = _from.Orthonormalized().InterpolateWith(target, t);
        }
        Fov = Mathf.Lerp(_fromFov, FovOf(CurrentShot), t);
        if (CurrentShot == Shot.Game && _blend >= 1.0f && !_arrived)
        {
            _arrived = true;
            if (GameCamera != null && GameCamera.IsInsideTree())
            {
                GameCamera.MakeCurrent();
            }
            Arrived?.Invoke();
        }
    }

    private float FovOf(Shot shot)
    {
        if (shot == Shot.Portrait)
        {
            return PortraitFov;
        }
        if (shot == Shot.Game && GameCamera != null)
        {
            return GameCamera.Fov;
        }
        return VistaFov;
    }

    /// <summary>The camera's transform for a shot right now (the vista drifts with time).</summary>
    public Transform3D Pose(Shot shot)
    {
        if (shot == Shot.Game && GameCamera != null && GameCamera.IsInsideTree())
        {
            return GameCamera.GlobalTransform;
        }
        Vector3 offset = VistaOffset;
        Vector3 look = VistaLook;
        float drift = DriftDegrees;
        if (shot == Shot.Portrait)
        {
            offset = PortraitOffset;
            look = PortraitLook;
            drift = PortraitDriftDegrees;
        }
        float swing = Mathf.DegToRad(drift) * Mathf.Sin(_time * Mathf.Tau / Mathf.Max(DriftSeconds, 1.0f));
        Vector3 forward = _forward.Rotated(Vector3.Up, swing);
        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        Vector3 position = _carrier + right * offset.X + Vector3.Up * offset.Y + forward * offset.Z;
        // A slow rise and fall, like breathing.
        position += Vector3.Up * 0.25f * Mathf.Sin(_time * Mathf.Tau / (DriftSeconds * 0.6f));
        Vector3 at = _carrier + right * look.X + Vector3.Up * look.Y + forward * look.Z;
        if (position.IsEqualApprox(at))
        {
            at += forward;
        }
        return new Transform3D(Basis.Identity, position).LookingAt(at, Vector3.Up);
    }
}
