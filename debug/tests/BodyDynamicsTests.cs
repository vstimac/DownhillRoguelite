using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the walk's weight (BodyDynamics): a pack bends the carrier forward and draws
/// his shoulders up; he leans into a start, rocks back when he stops and then settles; he banks into
/// a turn with the upper body leading; and the shown character carries all of it (through RigFollower).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/BodyDynamicsTests.tscn
/// </summary>
public partial class BodyDynamicsTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public float HeavyPack { get; set; } = 30.0f;

    private readonly List<string> _failures = new();
    private BodyDynamics? _body;

    public override async void _Ready()
    {
        _body = PlayerBody?.GetNodeOrNull<BodyDynamics>("Model/Rig/Skeleton3D/BodyDynamics");
        if (PlayerBody == null || Streamer == null || _body == null)
        {
            GD.PushError("BodyDynamicsTests: PlayerBody (with Model/Rig/Skeleton3D/BodyDynamics) and Streamer must be assigned.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.GlobalPosition = Streamer.SpawnPoint;
        await Seconds(1.0f);

        await TestPosture(PlayerBody, _body);
        await TestStartAndStop(PlayerBody, _body);
        await TestTurn(PlayerBody, _body);

        if (_failures.Count == 0)
        {
            GD.Print("BODY DYNAMICS TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("BODY DYNAMICS TESTS: FAIL");
            GetTree().Quit(1);
        }
    }

    private async Task TestPosture(PlayerController player, BodyDynamics body)
    {
        CarrierModel? look = player.GetNodeOrNull<CarrierModel>("CarrierModel");
        if (look?.Follower != null)
        {
            look.Follower.RecordPose = true;
        }
        body.LoadMassOverride = 0.0f;
        await Seconds(1.5f);
        float lightPitch = body.Pitch;
        float lightChest = ChestLean(player, look);
        body.LoadMassOverride = HeavyPack;
        await Seconds(1.5f);
        float heavyPitch = body.Pitch;
        float heavyChest = ChestLean(player, look);
        Check(Mathf.Abs(lightPitch) < 0.04f, $"no pack: he stands up straight ({Mathf.RadToDeg(lightPitch):0.0}°)",
            $"no pack, yet he leans {Mathf.RadToDeg(lightPitch):0.0}°");
        Check(heavyPitch > 0.15f && body.Hunch > 0.1f, $"{HeavyPack:0} kg bends him forward {Mathf.RadToDeg(heavyPitch):0}° with the shoulders up {Mathf.RadToDeg(body.Hunch):0}°",
            $"{HeavyPack:0} kg barely shows: lean {Mathf.RadToDeg(heavyPitch):0.0}°, shoulders {Mathf.RadToDeg(body.Hunch):0.0}°");
        if (look?.Follower != null)
        {
            Check(heavyChest - lightChest > 0.08f, $"the shown character leans with him (chest forward by {heavyChest - lightChest:0.00})",
                $"the shown character doesn't follow the lean ({lightChest:0.00} → {heavyChest:0.00})");
            look.Follower.RecordPose = false;
        }
    }

    private async Task TestStartAndStop(PlayerController player, BodyDynamics body)
    {
        body.LoadMassOverride = 15.0f;
        await Seconds(1.0f);
        float settled = body.Pitch;
        Vector3 forward = -player.GlobalBasis.Z;
        player.DirectionOverride = new Vector3(forward.X, 0.0f, forward.Z).Normalized();
        float most = settled;
        for (float t = 0.0f; t < 0.6f; t += 1.0f / 60.0f)
        {
            await Frame();
            most = Mathf.Max(most, body.Pitch);
        }
        Check(most - settled > 0.03f, $"he leans into the start (+{Mathf.RadToDeg(most - settled):0.0}°)",
            $"no lean into the start (+{Mathf.RadToDeg(most - settled):0.0}°)");
        await Seconds(2.5f);
        float walking = body.Pitch;
        player.DirectionOverride = Vector3.Zero;
        float least = walking;
        for (float t = 0.0f; t < 1.0f; t += 1.0f / 60.0f)
        {
            await Frame();
            least = Mathf.Min(least, body.Pitch);
        }
        Check(walking - least > 0.03f, $"he rocks back as he stops (-{Mathf.RadToDeg(walking - least):0.0}°)",
            $"no rock back on stopping (-{Mathf.RadToDeg(walking - least):0.0}°)");
        await Seconds(2.0f);
        float rest = body.Pitch;
        await Seconds(0.5f);
        Check(Mathf.Abs(body.Pitch - rest) < 0.01f, "then he settles", $"he keeps swaying ({Mathf.RadToDeg(body.Pitch - rest):0.0}° in 0.5 s)");
        player.DirectionOverride = null;
    }

    private async Task TestTurn(PlayerController player, BodyDynamics body)
    {
        Vector3 forward = -player.GlobalBasis.Z;
        player.DirectionOverride = new Vector3(forward.X, 0.0f, forward.Z).Normalized();
        await Seconds(2.5f);
        Vector3 right = player.GlobalBasis.X;
        player.DirectionOverride = new Vector3(right.X, 0.0f, right.Z).Normalized();
        float bank = 0.0f;
        float lead = 0.0f;
        for (float t = 0.0f; t < 1.0f; t += 1.0f / 60.0f)
        {
            await Frame();
            bank = Mathf.Max(bank, body.Roll);
            lead = Mathf.Min(lead, body.Twist);
        }
        Check(bank > 0.03f, $"turning right he banks right ({Mathf.RadToDeg(bank):0.0}°)", $"no bank into the right turn ({Mathf.RadToDeg(bank):0.0}°)");
        Check(lead < -0.03f, $"his upper body leads the turn ({Mathf.RadToDeg(-lead):0.0}°)", $"the upper body doesn't lead the turn ({Mathf.RadToDeg(lead):0.0}°)");
        player.DirectionOverride = null;
    }

    /// <summary>How far the shown character's chest sits ahead of its hips, as a share of their distance.</summary>
    private static float ChestLean(PlayerController player, CarrierModel? look)
    {
        if (look?.Follower == null)
        {
            return 0.0f;
        }
        Skeleton3D? skeleton = look.Follower.GetSkeleton();
        Vector3[] pose = look.Follower.TargetPose;
        if (skeleton == null || pose.Length == 0)
        {
            return 0.0f;
        }
        int hips = RigFollower.FindBone(skeleton, "x_Hips");
        int chest = RigFollower.FindBone(skeleton, "x_Neck");
        if (hips < 0 || chest < 0)
        {
            return 0.0f;
        }
        Vector3 up = skeleton.GlobalTransform.Basis * (pose[chest] - pose[hips]);
        Vector3 forward = -player.GlobalBasis.Z;
        return up.Normalized().Dot(new Vector3(forward.X, 0.0f, forward.Z).Normalized());
    }

    private async Task Seconds(float seconds)
    {
        for (float t = 0.0f; t < seconds; t += 1.0f / 60.0f)
        {
            await Frame();
        }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
}
