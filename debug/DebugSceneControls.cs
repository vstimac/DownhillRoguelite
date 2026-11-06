using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Shared controls for debug scenes with a player: F1 swaps between the player camera and the
/// free-fly camera, R respawns at the player's starting point, T skips an hour, F4 cycles forced
/// weather (forecast, then each kind), and falling below KillHeight
/// respawns automatically.
/// </summary>
public partial class DebugSceneControls : Node
{
    private static readonly StringName ActionToggleFly = "debug_toggle_fly_camera";
    private static readonly StringName ActionRespawn = "debug_respawn";
    private static readonly StringName ActionSkipHour = "debug_skip_hour";
    private static readonly StringName ActionCycleWeather = "debug_cycle_weather";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public Camera3D? PlayerCamera { get; set; }
    [Export] public FreeFlyCamera? FlyCamera { get; set; }
    [Export] public WeatherSystem? Weather { get; set; }

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

        // Until runs have a real fail state, death sends the carrier back to the start.
        GameEvents.RespawnRequested += Respawn;

        // For rendered checks (--write-movie): `-- --autowalk` holds forward, `--autobrake` also brakes.
        string[] args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--autowalk") >= 0)
        {
            Input.ActionPress("player_move_forward");
        }
        if (System.Array.IndexOf(args, "--autobrake") >= 0)
        {
            Input.ActionPress("player_brake");
        }
        // `--weather=rain` (or fog, snow, wind, heat, clear) forces a front; `--hour=22` sets the clock.
        foreach (string arg in args)
        {
            if (arg.StartsWith("--hour=") && float.TryParse(arg.Substring("--hour=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hour))
            {
                GetNodeOrNull<WorldClock>("/root/WorldClock")?.SetTimeOfDay(hour);
            }
            if (Weather != null && arg.StartsWith("--weather=") && System.Enum.TryParse(arg.Substring("--weather=".Length), true, out WeatherKind kind))
            {
                Weather.Forced = kind;
            }
        }
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= Respawn;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Packing uses some of the same keys (R tips an item).
        if (GameEvents.IsPacking)
        {
            return;
        }

        if (@event.IsActionPressed(ActionToggleFly))
        {
            ToggleFlyCamera();
        }
        else if (@event.IsActionPressed(ActionRespawn))
        {
            Respawn();
        }
        else if (@event.IsActionPressed(ActionSkipHour))
        {
            GetNodeOrNull<WorldClock>("/root/WorldClock")?.AdvanceHours(1.0f);
        }
        else if (@event.IsActionPressed(ActionCycleWeather))
        {
            Weather?.CycleForced();
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
