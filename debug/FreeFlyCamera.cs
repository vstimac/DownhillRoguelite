using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug free-fly camera. WASD to move, Q/E down/up, Shift to go faster.
/// Hold right mouse to capture the cursor and look around.
/// </summary>
public partial class FreeFlyCamera : Camera3D
{
    private static readonly StringName ActionForward = "debug_fly_forward";
    private static readonly StringName ActionBack = "debug_fly_back";
    private static readonly StringName ActionLeft = "debug_fly_left";
    private static readonly StringName ActionRight = "debug_fly_right";
    private static readonly StringName ActionUp = "debug_fly_up";
    private static readonly StringName ActionDown = "debug_fly_down";
    private static readonly StringName ActionFast = "debug_fly_fast";
    private static readonly StringName ActionLook = "debug_fly_look";

    /// <summary>Movement speed in metres per second.</summary>
    [Export] public float MoveSpeed { get; set; } = 10.0f;

    /// <summary>Speed multiplier while the fast key is held.</summary>
    [Export] public float FastMultiplier { get; set; } = 4.0f;

    /// <summary>Mouse look sensitivity in degrees per pixel.</summary>
    [Export] public float MouseSensitivity { get; set; } = 0.15f;

    /// <summary>Maximum look angle up or down, in degrees.</summary>
    [Export] public float MaxPitch { get; set; } = 89.0f;

    private float _yawDegrees;
    private float _pitchDegrees;

    public override void _Ready()
    {
        _pitchDegrees = RotationDegrees.X;
        _yawDegrees = RotationDegrees.Y;
    }

    public override void _Process(double delta)
    {
        UpdateMouseCapture();
        Move((float)delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            _yawDegrees -= motion.Relative.X * MouseSensitivity;
            _pitchDegrees -= motion.Relative.Y * MouseSensitivity;
            _pitchDegrees = Mathf.Clamp(_pitchDegrees, -MaxPitch, MaxPitch);
            RotationDegrees = new Vector3(_pitchDegrees, _yawDegrees, 0.0f);
        }
    }

    private static void UpdateMouseCapture()
    {
        bool wantsCapture = Input.IsActionPressed(ActionLook);
        bool isCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (wantsCapture && !isCaptured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (!wantsCapture && isCaptured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void Move(float delta)
    {
        // Negative Y is forward, so it maps onto the camera's local -Z.
        Vector2 planar = Input.GetVector(ActionLeft, ActionRight, ActionForward, ActionBack);
        float vertical = Input.GetAxis(ActionDown, ActionUp);

        Vector3 direction = GlobalBasis * new Vector3(planar.X, 0.0f, planar.Y);
        direction += Vector3.Up * vertical;
        if (direction.LengthSquared() > 1.0f)
        {
            direction = direction.Normalized();
        }

        float speed = MoveSpeed;
        if (Input.IsActionPressed(ActionFast))
        {
            speed *= FastMultiplier;
        }

        GlobalPosition += direction * speed * delta;
    }
}
