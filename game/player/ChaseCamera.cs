using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Third-person chase camera rig. Follows the target's interpolated position, orbits with the
/// mouse or right stick, keeps out of terrain through a SpringArm3D, and drifts back behind the
/// direction of travel when the player stops steering it. Framing values are exports, so a close
/// over-the-shoulder view is a settings change.
/// </summary>
public partial class ChaseCamera : Node3D
{
    private static readonly StringName ActionLookLeft = "camera_look_left";
    private static readonly StringName ActionLookRight = "camera_look_right";
    private static readonly StringName ActionLookUp = "camera_look_up";
    private static readonly StringName ActionLookDown = "camera_look_down";
    private static readonly StringName ActionReleaseMouse = "ui_cancel";

    [Export] public CharacterBody3D? Target { get; set; }
    [Export] public SpringArm3D? Arm { get; set; }
    [Export] public Camera3D? Camera { get; set; }

    [ExportGroup("Framing")]
    /// <summary>Height of the orbit pivot above the target's origin (the feet), in m.</summary>
    [Export] public float PivotHeight { get; set; } = 1.6f;
    [Export] public float Distance { get; set; } = 6.0f;
    /// <summary>Sideways camera offset; around 0.6 with a short distance gives an over-the-shoulder view.</summary>
    [Export] public float ShoulderOffset { get; set; } = 0.0f;
    [Export] public float FieldOfView { get; set; } = 70.0f;
    [Export] public float DefaultPitch { get; set; } = -22.0f;
    [Export] public float MinPitch { get; set; } = -70.0f;
    [Export] public float MaxPitch { get; set; } = 20.0f;
    /// <summary>Gap kept between the camera and terrain, in m.</summary>
    [Export] public float CollisionMargin { get; set; } = 0.2f;

    [ExportGroup("Control")]
    /// <summary>Degrees per pixel of mouse movement.</summary>
    [Export] public float MouseSensitivity { get; set; } = 0.15f;
    /// <summary>Mouse acceleration: the mouse speed (counts per second) at which full acceleration doubles the turn.</summary>
    [Export] public float AccelerationReferenceSpeed { get; set; } = 1500.0f;
    /// <summary>Most extra gain mouse acceleration can add (on top of 1).</summary>
    [Export] public float AccelerationMaxBoost { get; set; } = 3.0f;
    /// <summary>Mouse smoothing's time constant, in seconds.</summary>
    [Export] public float SmoothingSeconds { get; set; } = 0.035f;
    /// <summary>Degrees per second at full right-stick deflection.</summary>
    [Export] public float StickSpeed { get; set; } = 140.0f;
    [Export] public float FollowSharpness { get; set; } = 14.0f;

    [ExportGroup("Auto align")]
    /// <summary>Seconds without look input before the camera swings behind the direction of travel.</summary>
    [Export] public float AutoAlignDelay { get; set; } = 1.5f;
    [Export] public float AutoAlignSharpness { get; set; } = 1.5f;
    /// <summary>No auto align below this horizontal speed, in m/s.</summary>
    [Export] public float AutoAlignMinSpeed { get; set; } = 1.0f;

    [ExportGroup("Lock on")]
    /// <summary>With an enemy locked on (Tab): how quickly the view swings to it, the aim's height on it (m) and the pitch it settles at.</summary>
    [Export] public float LockSharpness { get; set; } = 7.0f;
    [Export] public float LockAimHeight { get; set; } = 1.0f;
    [Export] public float LockPitch { get; set; } = -14.0f;

    [ExportGroup("Pack")]
    /// <summary>
    /// While the pack is off the back (GameEvents.IsPacking) the view comes down over the carrier's
    /// shoulder onto the pack on the ground: a lower pivot between carrier and pack, a shorter arm, a
    /// steeper look, turned this many degrees off straight behind, and the picture shifted sideways
    /// (m of lens shift) so carrier and pack sit left of the pack page docked at the right.
    /// </summary>
    [Export] public float PackPivotHeight { get; set; } = 0.6f;
    [Export] public float PackDistance { get; set; } = 3.1f;
    [Export] public float PackPitch { get; set; } = -40.0f;
    [Export] public float PackYawOffset { get; set; } = -58.0f;
    [Export] public float PackLensShift { get; set; } = 1.7f;
    /// <summary>How quickly the view goes to the pack and back (per s).</summary>
    [Export] public float PackSharpness { get; set; } = 4.5f;

    /// <summary>0 in the usual view, 1 framing the pack on the ground.</summary>
    public float PackFrame { get; private set; }
    /// <summary>0 to 1: the picture shifted left of the open pack page (only while it is open).</summary>
    private float _lensFrame;

    private float _yawDegrees;
    private float _pitchDegrees;
    private bool _armForPack;
    private float _timeSinceLook;
    private readonly MouseLookFilter _mouse = new();
    /// <summary>A shake (GameEvents.RequestCameraShake) dies away at this rate (per s) and jitters this fast (Hz).</summary>
    [Export] public float ShakeDecay { get; set; } = 6.0f;
    [Export] public float ShakeFrequency { get; set; } = 28.0f;
    /// <summary>The strongest jolt allowed, in m.</summary>
    [Export] public float MaxShake { get; set; } = 0.25f;
    private float _shake;
    private float _shakeTime;
    private bool _desktopRead;

    public override void _Ready()
    {
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _pitchDegrees = DefaultPitch;

        SnapBehindTarget();
        if (Target is PlayerController controller)
        {
            controller.Teleported += SnapBehindTarget;
        }

        if (Arm != null)
        {
            Arm.SpringLength = Distance;
            Arm.Margin = CollisionMargin;
            Arm.Position = new Vector3(ShoulderOffset, 0.0f, 0.0f);
            if (Target != null)
            {
                Arm.AddExcludedObject(Target.GetRid());
            }
        }

        if (Camera != null)
        {
            // The player's field of view (Options) wins over the scene's default.
            GameOptions.Load();
            Camera.Fov = GameOptions.FieldOfView;
            GameOptions.Changed += ApplyFieldOfView;
            GameOptions.Changed += ReadDesktopPointer;
            ReadDesktopPointer();
            if (Camera.Current)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }

        ApplyRotation();
        GameEvents.CameraShakeRequested += OnShake;
    }

    private void OnShake(float strength)
    {
        _shake = Mathf.Min(MaxShake, Mathf.Max(_shake, strength));
    }

    /// <summary>Jolts the lens sideways and up (the camera's offsets), so aim and collision are untouched.</summary>
    private void UpdateShake(float dt)
    {
        if (Camera == null)
        {
            return;
        }
        float lens = PackLensShift * _lensFrame;
        if (_shake <= 0.0005f)
        {
            if (_shake > 0.0f || !Mathf.IsEqualApprox(Camera.HOffset, lens))
            {
                _shake = 0.0f;
                Camera.HOffset = lens;
                Camera.VOffset = 0.0f;
            }
            return;
        }
        _shakeTime += dt;
        float phase = _shakeTime * ShakeFrequency * Mathf.Tau;
        Camera.HOffset = lens + Mathf.Sin(phase) * _shake;
        Camera.VOffset = Mathf.Sin(phase * 1.37f + 1.1f) * _shake * 0.7f;
        _shake = Mathf.MoveToward(_shake, 0.0f, _shake * ShakeDecay * dt + 0.001f);
    }

    public override void _ExitTree()
    {
        GameEvents.CameraShakeRequested -= OnShake;
        GameOptions.Changed -= ApplyFieldOfView;
        GameOptions.Changed -= ReadDesktopPointer;
        if (Target is PlayerController controller)
        {
            controller.Teleported -= SnapBehindTarget;
        }
    }

    /// <summary>Debug portraits (--portrait): a close camera turned round by <paramref name="turnDegrees"/> (180 = from the front).</summary>
    public void Portrait(float distance, float turnDegrees)
    {
        if (Arm != null)
        {
            Arm.SpringLength = distance;
        }
        _yawDegrees += turnDegrees;
        _pitchDegrees = -6.0f;
        PivotHeight = 1.2f;
        ApplyRotation();
    }

    /// <summary>Puts the camera straight behind the target at the default pitch, e.g. after a respawn.</summary>
    private void SnapBehindTarget()
    {
        if (Target == null)
        {
            return;
        }
        _yawDegrees = Mathf.RadToDeg(Target.GlobalRotation.Y);
        _pitchDegrees = DefaultPitch;
        _timeSinceLook = 0.0f;
        _mouse.Clear();
        GlobalPosition = PivotOf(Target);
        ApplyRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The held map uses the mouse while it is up.
        if (Camera == null || !Camera.Current || GameEvents.IsMapOpen || GameEvents.IsScreenOpen)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Gathered here, turned in _Process once a frame (acceleration and smoothing need the frame's time).
            _mouse.Add(motion.Relative);
            _timeSinceLook = 0.0f;
        }
        else if (@event.IsActionPressed(ActionReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventMouseButton button && button.Pressed && button.ButtonIndex == MouseButton.Left)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>With Raw input off the camera follows the desktop's pointer settings: read them when it turns off.</summary>
    private void ReadDesktopPointer()
    {
        if (!GameOptions.RawInput && !_desktopRead)
        {
            DesktopPointer.Refresh();
        }
        _desktopRead = !GameOptions.RawInput;
    }

    /// <summary>-1 when Options → Controls inverts the look.</summary>
    private static float LookPitchSign()
    {
        if (GameOptions.InvertLook)
        {
            return -1.0f;
        }
        return 1.0f;
    }

    public override void _Process(double delta)
    {
        if (Target == null)
        {
            return;
        }

        float dt = (float)delta;
        _timeSinceLook += dt;
        float packWant = GameEvents.IsPacking ? 1.0f : 0.0f;
        PackFrame = Mathf.Lerp(PackFrame, packWant, 1.0f - Mathf.Exp(-PackSharpness * dt));
        if (Mathf.Abs(PackFrame - packWant) < 0.002f)
        {
            PackFrame = packWant;
        }
        float lensWant = GameEvents.IsPackOpen ? 1.0f : 0.0f;
        _lensFrame = Mathf.Abs(_lensFrame - lensWant) < 0.002f ? lensWant : Mathf.Lerp(_lensFrame, lensWant, 1.0f - Mathf.Exp(-PackSharpness * dt));
        FramePackArm();
        UpdateShake(dt);

        if (Camera != null && Camera.Current)
        {
            _mouse.AccelerationReferenceSpeed = AccelerationReferenceSpeed;
            _mouse.AccelerationMaxBoost = AccelerationMaxBoost;
            _mouse.SmoothingSeconds = SmoothingSeconds;
            Vector2 look = _mouse.Take(dt, GameOptions.MouseSensitivity, GameOptions.MouseAcceleration, GameOptions.MouseSmoothing, GameOptions.RawInput);
            if (!look.IsZeroApprox())
            {
                _yawDegrees -= look.X * MouseSensitivity;
                _pitchDegrees -= look.Y * MouseSensitivity * LookPitchSign();
            }
            Vector2 stick = Input.GetVector(ActionLookLeft, ActionLookRight, ActionLookUp, ActionLookDown);
            if (!stick.IsZeroApprox())
            {
                _yawDegrees -= stick.X * StickSpeed * GameOptions.LookSensitivity * dt;
                _pitchDegrees -= stick.Y * StickSpeed * GameOptions.LookSensitivity * LookPitchSign() * dt;
                _timeSinceLook = 0.0f;
            }
        }

        if (!FollowLock(dt))
        {
            AutoAlign(dt);
        }

        float weight = 1.0f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(PivotOf(Target), weight);
        ApplyRotation();
    }

    /// <summary>Keeps the locked enemy (GameEvents.LockTarget) in view, beyond the carrier; false with no lock.</summary>
    private bool FollowLock(float dt)
    {
        Node3D? locked = GameEvents.LockTarget;
        if (Target == null || locked == null || !IsInstanceValid(locked))
        {
            return false;
        }
        Vector3 pivot = PivotOf(Target);
        Vector3 aim = locked.GlobalPosition + Vector3.Up * LockAimHeight;
        Vector3 to = aim - pivot;
        float flat = new Vector2(to.X, to.Z).Length();
        if (flat < 0.3f)
        {
            return true;
        }
        float weight = 1.0f - Mathf.Exp(-LockSharpness * dt);
        float yaw = Mathf.LerpAngle(Mathf.DegToRad(_yawDegrees), Mathf.Atan2(-to.X, -to.Z), weight);
        _yawDegrees = Mathf.RadToDeg(yaw);
        float pitch = LockPitch + Mathf.RadToDeg(Mathf.Atan2(to.Y, flat));
        _pitchDegrees = Mathf.Lerp(_pitchDegrees, pitch, weight);
        _timeSinceLook = 0.0f;
        return true;
    }

    private void AutoAlign(float dt)
    {
        if (Target == null || _timeSinceLook < AutoAlignDelay)
        {
            return;
        }

        Vector3 velocity = Target.Velocity;
        Vector2 horizontal = new(velocity.X, velocity.Z);
        if (horizontal.Length() < AutoAlignMinSpeed)
        {
            return;
        }

        float targetYaw = Mathf.Atan2(-velocity.X, -velocity.Z);
        float weight = 1.0f - Mathf.Exp(-AutoAlignSharpness * dt);
        float yaw = Mathf.LerpAngle(Mathf.DegToRad(_yawDegrees), targetYaw, weight);
        _yawDegrees = Mathf.RadToDeg(yaw);
    }

    private void ApplyRotation()
    {
        _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
        float yaw = Mathf.DegToRad(_yawDegrees);
        float pitch = Mathf.DegToRad(_pitchDegrees);
        if (PackFrame > 0.0f && Target != null)
        {
            // Blended over the player's own view, which is kept: when the pack is back on, the view returns to it.
            float packYaw = Target.GlobalRotation.Y + Mathf.DegToRad(PackYawOffset);
            yaw = Mathf.LerpAngle(yaw, packYaw, PackFrame);
            pitch = Mathf.Lerp(pitch, Mathf.DegToRad(PackPitch), PackFrame);
        }
        Rotation = new Vector3(pitch, yaw, 0.0f);
    }

    /// <summary>The arm shortens while the pack is framed, and gets its own length back after.</summary>
    private void FramePackArm()
    {
        if (Arm == null)
        {
            return;
        }
        if (PackFrame > 0.0f)
        {
            Arm.SpringLength = Mathf.Lerp(Distance, PackDistance, PackFrame);
            _armForPack = true;
        }
        else if (_armForPack)
        {
            Arm.SpringLength = Distance;
            _armForPack = false;
        }
    }

    private Vector3 PivotOf(Node3D target)
    {
        Vector3 origin = target.GetGlobalTransformInterpolated().Origin;
        Vector3 pivot = origin + Vector3.Up * PivotHeight;
        if (PackFrame <= 0.0f)
        {
            return pivot;
        }
        // Between the carrier and the pack standing in front of the feet, a little to the left (PackSetDown.Side).
        Basis basis = target.GlobalBasis;
        Vector3 forward = new Vector3(-basis.Z.X, 0.0f, -basis.Z.Z).Normalized();
        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        Vector3 pack = origin + forward * 0.3f - right * 0.18f + Vector3.Up * PackPivotHeight;
        return pivot.Lerp(pack, PackFrame);
    }

    private void ApplyFieldOfView()
    {
        if (Camera != null && IsInstanceValid(Camera))
        {
            Camera.Fov = GameOptions.FieldOfView;
        }
    }
}
