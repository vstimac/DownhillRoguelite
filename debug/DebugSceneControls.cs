using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Shared controls for debug scenes with a player: F1 swaps between the player camera and the
/// free-fly camera, R respawns at the player's starting point, and falling below KillHeight
/// respawns automatically.
/// </summary>
public partial class DebugSceneControls : Node
{
    private static readonly StringName ActionToggleFly = "debug_toggle_fly_camera";
    private static readonly StringName ActionRespawn = "debug_respawn";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public Camera3D? PlayerCamera { get; set; }
    [Export] public FreeFlyCamera? FlyCamera { get; set; }

    /// <summary>Respawn when the player falls below this height, in m.</summary>
    [Export] public float KillHeight { get; set; } = -30.0f;

    private Vector3 _spawnPosition;
    private float _spawnYaw;

    public override void _Ready()
    {
        if (PlayerBody != null)
        {
            _spawnPosition = PlayerBody.GlobalPosition;
            _spawnYaw = PlayerBody.GlobalRotation.Y;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionToggleFly))
        {
            ToggleFlyCamera();
        }
        else if (@event.IsActionPressed(ActionRespawn))
        {
            Respawn();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (PlayerBody != null && PlayerBody.GlobalPosition.Y < KillHeight)
        {
            Respawn();
        }
    }

    private void Respawn()
    {
        PlayerBody?.Teleport(_spawnPosition, _spawnYaw);
    }

    private void ToggleFlyCamera()
    {
        if (PlayerBody == null || PlayerCamera == null || FlyCamera == null)
        {
            return;
        }

        if (FlyCamera.Current)
        {
            PlayerCamera.MakeCurrent();
            PlayerBody.InputEnabled = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else
        {
            FlyCamera.GlobalTransform = PlayerCamera.GlobalTransform;
            FlyCamera.ResetLook();
            FlyCamera.MakeCurrent();
            PlayerBody.InputEnabled = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }
}
