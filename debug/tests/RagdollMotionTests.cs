using System;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>World-space invariants through a native physical fall and recovery, including a moved origin.</summary>
public partial class RagdollMotionTests : Node3D
{
    [Export] public float EntrySpeed { get; set; } = 2.0f;
    [Export] public float MaxFrameMotion { get; set; } = 1.0f;
    [Export] public float MaxHorizontalTravel { get; set; } = 12.0f;
    [Export] public float MaxFollowError { get; set; } = 0.35f;
    [Export] public float MaxRecoveryDrift { get; set; } = 0.5f;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            await Fall("origin", Vector3.Zero, 0);
            await Fall("turned", Vector3.Zero, Mathf.Pi * 0.5f);
            await Fall("translated", new Vector3(1000, 0, -1000), Mathf.Pi);
        }
        catch (Exception error) { _failures++; GD.Print("FAIL  ragdoll motion exception: " + error); }
        GD.Print("RAGDOLL MOTION TESTS: " + _failures + " failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task Fall(string title, Vector3 origin, float yaw)
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        world.Position = origin;
        AddChild(world);
        try
        {
            // The test uses the actual player on this scene's flat floor, without obstacle contacts.
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            PlayerController player = world.GetNode<PlayerController>("Player");
            player.DirectionOverride = Vector3.Zero;
            player.Teleport(origin + Vector3.Up * 0.1f, yaw);
            player.Health?.Refill();
            player.Stamina?.Refill();
            for (int i = 0; i < 20; i++) { await Frame(); }
            PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
            PhysicalBoneSimulator3D simulator = player.GetNode<PhysicalBoneSimulator3D>("Model/Rig/Skeleton3D/Ragdoll");
            PhysicalBone3D hips = simulator.GetNode<PhysicalBone3D>("Bone_DEF-hips");
            Vector3 start = player.GlobalPosition;
            Vector3 previous = start;
            float largestStep = 0;
            float furthest = 0;
            float followError = 0;
            float peakSpeed = 0;
            int followSamples = 0;
            bool finite = true;
            ragdoll.GoDown(-player.GlobalBasis.Z * EntrySpeed, Vector3.Zero);
            bool entered = ragdoll.Downed;
            int frames = Mathf.CeilToInt((ragdoll.MaxDownTime + ragdoll.GetUpBlendTime + 1.0f) * Engine.PhysicsTicksPerSecond);
            for (int i = 0; i < frames; i++)
            {
                await Frame();
                Vector3 at = player.GlobalPosition;
                finite &= at.IsFinite() && hips.GlobalPosition.IsFinite() && hips.LinearVelocity.IsFinite();
                largestStep = Mathf.Max(largestStep, at.DistanceTo(previous));
                furthest = Mathf.Max(furthest, FlatDistance(at, start));
                peakSpeed = Mathf.Max(peakSpeed, hips.LinearVelocity.Length());
                if (i > 5 && ragdoll.Downed && simulator.Influence > 0.999f)
                {
                    followError = Mathf.Max(followError, FlatDistance(at, hips.GlobalPosition));
                    followSamples++;
                }
                previous = at;
                if (i > 5 && !ragdoll.Downed) { break; }
            }
            string metrics = $"max step {largestStep:0.000} m, travel {furthest:0.000} m, hips speed {peakSpeed:0.000} m/s, tracking {followError:0.000} m";
            Check(title + " fall enters native simulation and stays finite", entered && finite, metrics);
            Check(title + " low-energy fall has no unexplained position jumps", largestStep <= MaxFrameMotion, metrics);
            Check(title + " low-energy fall stays near its launch point", furthest <= MaxHorizontalTravel, metrics);
            Check(title + " controller follows physical hips in world space", followSamples > 5 && followError <= MaxFollowError, metrics);
            bool recovered = !ragdoll.Downed && player.Health?.Dead == false;
            Vector3 recoveredAt = player.GlobalPosition;
            float recoveryDrift = 0;
            for (int i = 0; i < 30; i++)
            {
                await Frame();
                recoveryDrift = Mathf.Max(recoveryDrift, FlatDistance(player.GlobalPosition, recoveredAt));
            }
            Check(title + " recovery does not repeatedly translate the carrier", recovered && !ragdoll.Downed
                && recoveryDrift <= MaxRecoveryDrift, $"recovered {recovered}, drift {recoveryDrift:0.000} m");
        }
        finally
        {
            RemoveChild(world);
            world.QueueFree();
            await Frame();
        }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private void Check(string title, bool passed, string detail)
    {
        if (passed) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failures++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
