using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the dive (Left Ctrl) and the landing roll (Space while falling): a dive pushes
/// off, flies forward in an arc and rolls out, animated on the driving skeleton (never a T-pose), and
/// he's standing at the end; a tree in the way stops the travel; diving off a high ledge takes him over
/// and the roll cue shows on the way down; a 7 m fall with Space in the window rolls (no knockdown,
/// the Roll clip plays); the same fall without it knocks him down.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/DiveTests.tscn
/// </summary>
public partial class DiveTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;

    private readonly List<string> _failures = new();
    private CharacterAnimator? _animator;
    private Skeleton3D? _driver;

    public override async void _Ready()
    {
        Dive? dive = PlayerBody?.GetNodeOrNull<Dive>("Dive");
        _animator = PlayerBody?.GetNodeOrNull<CharacterAnimator>("Animator");
        _driver = PlayerBody?.GetNodeOrNull<Skeleton3D>("Model/Rig/Skeleton3D");
        if (PlayerBody == null || Streamer == null || dive == null || _animator == null || _driver == null)
        {
            GD.PushError("DiveTests: PlayerBody (with its Dive, Animator and driver) and Streamer must be assigned.");
            GetTree().Quit(1);
            return;
        }
        // The animated dive and roll (RagdollDive off); the ragdoll throws are checked at the end.
        dive.RagdollDive = false;
        PlayerBody.RagdollJump = false;
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestDive(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestTree(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestLedge(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRoll(PlayerBody, dive, true);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRoll(PlayerBody, dive, false);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestBufferedPress(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRagdollDive(PlayerBody, dive);
        await Place(PlayerBody, Streamer.SpawnPoint);
        await TestRagdollJump(PlayerBody);

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
        // Ctrl: push off, an arc forward, a roll out; each part shows its clip on the driver.
        Vector3 start = player.GlobalPosition;
        bool started = dive.TryDive();
        HashSet<string> clips = new();
        float peak = 0.0f;
        float leastAway = 99.0f;
        bool wentDown = false;
        for (int i = 0; i < 300 && dive.Diving; i++)
        {
            await Frame();
            wentDown = wentDown || GameEvents.IsDowned;
            peak = Mathf.Max(peak, Height(player));
            if (_animator!.ActionWeight > 0.95f)
            {
                clips.Add($"{dive.Stage}:{_animator.ActionClip}");
                leastAway = Mathf.Min(leastAway, AwayFromRest());
            }
        }
        await WaitSeconds(0.5f);
        float travelled = new Vector2(player.GlobalPosition.X - start.X, player.GlobalPosition.Z - start.Z).Length();
        bool allParts = clips.Contains("PushOff:Jump_Start") && clips.Contains("Flight:Jump") && clips.Contains("Roll:Roll");
        Check(started && allParts && !wentDown && peak > 0.4f && travelled > 4.0f && !dive.Diving && !GameEvents.IsVaulting && Height(player) < 0.3f,
            $"Left Ctrl: push-off, a {peak:0.0} m high arc and a roll, {travelled:0.0} m forward, and he's on his feet",
            $"dive: started {started}, clips [{string.Join(", ", clips)}], downed {wentDown}, peak {peak:0.00} m, travelled {travelled:0.0} m, still diving {dive.Diving}, {Height(player):0.00} m above ground");
        Check(leastAway > 0.3f, $"the dive is animated on the body, never a T-pose (least {leastAway:0.00} rad from rest)",
            $"dive pose: least {leastAway:0.00} rad from the rest pose (a T-pose)");
        Check(_animator!.ActionWeight < 0.05f, "after the dive the normal animation is back", $"action weight {_animator.ActionWeight:0.00} after the dive");
    }

    private async Task TestBufferedPress(PlayerController player, Dive dive)
    {
        // Ctrl pressed a moment before the feet touch down (a bump on the slope) still dives on landing.
        int dives = dive.Dives;
        player.Teleport(player.GlobalPosition + Vector3.Up * 0.8f, player.Rotation.Y);
        for (int i = 0; i < 60 && Height(player) > 0.2f; i++)
        {
            await Frame();
        }
        bool airborne = !player.IsOnFloor();
        Input.ActionPress("player_dive");
        await Frame();
        Input.ActionRelease("player_dive");
        bool dived = false;
        for (int i = 0; i < 40 && !dived; i++)
        {
            await Frame();
            dived = dive.Dives > dives;
        }
        Check(airborne && dived, "Ctrl pressed just before landing still dives", $"airborne {airborne}, dived {dived}");
        for (int i = 0; i < 300 && dive.Diving; i++)
        {
            await Frame();
        }
        await WaitSeconds(0.5f);
    }

    private async Task TestTree(PlayerController player, Dive dive)
    {
        // A trunk 1.5 m ahead: the dive stops at it instead of passing through.
        Vector3 start = player.GlobalPosition;
        Vector3 ahead = -player.GlobalBasis.Z;
        ahead.Y = 0.0f;
        ahead = ahead.Normalized();
        StaticBody3D trunk = new() { Name = "Trunk" };
        trunk.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.35f, Height = 6.0f } });
        AddChild(trunk);
        trunk.GlobalPosition = start + ahead * 1.9f + Vector3.Up * 2.0f;
        await Frame();
        int blocked = dive.Blocked;
        dive.TryDive();
        for (int i = 0; i < 300 && dive.Diving; i++)
        {
            await Frame();
        }
        float along = (player.GlobalPosition - start).Dot(ahead);
        Check(dive.Blocked == blocked + 1 && along < 1.9f, $"a tree in the way stops the dive ({along:0.0} m on, the trunk at 1.9 m)",
            $"tree: blocked {blocked} to {dive.Blocked}, {along:0.0} m on (the trunk at 1.9 m)");
        trunk.QueueFree();
        await WaitSeconds(0.3f);
    }

    /// <summary>The largest turn (rad) of the driver's arm and leg bones away from their rest pose.</summary>
    private float AwayFromRest()
    {
        float most = 0.0f;
        foreach (string bone in new[] { "DEF-upper_arm.L", "DEF-upper_arm.R", "DEF-thigh.L", "DEF-thigh.R", "DEF-spine.001" })
        {
            int index = _driver!.FindBone(bone);
            if (index < 0)
            {
                continue;
            }
            Quaternion pose = _driver.GetBonePoseRotation(index);
            Quaternion rest = _driver.GetBoneRest(index).Basis.GetRotationQuaternion();
            most = Mathf.Max(most, pose.AngleTo(rest));
        }
        return most;
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
        bool cue = false;
        for (int i = 0; i < 240; i++)
        {
            await Frame();
            cue = cue || GameEvents.CurrentVault.Active;
        }
        float below = top - player.GlobalPosition.Y;
        Check(started && cue && below > 5.0f, $"diving off an 8 m ledge takes him over, the roll cue shows, and he ends up {below:0.0} m lower",
            $"ledge: started {started}, roll cue {cue}, {below:0.0} m lower");
        ledge.QueueFree();
        for (int i = 0; i < 900 && GameEvents.IsDowned; i++)
        {
            await Frame();
        }
        await WaitSeconds(2.0f);
    }

    private async Task TestRoll(PlayerController player, Dive dive, bool press)
    {
        int rolls = dive.Rolls;
        player.Teleport(player.GlobalPosition + Vector3.Up * 7.0f, 0.0f);
        bool primed = false;
        bool rollShown = false;
        float leastAway = 99.0f;
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
        for (int i = 0; i < 90; i++)
        {
            await Frame();
            if (dive.Stage == Dive.Phase.Roll && _animator!.ActionClip == "Roll" && _animator.ActionWeight > 0.95f)
            {
                rollShown = true;
                leastAway = Mathf.Min(leastAway, AwayFromRest());
            }
        }
        if (press)
        {
            Check(primed && !GameEvents.IsDowned && dive.Rolls == rolls + 1, "a 7 m fall with Space in the window rolls: no knockdown",
                $"roll: primed {primed}, downed {GameEvents.IsDowned}, rolls {rolls} to {dive.Rolls}");
            Check(rollShown && leastAway > 0.3f, $"the rolled landing plays the Roll clip (least {leastAway:0.00} rad from rest, no T-pose)",
                $"roll animation: shown {rollShown}, least {leastAway:0.00} rad from rest");
            for (int i = 0; i < 120 && dive.Diving; i++)
            {
                await Frame();
            }
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

    /// <summary>Ctrl (the default): physics throws him forward as a ragdoll, and he gets up by himself.</summary>
    private async Task TestRagdollDive(PlayerController player, Dive dive)
    {
        dive.RagdollDive = true;
        PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
        Vector3 start = player.GlobalPosition;
        Vector3 forward = -player.GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        bool started = dive.TryDive();
        bool downed = false;
        for (int i = 0; i < 30; i++)
        {
            await Frame();
            downed |= ragdoll.Downed;
        }
        float seconds = 0.0f;
        while (ragdoll.Downed && seconds < 8.0f)
        {
            await Frame();
            seconds += 1.0f / Engine.PhysicsTicksPerSecond;
        }
        await WaitSeconds(0.5f);
        Vector3 moved = player.GlobalPosition - start;
        float along = new Vector3(moved.X, 0.0f, moved.Z).Dot(forward);
        Check(started && downed && !ragdoll.Downed && along > 1.0f,
            $"Ctrl throws him forward as a ragdoll ({along:0.0} m) and he gets up after {seconds:0.0} s",
            $"started {started}, downed {downed}, still down {ragdoll.Downed}, {along:0.0} m forward, {seconds:0.0} s");
        dive.RagdollDive = false;
    }

    /// <summary>Space (the default): a ragdoll hop up, and he is back on his feet soon after.</summary>
    private async Task TestRagdollJump(PlayerController player)
    {
        player.RagdollJump = true;
        PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
        int jumps = player.Jumps;
        Input.ActionPress("player_jump");
        await Frame();
        await Frame();
        Input.ActionRelease("player_jump");
        bool downed = false;
        for (int i = 0; i < 40; i++)
        {
            await Frame();
            downed |= ragdoll.Downed;
        }
        float seconds = 0.0f;
        while (ragdoll.Downed && seconds < 6.0f)
        {
            await Frame();
            seconds += 1.0f / Engine.PhysicsTicksPerSecond;
        }
        Check(player.Jumps == jumps + 1 && downed && !ragdoll.Downed && seconds < 4.0f,
            $"Space throws him up as a ragdoll and he is up again {seconds:0.0} s later",
            $"jumps {player.Jumps - jumps}, downed {downed}, still down {ragdoll.Downed}, {seconds:0.0} s");
        player.RagdollJump = false;
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
