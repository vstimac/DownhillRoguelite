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
        // `--perf` prints where the frame time goes (debug/PerfLog).
        if (System.Array.IndexOf(args, "--perf") >= 0)
        {
            AddChild(new PerfLog { Name = "PerfLog", Breakdown = System.Array.IndexOf(args, "--perf-breakdown") >= 0 || System.Array.IndexOf(args, "--perf-cpu") >= 0,
                CpuBreakdown = System.Array.IndexOf(args, "--perf-cpu") >= 0,
                Counting = System.Array.IndexOf(args, "--perf-nodes") >= 0 });
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
            if (arg.StartsWith("--drop="))
            {
                string items = arg.Substring("--drop=".Length);
                GetTree().CreateTimer(1.0).Timeout += () => DropItems(items);
            }
            if (arg.StartsWith("--give="))
            {
                // `--give=paper,cord` puts items in the pack (render checks of icons).
                string items = arg.Substring("--give=".Length);
                GetTree().CreateTimer(0.8).Timeout += () => GiveItems(items, false);
            }
            if (arg.StartsWith("--wear="))
            {
                // `--wear=wool_hat,fur_cape` puts clothes on him (render checks of the worn look).
                string items = arg.Substring("--wear=".Length);
                GetTree().CreateTimer(0.8).Timeout += () => GiveItems(items, true);
            }
            if (arg.StartsWith("--portrait="))
            {
                // `--portrait=180` brings the camera close and turns it that many degrees (180: from the front).
                float turn = float.Parse(arg.Substring("--portrait=".Length), System.Globalization.CultureInfo.InvariantCulture);
                GetTree().CreateTimer(1.0).Timeout += () => Portrait(GetTree().Root, turn);
            }
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
        // `--page=inventory` (or journal) opens a UI page after a few seconds (render checks).
        foreach (string arg in args)
        {
            if (arg.StartsWith("--page="))
            {
                string page = arg.Substring("--page=".Length);
                GetTree().CreateTimer(3.0).Timeout += () => OpenPage(page);
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
            // `--at-president` stands the carrier a few metres uphill of the restsite president (render checks).
            if (arg == "--at-president")
            {
                GetTree().CreateTimer(1.2).Timeout += GoToPresident;
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

    private const ulong GiveUpConfirmMs = 2500;
    private ulong _giveUpArmedAt;

    public override void _UnhandledInput(InputEvent @event)
    {
        // Packing uses some of the same keys (R tips an item); pages and the chat take their own keys.
        if (GameEvents.IsPacking || GameEvents.IsScreenOpen || GameEvents.IsConversing || GameEvents.IsMapOpen)
        {
            return;
        }

        if (@event.IsActionPressed(ActionRespawn))
        {
            // Giving up ends the run like any death: summary, then a new mountain. Twice, to be sure.
            ulong now = Time.GetTicksMsec();
            if (now - _giveUpArmedAt < GiveUpConfirmMs)
            {
                _giveUpArmedAt = 0;
                GameEvents.PublishKill("gave up");
            }
            else
            {
                _giveUpArmedAt = now;
                GameEvents.PublishHint("Press R again to give up this run");
            }
            return;
        }
        // Debug keys only in development builds.
        if (!OS.IsDebugBuild())
        {
            return;
        }
        if (@event.IsActionPressed(ActionToggleFly))
        {
            ToggleFlyCamera();
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

    /// <summary>Gives the carrier these items (comma-separated ids), putting them on if <paramref name="wear"/> (screenshots).</summary>
    private void GiveItems(string ids, bool wear)
    {
        Inventory.CarrierInventory? inventory = null;
        Inventory.CarrierEquipment? equipment = null;
        foreach (Node node in GetTree().Root.FindChildren("*", "", true, false))
        {
            if (node is Inventory.CarrierInventory foundInventory)
            {
                inventory = foundInventory;
            }
            else if (node is Inventory.CarrierEquipment foundEquipment)
            {
                equipment = foundEquipment;
            }
        }
        if (inventory == null || equipment == null)
        {
            return;
        }
        foreach (string id in ids.Split(','))
        {
            if (ResourceLoader.Load($"res://inventory/items/{id.Trim()}.tres") is Inventory.ItemDefinition definition)
            {
                inventory.TryAdd(definition, -1.0f, false);
                if (wear)
                {
                    equipment.Wear(definition.ItemId);
                }
            }
        }
    }

    /// <summary>Lays items (comma-separated ids from inventory/items) on the ground ahead of the carrier (screenshots).</summary>
    private void DropItems(string ids)
    {
        if (PlayerBody == null)
        {
            return;
        }
        string[] list = ids.Split(',');
        Vector3 forward = -PlayerBody.GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        Vector3 side = forward.Cross(Vector3.Up);
        PhysicsDirectSpaceState3D space = PlayerBody.GetWorld3D().DirectSpaceState;
        for (int i = 0; i < list.Length; i++)
        {
            if (ResourceLoader.Load($"res://inventory/items/{list[i].Trim()}.tres") is not Inventory.ItemDefinition definition)
            {
                continue;
            }
            Vector3 at = PlayerBody.GlobalPosition + forward * (5.0f + i * 2.5f) + side * ((i % 2) * 2.0f - 1.0f) * 1.8f;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 10.0f, at + Vector3.Down * 10.0f, 1));
            if (hit.Count > 0)
            {
                at = (Vector3)hit["position"];
            }
            Inventory.WorldItem item = new() { Definition = definition };
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = at;
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

    private void OpenPage(string page)
    {
        foreach (Node node in GetTree().GetNodesInGroup(Ui.Hud.Group))
        {
            if (node is Ui.Hud hud)
            {
                if (page == "journal")
                {
                    hud.Toggle(hud.Journal);
                }
                else
                {
                    hud.Toggle(hud.Inventory);
                }
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

    private void GoToPresident()
    {
        Narrative.PresidentNpc? npc = FindPresident(GetTree().Root);
        IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
        if (npc == null || route == null || PlayerBody == null)
        {
            GD.Print("--at-president: no president seated");
            return;
        }
        Vector3 at = npc.GlobalPosition + new Vector3(0.0f, 0.0f, 5.0f);
        PlayerBody.Teleport(new Vector3(at.X, route.GroundHeight(at.X, at.Z) + 0.3f, at.Z), 0.0f);
    }

    private static Narrative.PresidentNpc? FindPresident(Node node)
    {
        if (node is Narrative.PresidentNpc npc)
        {
            return npc;
        }
        foreach (Node child in node.GetChildren())
        {
            Narrative.PresidentNpc? found = FindPresident(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
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

    private static void Portrait(Node node, float turn)
    {
        if (node is Player.ChaseCamera camera)
        {
            camera.Portrait(2.6f, turn);
        }
        foreach (Node child in node.GetChildren())
        {
            Portrait(child, turn);
        }
    }
}
