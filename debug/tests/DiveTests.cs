using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the dive (Left Ctrl) and the landing roll (Space while falling): a dive carries
/// the carrier well forward and lands him on his feet; diving off a high ledge hands him to falling
/// and he ends up on the ground below; a 7 m fall with Space in the window rolls (no knockdown);
/// the same fall without it knocks him down.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/DiveTests.tscn
/// </summary>
public partial class DiveTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        Dive? dive = PlayerBody?.GetNodeOrNull<Dive>("Dive");
        if (PlayerBody == null || Streamer == null || dive == null)
        {
            GD.PushError("DiveTests: PlayerBody (with its Dive) and Streamer must be assigned.");
            GetTree().Quit(1);
            return;
        }
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestDive(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestLedge(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRoll(PlayerBody, dive, true);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRoll(PlayerBody, dive, false);

        if (_failures.Count == 0)
        {
            GD.Print("DIVE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("DIVE TESTS: FAIL");
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

    private async Task TestDive(PlayerController player, Dive dive)
    {
        Vector3 start = player.GlobalPosition;
        bool started = dive.TryDive();
        for (int i = 0; i < 400 && dive.Diving; i++)
        {
            await Frame();
        }
        await WaitSeconds(0.5f);
        float travelled = new Vector2(player.GlobalPosition.X - start.X, player.GlobalPosition.Z - start.Z).Length();
        float height = Height(player);
        Check(started && !dive.Diving && travelled > 4.0f && !GameEvents.IsDowned && height < 0.3f,
            $"Left Ctrl dives {travelled:0.0} m forward and lands him on his feet", $"dive: started {started}, travelled {travelled:0.0} m, downed {GameEvents.IsDowned}, {height:0.00} m above ground");
    }

    private async Task TestLedge(PlayerController player, Dive dive)
    {
        // A wide platform 8 m up; he stands near its downhill edge and dives off it.
        Vector3 ground = player.GlobalPosition;
        StaticBody3D ledge = new() { Name = "Ledge" };
        ledge.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6.0f, 1.0f, 6.0f) } });
        AddChild(ledge);
        ledge.GlobalPosition = ground + Vector3.Up * 8.0f + Vector3.Back * 1.5f;
        player.Teleport(ground + Vector3.Up * 8.6f, 0.0f);
        await WaitSeconds(0.6f);
        float top = player.GlobalPosition.Y;
        bool started = dive.TryDive();
        bool released = false;
        for (int i = 0; i < 400; i++)
        {
            await Frame();
            if (!dive.Diving)
            {
                // Handed to a fall: still well above the ground when the dive lets go.
                released = Height(player) > 3.0f;
                break;
            }
        }
        await WaitSeconds(3.0f);
        float below = top - player.GlobalPosition.Y;
        Check(started && released && below > 5.0f, $"diving off an 8 m ledge hands him to falling: he ends up {below:0.0} m lower",
            $"ledge: started {started}, released into a fall {released}, {below:0.0} m lower");
        ledge.QueueFree();
        await WaitSeconds(5.0f);
    }

    private async Task TestRoll(PlayerController player, Dive dive, bool press)
    {
        int rolls = dive.Rolls;
        player.Teleport(player.GlobalPosition + Vector3.Up * 7.0f, 0.0f);
        bool primed = false;
        await Frame();
        await Frame();
        for (int i = 0; i < 300 && Height(player) > 0.05f; i++)
        {
            await Frame();
            VaultCue cue = GameEvents.CurrentVault;
            if (press && !primed && cue.Active && cue.InWindow)
            {
                dive.TryRoll(cue);
                primed = true;
            }
        }
        await WaitSeconds(0.3f);
        if (press)
        {
            Check(primed && !GameEvents.IsDowned && dive.Rolls == rolls + 1, "a 7 m fall with Space in the window rolls: no knockdown",
                $"roll: primed {primed}, downed {GameEvents.IsDowned}, rolls {rolls} to {dive.Rolls}");
        }
        else
        {
            Check(GameEvents.IsDowned, "the same fall without Space knocks him down", $"no roll: downed {GameEvents.IsDowned}");
            await WaitSeconds(5.0f);
        }
    }

    /// <summary>How far his feet are above the ground below him.</summary>
    private float Height(PlayerController player)
    {
        Vector3 at = player.GlobalPosition;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.5f, at + Vector3.Down * 80.0f, 1,
            new Godot.Collections.Array<Rid> { player.GetRid() }));
        if (hit.Count > 0)
        {
            return at.Y - ((Vector3)hit["position"]).Y;
        }
        return 80.0f;
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        player.Stamina?.Refill();
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
