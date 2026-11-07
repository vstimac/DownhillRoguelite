using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
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
    /// <summary>When set, a new run starts at this streamer's spawn point (the new mountain's summit).</summary>
    [Export] public TerrainStreamer? Streamer { get; set; }

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

        // A new run (RespawnRequested) puts the carrier on the summit of the new mountain.
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
        // `--promo` hides every overlay and the bars, for clean screenshots.
        if (System.Array.IndexOf(args, "--promo") >= 0)
        {
            DebugDraw3D.SetEnabled(false);
            DebugDraw3D.HideHud = true;
        }
        // `--showcase` hides the debug text but keeps the player's HUD (screenshots that show the game).
        if (System.Array.IndexOf(args, "--showcase") >= 0)
        {
            DebugDraw3D.SetEnabled(false);
        }
        // `--herd=Horse` (or `--herd=Sheep:6`) brings a group of animals close, for screenshots.
        // `--walk-seconds=3` walks forward that long, then stops.
        foreach (string arg in args)
        {
            if (arg.StartsWith("--herd="))
            {
                string spec = arg.Substring("--herd=".Length);
                GetTree().CreateTimer(1.5).Timeout += () => SpawnHerd(spec);
            }
            if (arg.StartsWith("--walk-seconds=") && float.TryParse(arg.Substring("--walk-seconds=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                Input.ActionPress("player_move_forward");
                GetTree().CreateTimer(seconds).Timeout += () => Input.ActionRelease("player_move_forward");
            }
        }
        // `--map` raises the map at start (render checks).
        if (System.Array.IndexOf(args, "--map") >= 0)
        {
            CallDeferred(MethodName.OpenMap);
        }
        // `--at-station=2` starts the carrier at station 2 (render checks, trying boons).
        foreach (string arg in args)
        {
            if (arg.StartsWith("--at-station=") && int.TryParse(arg.Substring("--at-station=".Length), out int station) && station > 0)
            {
                CallDeferred(MethodName.GoToStation, station);
            }
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
            // Giving up ends the run like any death: summary, then a new mountain.
            GameEvents.PublishKill("gave up");
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
            GameEvents.PublishKill("fell");
        }
    }

    /// <summary>Puts a group of one species in front of the carrier (screenshots).</summary>
    private void SpawnHerd(string spec)
    {
        string[] parts = spec.Split(':');
        int count = 5;
        if (parts.Length > 1 && int.TryParse(parts[1], out int n))
        {
            count = n;
        }
        if (PlayerBody == null)
        {
            return;
        }
        foreach (Node node in GetTree().Root.FindChildren("*", "Node3D", true, false))
        {
            if (node is not Wildlife.WildlifeSpawner spawner || spawner.Table == null)
            {
                continue;
            }
            foreach (Wildlife.WildlifeSpecies species in spawner.Table.Species)
            {
                if (!string.Equals(species.DisplayName, parts[0], System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Vector3 forward = -PlayerBody.GlobalBasis.Z;
                forward.Y = 0.0f;
                forward = forward.Normalized();
                Vector3 side = forward.Cross(Vector3.Up);
                PhysicsDirectSpaceState3D space = PlayerBody.GetWorld3D().DirectSpaceState;
                float distance = species.FleeDistance + 5.0f;
                for (int i = 0; i < count; i++)
                {
                    Vector3 at = PlayerBody.GlobalPosition + forward * (distance + (i % 3) * 2.5f) + side * ((i - count * 0.5f) * 2.6f + 4.0f);
                    if (spawner.Ground(space, at, out Vector3 ground, out string _, out float _))
                    {
                        spawner.Spawn(species, ground);
                    }
                }
                return;
            }
        }
    }

    private void OpenMap()
    {
        foreach (Node node in GetTree().GetNodesInGroup(Map.FieldMap.Group))
        {
            if (node is Map.FieldMap map)
            {
                map.SetOpen(true);
            }
        }
    }

    private void GoToStation(int station)
    {
        IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
        if (route != null && PlayerBody != null)
        {
            PlayerBody.Teleport(route.Station(station) + new Vector3(0.0f, 0.3f, 6.0f), 0.0f);
        }
    }

        private void Respawn()
    {
        if (Streamer != null)
        {
            PlayerBody?.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
            return;
        }
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
