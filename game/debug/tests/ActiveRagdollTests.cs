using System;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Analytical muscle checks plus real-controller pose/IK and native-physics integration.</summary>
public partial class ActiveRagdollTests : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            TorqueChecks();
            await CarrierChecks(0);
            await CarrierChecks(Mathf.Pi * 0.5f);
        }
        catch (Exception error) { Check("active ragdoll completes without exceptions", false, error.ToString()); }
        GD.Print($"ACTIVE RAGDOLL TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private void TorqueChecks()
    {
        Vector3 rest = RagdollMuscles.CalculateTorque(Quaternion.Identity, Quaternion.Identity, Vector3.Zero, 10, 0.7f, 5);
        Check("a relaxed muscle at its target adds no energy", rest.IsZeroApprox());
        Vector3 damping = RagdollMuscles.CalculateTorque(Quaternion.Identity, Quaternion.Identity, Vector3.Up * 2, 10, 0.7f, 5);
        Check("damping opposes relative joint rotation", damping.IsEqualApprox(Vector3.Down * 1.4f));
        Quaternion target = new(Vector3.Right, 0.2f);
        Vector3 correction = RagdollMuscles.CalculateTorque(target, Quaternion.Identity, Vector3.Zero, 10, 0.7f, 5);
        Check("a 0.2-radian error produces the expected 2 Nm correction", correction.IsEqualApprox(Vector3.Right * 2));
        Quaternion sameRotation = new(-target.X, -target.Y, -target.Z, -target.W);
        Check("quaternion sign cannot reverse the muscle", correction.IsEqualApprox(RagdollMuscles.CalculateTorque(sameRotation, Quaternion.Identity, Vector3.Zero, 10, 0.7f, 5)));
        Vector3 hinge = RagdollMuscles.CalculateHingeTorque(new Quaternion(Vector3.Right, 0.5f), Quaternion.Identity,
            Vector3.Right * 3, Vector3.Up, 10, 0.7f, 5);
        Check("an elbow never drives forbidden swing or off-axis damping", hinge.IsZeroApprox());
        Quaternion mixed = new Quaternion(Vector3.Right, 0.8f) * new Quaternion(Vector3.Up, 0.2f);
        Vector3 twist = RagdollMuscles.CalculateHingeTorque(mixed, Quaternion.Identity, Vector3.Zero, Vector3.Up, 10, 0, 5);
        Check("elbow drive extracts twist before applying the spring", twist.IsEqualApprox(Vector3.Up * 2));
        Vector3 capped = RagdollMuscles.CalculateTorque(new Quaternion(Vector3.Forward, 2.8f), Quaternion.Identity, Vector3.Back * 40, 100, 2, 5);
        Check("large errors and speed cannot exceed the torque cap", capped.IsFinite() && Mathf.Abs(capped.Length() - 5) < 0.0001f);
    }

    private async Task CarrierChecks(float yaw)
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        try
        {
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            PlayerController body = world.GetNode<PlayerController>("Player");
            body.DirectionOverride = Vector3.Zero;
            body.Teleport(new Vector3(0, 0.1f, 3), yaw);
            body.Health?.Refill();
            await Frames(40);
            BalanceController balance = body.GetNode<BalanceController>("Balance");
            balance.SetPhysicsProcess(false);
            ActiveBalancePose pose = body.GetNode<ActiveBalancePose>("Model/Rig/Skeleton3D/ActiveBalancePose");
            RagdollMuscles muscles = body.GetNode<RagdollMuscles>("RagdollMuscles");
            Check("the shipped player keeps physical muscles off by default", !muscles.Enabled);
            PlayerRagdoll ragdoll = body.GetNode<PlayerRagdoll>("Ragdoll");
            FootIK feet = body.GetNode<FootIK>("FootIK");
            Skeleton3D skeleton = body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            int handBone = skeleton.FindBone("DEF-hand.R");
            int chestBone = skeleton.FindBone("DEF-spine.003");
            Vector3 handFromChest = Vector3.Zero;
            void RecordHand()
            {
                handFromChest = skeleton.GlobalBasis * (skeleton.GetBoneGlobalPose(handBone).Origin - skeleton.GetBoneGlobalPose(chestBone).Origin);
            }
            skeleton.SkeletonUpdated += RecordHand;
            await Frames(2);
            Vector3 start = body.GlobalPosition;
            Vector3 right = body.GlobalBasis.X;
            float relaxedHand = handFromChest.Dot(right);
            Vector3 leftContact = feet.Feet[0].TargetPosition;
            Vector3 rightContact = feet.Feet[1].TargetPosition;
            GameEvents.PublishBalance(balance.Snapshot with
            {
                State = BalanceState.Stable,
                Imbalance = Vector2.Zero,
                SupportCenter = start,
                CenterOfMassGround = start + right * 0.03f,
            });
            await Frames(30);
            Check("stable balance leaves the original hip pose untouched", pose.Effort < 0.001f && pose.HipShift.IsZeroApprox());
            BalanceSnapshot displaced = balance.Snapshot with
            {
                State = BalanceState.Wobbling,
                Imbalance = new Vector2(1.2f, 0),
                CenterOfMassGround = start + right * 0.2f,
                SupportCenter = start,
                Right = right,
                Forward = -body.GlobalBasis.Z,
            };
            GameEvents.PublishBalance(displaced);
            await Frames(60);
            string context = $"yaw {yaw:0.00}, shift {pose.HipShift}, effort {pose.Effort:0.00}";
            Check("hips counter the load in world space", pose.HipShift.Dot(right) < -0.02f && pose.HipShift.IsFinite(), context);
            Check("recovery bends the knees within its bounds", pose.HipShift.Y < -0.02f && pose.HipShift.Y >= -pose.MaxCrouch - 0.001f
                && new Vector2(pose.HipShift.X, pose.HipShift.Z).Length() <= pose.MaxHipShift + 0.001f, context);
            Check("pose recovery never translates the controller", body.GlobalPosition.DistanceTo(start) < 0.03f, context);
            Check("free arms react while foot IK stays grounded", pose.RightArmWeight > 0.3f && feet.IkWeight > 0.95f
                && feet.Feet.Count == 2 && feet.Feet[0].HasGround && feet.Feet[1].HasGround, context);
            Check("the free hand opens outward to balance instead of crossing the chest", handFromChest.Dot(right) > relaxedHand + 0.05f,
                $"relaxed {relaxedHand:0.000}, balancing {handFromChest.Dot(right):0.000}");
            skeleton.SkeletonUpdated -= RecordHand;
            Check("pelvis recovery leaves both ankle contacts on their original support", feet.Feet[0].TargetPosition.DistanceTo(leftContact) < 0.035f
                && feet.Feet[1].TargetPosition.DistanceTo(rightContact) < 0.035f,
                $"left {feet.Feet[0].TargetPosition.DistanceTo(leftContact):0.000}, right {feet.Feet[1].TargetPosition.DistanceTo(rightContact):0.000}");
            pose.Influence = 0.5f;
            await Frames(10);
            Check("native half-influence blending preserves contact targets", feet.Feet[0].TargetPosition.DistanceTo(leftContact) < 0.035f
                && feet.Feet[1].TargetPosition.DistanceTo(rightContact) < 0.035f);
            pose.Active = false;
            await Frames(10);
            Check("native modifier disable cannot leave a stale ankle correction", feet.Feet[0].TargetPosition.DistanceTo(leftContact) < 0.035f
                && feet.Feet[1].TargetPosition.DistanceTo(rightContact) < 0.035f);
            pose.Active = true;
            pose.Influence = 1;
            await Frames(10);

            GameEvents.PublishSwordDrawn(true);
            await Frames(2);
            Check("combat immediately owns both arms", pose.LeftArmWeight == 0 && pose.RightArmWeight == 0);
            GameEvents.PublishSwordDrawn(false);
            CarrierTorch torch = body.GetNode<CarrierTorch>("Torch");
            torch.SetRemote(true, false);
            await Frames(2);
            Check("a lit torch retains its arm while the other balances", pose.LeftArmWeight == 0 && pose.RightArmWeight > 0.3f);
            torch.SetRemote(false, false);

            pose.Enabled = false;
            await Frames(45);
            Check("A/B disable returns smoothly to the original pose", pose.Weight == 0 && pose.HipShift.Length() < 0.001f && pose.Effort < 0.001f);
            pose.Enabled = true;
            GameEvents.PublishBalance(displaced);
            await Frames(30);
            body.Teleport(start, yaw);
            Check("teleport clears recovery springs immediately", pose.HipShift.IsZeroApprox() && pose.Effort == 0 && pose.Weight == 0);
            await Frames(20);

            ragdoll.SafeImpactSpeed = 100;
            ragdoll.GoDown(-body.GlobalBasis.Z * 2, Vector3.Zero);
            await Frames(8);
            Check("disabled muscles never apply a physical impulse", ragdoll.Downed && muscles.Weight == 0 && muscles.AppliedPairs == 0);
            body.Teleport(start, yaw);
            await Frames(20);
            muscles.Enabled = true;
            ragdoll.GoDown(-body.GlobalBasis.Z * (muscles.MaxControlledSpeed + 2), Vector3.Zero);
            await Frames(8);
            Check("high-energy launches remain passive for the whole fall", ragdoll.Downed && muscles.Weight == 0 && muscles.AppliedPairs == 0);
            body.Teleport(start, yaw);
            await Frames(20);
            ragdoll.GoDown(-body.GlobalBasis.Z * 2, Vector3.Zero);
            await Frames(3);
            PhysicalBone3D arm = ragdoll.BodyOf("DEF-upper_arm.R")!;
            arm.AngularVelocity += Vector3.Up * 2;
            await Frames(8);
            Check("living falls drive real physical joints with bounded torques", ragdoll.Downed && muscles.Weight > 0
                && muscles.AppliedPairs > 0 && muscles.PeakTorque > 0.01f && muscles.PeakTorque <= muscles.MaxTorque + 0.001f,
                $"weight {muscles.Weight:0.00}, pairs {muscles.AppliedPairs}, peak {muscles.PeakTorque:0.00}");
            Check("pose corrections release the simulated body", pose.Weight == 0 && pose.HipShift.IsZeroApprox());
            int beforeDisable = muscles.AppliedPairs;
            muscles.Enabled = false;
            await Frames(4);
            Check("switching muscles off stops all impulses immediately", muscles.Weight == 0 && muscles.AppliedPairs == beforeDisable);
            muscles.Enabled = true;
            await Frames(4);
            Check("reenabling mid-fall waits until a fresh fall", muscles.Weight == 0 && muscles.AppliedPairs == beforeDisable);
            await Frames(35);
            int afterRelax = muscles.AppliedPairs;
            await Frames(10);
            Check("muscles relax completely after their short hold", muscles.Weight == 0 && muscles.AppliedPairs == afterRelax);
            await Frames(420);
            Check("native fall returns to a finite upright controller", !ragdoll.Downed && body.GlobalPosition.IsFinite() && body.Health?.Dead == false);

            StaticBody3D ramp = new()
            {
                Position = new Vector3(-8, 1.3f, -6),
                Rotation = new Vector3(0, 0, Mathf.DegToRad(20)),
            };
            world.AddChild(ramp);
            ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 0.4f, 6) } });
            body.Teleport(new Vector3(-8, 2, -6), yaw);
            await Frames(60);
            GameEvents.PublishBalance(displaced with
            {
                SupportCenter = body.GlobalPosition,
                CenterOfMassGround = body.GlobalPosition + body.GlobalBasis.X * 0.2f,
            });
            await Frames(45);
            bool slopeContacts = body.IsOnFloor() && pose.HipShift.IsFinite();
            foreach (FootState foot in feet.Feet)
            {
                slopeContacts &= foot.HasGround && foot.TargetPosition.IsFinite()
                    && foot.GroundNormal.AngleTo(ramp.GlobalBasis.Y) < 0.01f
                    && (foot.TargetPosition - foot.GroundPoint).Dot(foot.GroundNormal) > 0.02f;
            }
            Check("both ankles remain above a real cross-slope during recovery", slopeContacts,
                $"floor {body.IsOnFloor()}, hips {pose.HipShift}, normals {feet.Feet[0].GroundNormal}/{feet.Feet[1].GroundNormal}");

            body.Health!.TakeDamage(10000, "test");
            await Frames(8);
            Check("death never activates protective muscles", body.Health.Dead && muscles.Weight == 0 && muscles.AppliedPairs == 0);
        }
        finally
        {
            GameEvents.PublishSwordDrawn(false);
            RemoveChild(world);
            world.QueueFree();
            await Frames(2);
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
