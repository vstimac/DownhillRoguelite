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
    /// <summary>Degrees per second at full right-stick deflection.</summary>
    [Export] public float StickSpeed { get; set; } = 140.0f;
    [Export] public float FollowSharpness { get; set; } = 14.0f;

    [ExportGroup("Auto align")]
    /// <summary>Seconds without look input before the camera swings behind the direction of travel.</summary>
    [Export] public float AutoAlignDelay { get; set; } = 1.5f;
    [Export] public float AutoAlignSharpness { get; set; } = 1.5f;
    /// <summary>No auto align below this horizontal speed, in m/s.</summary>
    [Export] public float AutoAlignMinSpeed { get; set; } = 1.0f;

    private float _yawDegrees;
    private float _pitchDegrees;
    private float _timeSinceLook;

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
            Camera.Fov = FieldOfView;
            if (Camera.Current)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }

        ApplyRotation();
    }

    public override void _ExitTree()
    {
        if (Target is PlayerController controller)
        {
            controller.Teleported -= SnapBehindTarget;
        }
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
        GlobalPosition = PivotOf(Target);
        ApplyRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Camera == null || !Camera.Current)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yawDegrees -= motion.Relative.X * MouseSensitivity;
            _pitchDegrees -= motion.Relative.Y * MouseSensitivity;
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

    public override void _Process(double delta)
    {
        if (Target == null)
        {
            return;
        }

        float dt = (float)delta;
        _timeSinceLook += dt;

        if (Camera != null && Camera.Current)
        {
            Vector2 stick = Input.GetVector(ActionLookLeft, ActionLookRight, ActionLookUp, ActionLookDown);
            if (!stick.IsZeroApprox())
            {
                _yawDegrees -= stick.X * StickSpeed * dt;
                _pitchDegrees -= stick.Y * StickSpeed * dt;
                _timeSinceLook = 0.0f;
            }
        }

        AutoAlign(dt);

        float weight = 1.0f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(PivotOf(Target), weight);
        ApplyRotation();
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
        Rotation = new Vector3(Mathf.DegToRad(_pitchDegrees), Mathf.DegToRad(_yawDegrees), 0.0f);
    }

    private Vector3 PivotOf(Node3D target)
    {
        return target.GetGlobalTransformInterpolated().Origin + Vector3.Up * PivotHeight;
    }
}
