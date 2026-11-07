using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the Jump Over vault: walking at a low block on the slope brings up the cue,
/// which reaches its window as the carrier closes in; vaulting in the window carries him past the
/// block and lands him on his feet; Space too early trips him into the ragdoll and costs health;
/// a rock too tall to vault offers no cue.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/VaultTests.tscn
/// </summary>
public partial class VaultTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;

    private readonly List<string> _failures = new();
    private readonly List<StaticBody3D> _blocks = new();

    public override async void _Ready()
    {
        Vault? vault = PlayerBody?.GetNodeOrNull<Vault>("Vault");
        if (PlayerBody == null || Streamer == null || vault == null)
        {
            GD.PushError("VaultTests: PlayerBody (with its Vault) and Streamer must be assigned.");
            GetTree().Quit(1);
            return;
        }
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestVault(PlayerBody, vault);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestTrip(PlayerBody, vault);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestTooTall(PlayerBody);

        PlayerBody.DirectionOverride = null;
        if (_failures.Count == 0)
        {
            GD.Print("VAULT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("VAULT TESTS: FAIL");
            GetTree().Quit(1);
        }
    }

    private void Check(bool ok, string pass, string fail)
    {
        if (ok)
        {
            GD.Print("PASS: " + pass);
        }
        else
        {
            _failures.Add(fail);
        }
    }

    /// <summary>Downhill on the spot (where the slope falls away), flattened.</summary>
    private static Vector3 Downhill()
    {
        return Vector3.Forward;
    }

    /// <summary>A block (in the vaultable group) on the ground this far ahead, this tall.</summary>
    private StaticBody3D Block(PlayerController player, float ahead, float height)
    {
        foreach (StaticBody3D old in _blocks)
        {
            old.QueueFree();
        }
        _blocks.Clear();
        Vector3 at = player.GlobalPosition + Downhill() * ahead;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 20.0f, at + Vector3.Down * 40.0f));
        if (hit.Count > 0)
        {
            at = (Vector3)hit["position"];
        }
        StaticBody3D block = new() { Name = "TestBlock" };
        block.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.0f, height, 0.6f) } });
        block.AddToGroup(TerrainChunk.VaultableGroup);
        AddChild(block);
        block.GlobalPosition = at + Vector3.Up * (height * 0.5f - 0.05f);
        _blocks.Add(block);
        return block;
    }

    private async Task TestVault(PlayerController player, Vault vault)
    {
        StaticBody3D block = Block(player, 6.0f, 0.8f);
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--side-camera") >= 0)
        {
            // For a look at the dive: a sun, and a camera beside the block looking across the slope.
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), ShadowEnabled = true });
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.7f, 0.74f, 0.8f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.6f) } });
            Camera3D side = new() { Fov = 55.0f };
            AddChild(side);
            side.GlobalPosition = block.GlobalPosition + Vector3.Right * 7.0f + Vector3.Up * 1.2f + Downhill() * 1.0f;
            side.LookAt(block.GlobalPosition + Vector3.Up * 0.6f + Downhill() * 1.0f);
            side.Current = true;
        }
        player.DirectionOverride = Downhill();
        bool seen = false;
        bool window = false;
        for (int i = 0; i < 360 && !window; i++)
        {
            await Frame();
            seen |= GameEvents.CurrentVault.Active;
            window = GameEvents.CurrentVault.InWindow;
        }
        Check(seen && window, $"walking at a low block brings up the cue and it reaches its window ({GameEvents.CurrentVault.Distance:0.0} m)",
            $"no vault cue (seen {seen}, window {window})");
        if (!window)
        {
            return;
        }
        GD.Print($"VAULT STARTS AT FRAME {Engine.GetFramesDrawn()}");
        vault.Attempt(GameEvents.CurrentVault);
        for (int i = 0; i < 400 && vault.Vaulting; i++)
        {
            await Frame();
        }
        player.DirectionOverride = null;
        await WaitSeconds(0.5f);
        float past = (player.GlobalPosition - block.GlobalPosition).Dot(Downhill());
        Check(!vault.Vaulting && past > 1.0f && !GameEvents.IsDowned && Mathf.Abs(player.GlobalPosition.Y - Ground(player.GlobalPosition).Y) < 0.5f,
            $"Space in the window vaults the block: he lands {past:0.0} m past it, on his feet",
            $"vault: still vaulting {vault.Vaulting}, {past:0.0} m past the block, downed {GameEvents.IsDowned}");
    }

    private async Task TestTrip(PlayerController player, Vault vault)
    {
        Block(player, 6.0f, 0.8f);
        player.DirectionOverride = Downhill();
        float health = player.Health?.Current ?? 0.0f;
        bool early = false;
        for (int i = 0; i < 360 && !early; i++)
        {
            await Frame();
            early = GameEvents.CurrentVault.Active && GameEvents.CurrentVault.Distance > GameEvents.CurrentVault.Far + 0.3f;
        }
        int trips = vault.Trips;
        if (early)
        {
            vault.Attempt(GameEvents.CurrentVault);
        }
        player.DirectionOverride = null;
        await WaitSeconds(0.3f);
        float after = player.Health?.Current ?? 0.0f;
        Check(early && vault.Trips == trips + 1 && GameEvents.IsDowned && after < health,
            $"Space too early trips him: down, health {health:0} to {after:0}",
            $"trip: early cue {early}, trips {trips} to {vault.Trips}, downed {GameEvents.IsDowned}, health {health} to {after}");
        await WaitSeconds(4.0f);
    }

    private async Task TestTooTall(PlayerController player)
    {
        Block(player, 6.0f, 2.4f);
        player.DirectionOverride = Downhill();
        bool seen = false;
        for (int i = 0; i < 200; i++)
        {
            await Frame();
            seen |= GameEvents.CurrentVault.Active;
        }
        player.DirectionOverride = null;
        Check(!seen, "a rock too tall to vault offers no cue", "a 2.4 m block offered a vault");
    }

    private Vector3 Ground(Vector3 point)
    {
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 3.0f, point + Vector3.Down * 10.0f, 1,
            new Godot.Collections.Array<Rid> { PlayerBody!.GetRid() }));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point;
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        player.DirectionOverride = null;
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(SettleTime);
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await Frame();
        }
    }
}
