using System;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// A carrier who falls onto his back gets up through the back get-up clip; one who falls onto his belly
/// keeps the crouch (there is no face-down clip yet). Either way he ends standing.
/// </summary>
public partial class GetUpTests : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            await RiseCheck(backward: true);
            await RiseCheck(backward: false);
        }
        catch (Exception error) { Check("get-up tests complete without exceptions", false, error.ToString()); }
        GD.Print($"GET UP TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task RiseCheck(bool backward)
    {
        string side = backward ? "back" : "belly";
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        try
        {
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            PlayerController body = world.GetNode<PlayerController>("Player");
            body.DirectionOverride = Vector3.Zero;
            body.Teleport(new Vector3(0, 0.1f, 3), 0.0f);
            body.Health?.Refill();
            await Frames(40);
            body.GetNode<BalanceController>("Balance").SetPhysicsProcess(false);
            PlayerRagdoll ragdoll = body.GetNode<PlayerRagdoll>("Ragdoll");
            Skeleton3D skeleton = body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            int headBone = skeleton.FindBone("DEF-head");
            ragdoll.SafeImpactSpeed = 100;

            // Backward is +Z (the carrier faces -Z); forward falls onto the belly.
            Vector3 dir = backward ? body.GlobalBasis.Z : -body.GlobalBasis.Z;
            ragdoll.GoDown(dir * 2, Vector3.Zero);

            bool rising = false;
            bool faceUp = false;
            StringName clip = "";
            for (int i = 0; i < 900 && !rising; i++)
            {
                await Frames(1);
                if (ragdoll.Rising)
                {
                    rising = true;
                    faceUp = ragdoll.LyingFaceUp();
                    clip = ragdoll.RiseClip;
                }
            }
            Check($"a fall onto the {side} starts a get-up", rising);
            if (backward)
            {
                Check("lying on his back, the back get-up plays", faceUp && clip == "GetUp_Back", $"face up {faceUp}, clip {clip}");
            }
            else
            {
                Check("lying on his belly, the crouch plays until a face-down clip exists", !faceUp && clip == ragdoll.GetUpClip, $"face up {faceUp}, clip {clip}");
            }

            for (int i = 0; i < 900 && ragdoll.Rising; i++)
            {
                await Frames(1);
            }
            float headHeight = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(headBone)).Origin.Y - body.GlobalPosition.Y;
            Check($"he stands up after a fall onto the {side}", !ragdoll.Downed && !ragdoll.Rising && headHeight > 1.2f,
                $"downed {ragdoll.Downed}, head {headHeight:0.00} m");
        }
        finally
        {
            world.QueueFree();
            await Frames(5);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Check(string title, bool ok, string detail = "")
    {
        if (ok) { GD.Print("PASS  " + title + " " + detail); }
        else { _failures++; GD.Print("FAIL  " + title + " " + detail); }
    }
}
