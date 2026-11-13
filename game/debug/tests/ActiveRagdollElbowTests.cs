using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Real enabled muscle falls inside the production speed gate, using the joint test's unchanged 16-degree limit.</summary>
public partial class ActiveRagdollElbowTests : Node3D
{
    [Export] public float SidewaysSlack { get; set; } = 16;
    [Export] public int HoldFrames { get; set; } = 6;
    [Export] public int FallFrames { get; set; } = 150;
    [Export] public float[] Speeds { get; set; } = { 1, 2, 3 };
    private int _failures;
    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { Check("enabled elbow trial completes", false, error.ToString()); }
        GD.Print($"ACTIVE ELBOW TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private async Task Run()
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        world.GetNode("BlueStep").Free(); world.GetNode("OrangeBlock").Free();
        PlayerController player = world.GetNode<PlayerController>("Player");
        player.DirectionOverride = Vector3.Zero;
        PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
        ragdoll.SafeImpactSpeed = 100;
        RagdollMuscles muscles = player.GetNode<RagdollMuscles>("RagdollMuscles");
        muscles.Enabled = true;
        Skeleton3D skeleton = player.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        string[] names = { "DEF-forearm.L", "DEF-forearm.R" };
        float worst = 0;
        int impulses = 0;
        for (int trial = 0; trial < Speeds.Length; trial++)
        {
            float yaw = trial * 1.3f;
            player.Teleport(new Vector3(0, 0.1f, 0), yaw); player.Health?.Refill();
            await Frames(30);
            List<Queue<float>> recent = new() { new(), new() };
            ragdoll.GoDown(-player.GlobalBasis.Z * Speeds[trial], Vector3.Zero);
            int applied = 0;
            for (int frame = 0; frame < FallFrames && ragdoll.Downed; frame++)
            {
                await Frames(1);
                applied = Math.Max(applied, muscles.AppliedPairs);
                for (int arm = 0; arm < names.Length; arm++)
                {
                    string parentName = "DEF-upper_arm.L";
                    if (arm == 1) { parentName = "DEF-upper_arm.R"; }
                    PhysicalBone3D parent = ragdoll.BodyOf(parentName)!;
                    PhysicalBone3D child = ragdoll.BodyOf(names[arm])!;
                    Basis parentRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(parentName)).Basis.Orthonormalized();
                    Basis childRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(names[arm])).Basis.Orthonormalized();
                    Vector3 axis = child.JointOffset.Basis.Z.Normalized();
                    Vector3 held = (parent.GlobalBasis * parentRest.Inverse() * childRest * axis).Normalized();
                    Vector3 own = (child.GlobalBasis * axis).Normalized();
                    recent[arm].Enqueue(Mathf.RadToDeg(held.AngleTo(own)));
                    if (recent[arm].Count > HoldFrames) { recent[arm].Dequeue(); }
                    if (recent[arm].Count == HoldFrames)
                    {
                        float minimum = float.MaxValue;
                        foreach (float value in recent[arm]) { minimum = Mathf.Min(minimum, value); }
                        worst = Mathf.Max(worst, minimum);
                    }
                }
            }
            impulses += applied;
            GD.Print($"Elbow trial speed {Speeds[trial]:0.0}: {applied} actual paired impulses");
        }
        Check("enabled elbows remain within the original sideways limit", worst <= SidewaysSlack, $"{worst:0.00} degrees held {HoldFrames} frames; limit {SidewaysSlack}");
        Check("the trial applies real protective muscle impulses", impulses > 0, $"{impulses} paired impulses");
        world.QueueFree(); await Frames(2);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private void Check(string title, bool ok, string detail)
    { if (ok) { GD.Print("PASS " + title + ": " + detail); } else { _failures++; GD.Print("FAIL " + title + ": " + detail); } }
}
