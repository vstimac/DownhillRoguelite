using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Network;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// A carrier who falls onto his back gets up through the back get-up clip. One who falls onto his belly
/// first rolls over onto his back (physically, the ragdoll still simulating) and then gets up through the
/// same clip. Either way he ends standing, upright, close to where he fell, and the roll keeps every
/// joint inside its limits.
/// `-- --headings` adds belly falls at three more headings (`-- --headings=12`: that many, spread round);
/// `-- --procedural` adds the experimental procedural rise (it does not pass yet).
/// </summary>
public partial class GetUpTests : Node3D
{
    /// <summary>How far past a limit a joint may stay during the roll (degrees), as in RagdollJointTests.</summary>
    private const float Slack = 10.0f;
    /// <summary>How long a joint must stay out to count (physics frames), as in RagdollJointTests.</summary>
    private const int HoldFrames = 6;
    /// <summary>How far he may end up from where he lay before getting up (m): the roll and the clip's own travel.</summary>
    private const float MaxDrift = 1.5f;

    private int _failures;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            await RiseCheck(backward: true);
            await RiseCheck(backward: false);
            float[] headings = Array.Empty<float>();
            foreach (string arg in args)
            {
                if (arg == "--headings")
                {
                    headings = new[] { 1.3f, 2.6f, 4.4f };
                }
                else if (arg.StartsWith("--headings=", StringComparison.Ordinal) && int.TryParse(arg["--headings=".Length..], out int count) && count > 0)
                {
                    headings = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        headings[i] = 0.1f + Mathf.Tau * i / count;
                    }
                }
            }
            foreach (float heading in headings)
            {
                await RiseCheck(backward: false, heading: heading);
            }
            // The procedural rise is experimental: it runs only with `-- --procedural`, so the default
            // suite keeps its clip checks. Its failures are reported, not hidden.
            if (args.Contains("--procedural"))
            {
                await RiseCheck(backward: true, procedural: true);
                await RiseCheck(backward: false, procedural: true);
            }
            // Last: after a remote copy has been freed, a later world can trip a .NET finalizer crash in
            // Godot 4.7.2 (a script-backed RefCounted disposed twice); NetPoseTests also ends with its copy.
            await CoopCheck();
        }
        catch (Exception error) { Check("get-up tests complete without exceptions", false, error.ToString()); }
        GD.Print($"GET UP TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task RiseCheck(bool backward, bool procedural = false, float heading = 0.0f)
    {
        string side = (backward ? "back" : "belly") + (procedural ? ", procedural" : "") + (heading != 0.0f ? $", heading {heading:0.0}" : "");
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        try
        {
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            PlayerController body = world.GetNode<PlayerController>("Player");
            body.DirectionOverride = Vector3.Zero;
            body.Teleport(new Vector3(0, 0.1f, 3), heading);
            body.Health?.Refill();
            await Frames(40);
            body.GetNode<BalanceController>("Balance").SetPhysicsProcess(false);
            PlayerRagdoll ragdoll = body.GetNode<PlayerRagdoll>("Ragdoll");
            Skeleton3D skeleton = body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            int headBone = skeleton.FindBone("DEF-head");
            int hipsBone = skeleton.FindBone("DEF-hips");
            ragdoll.SafeImpactSpeed = 100;
            ragdoll.ProceduralGetUp = procedural;

            // Backward is +Z (the carrier faces -Z); forward falls onto the belly.
            Vector3 dir = backward ? body.GlobalBasis.Z : -body.GlobalBasis.Z;
            ragdoll.GoDown(dir * 2, Vector3.Zero);

            JointWatch joints = new(ragdoll, skeleton);
            // What is shown: the skeleton's modifiers (the ragdoll among them) apply only while it
            // updates, so the pose is read there, not between frames.
            Vector3 shownHead = Vector3.Zero;
            Vector3 shownHipsUp = Vector3.Zero;
            float step = 0.0f;
            bool shown = false;
            bool trace = OS.GetCmdlineUserArgs().Contains("--trace");
            void Shown()
            {
                Vector3 head = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(headBone)).Origin;
                if (shown)
                {
                    step = Mathf.Max(step, head.DistanceTo(shownHead));
                }
                if (trace) { GD.Print($"TRACE head {head} step {(shown ? head.DistanceTo(shownHead) : 0):0.000} downed {ragdoll.Downed} getting {ragdoll.GettingUp} rising {ragdoll.Rising} turning {ragdoll.TurningOver} body {body.GlobalPosition} yaw {body.Rotation.Y:0.00}"); }
                shown = true;
                shownHead = head;
                shownHipsUp = (skeleton.GlobalTransform.Basis * skeleton.GetBoneGlobalPose(hipsBone).Basis).Y.Normalized();
            }
            skeleton.SkeletonUpdated += Shown;
            bool rising = false;
            bool faceUp = false;
            bool sawTurn = false;
            bool lyingFaceDown = false;
            Vector3 lying = body.GlobalPosition;
            StringName clip = "";
            float yawBefore = body.Rotation.Y;
            float riseTurn = 0.0f;
            float rollStep = 0.0f;
            int turnFrames = 0;
            for (int i = 0; i < 900 && !rising; i++)
            {
                yawBefore = body.Rotation.Y;
                await Frames(1);
                if (ragdoll.TurningOver)
                {
                    if (!sawTurn)
                    {
                        // Where he lay before the roll: the drift is measured from here.
                        lying = body.GlobalPosition;
                        lyingFaceDown = !ragdoll.LyingFaceUp();
                        step = 0.0f;
                    }
                    sawTurn = true;
                    turnFrames++;
                    joints.Measure();
                }
                if (ragdoll.Rising)
                {
                    rising = true;
                    faceUp = ragdoll.LyingFaceUp();
                    clip = ragdoll.RiseClip;
                    riseTurn = Mathf.Abs(Mathf.AngleDifference(yawBefore, body.Rotation.Y));
                    rollStep = step;
                    if (!sawTurn)
                    {
                        lying = body.GlobalPosition;
                    }
                }
            }
            Check($"a fall onto the {side} starts a get-up", rising);
            if (procedural)
            {
                Check($"the procedural get-up plays no clip ({side})", clip == "", $"clip '{clip}'");
            }
            else if (backward)
            {
                Check("lying on his back, the back get-up plays", faceUp && clip == "GetUp_Back", $"face up {faceUp}, clip {clip}");
                Check("lying on his back, he doesn't roll over first", !sawTurn && !ragdoll.TurnedOver, $"rolled {sawTurn}");
            }
            else
            {
                Check($"lying on his belly, he rolls over onto his back ({side})", sawTurn && lyingFaceDown && ragdoll.TurnedOver,
                    $"rolled {sawTurn}, was face down {lyingFaceDown}, ended face up {ragdoll.TurnedOver}, rolling {turnFrames / 60.0f:0.0} s");
                Check($"rolled onto his back, the back get-up plays ({side})", faceUp && clip == "GetUp_Back", $"face up {faceUp}, clip {clip}");
                Check($"the roll keeps knees and elbows inside their limits ({side})", joints.Worst("hinge") <= Slack, joints.Detail("hinge"));
                Check($"the roll keeps hips, neck, ankles and wrists inside their swing ({side})", joints.Worst("swing") <= Slack, joints.Detail("swing"));
                Check($"the head never jumps while he rolls over ({side})", rollStep < 0.12f, $"most in a frame {rollStep:0.000} m");
            }

            if (!procedural)
            {
                // The body's turn toward the clip happens while he lies there, not as a snap at the rise.
                Check($"the get-up starts without turning him on the spot ({side})", riseTurn < 0.1f, $"turn at the rise {Mathf.RadToDeg(riseTurn):0.0} deg");
            }

            int frames = 0;
            step = 0.0f;
            for (int i = 0; i < 900 && ragdoll.Rising; i++)
            {
                await Frames(1);
                frames++;
            }
            if (!procedural)
            {
                // From the ragdoll into the clip and up: the head moves as a head does, never jumps
                // (0.12 m in a frame is over 7 m/s).
                Check($"the head never jumps while he gets up ({side})", step < 0.12f, $"most in a frame {step:0.000} m");
            }
            float headHeight = shownHead.Y - body.GlobalPosition.Y;
            Check($"he stands up after a fall onto the {side}", !ragdoll.Downed && !ragdoll.Rising && headHeight > 1.2f,
                $"downed {ragdoll.Downed}, head {headHeight:0.00} m, {frames} frames");
            // A few frames more: standing, his hips point up and he isn't sliding off.
            await Frames(10);
            skeleton.SkeletonUpdated -= Shown;
            Vector3 hipsUp = shownHipsUp;
            float drift = new Vector2(body.GlobalPosition.X - lying.X, body.GlobalPosition.Z - lying.Z).Length();
            if (!procedural)
            {
                Check($"he ends upright ({side})", hipsUp.Y > 0.9f && !ragdoll.Downed, $"hips up {hipsUp.Y:0.00}");
                Check($"he gets up within {MaxDrift} m of where he lay ({side})", drift < MaxDrift, $"drift {drift:0.00} m");
            }
            else
            {
                // Stands within 15 s of game time (900 physics frames at 60 Hz) and does not drift far.
                Check($"the procedural rise stands him up within 15 s ({side})", ragdoll.ProceduralStood && !ragdoll.Rising, $"{frames} frames, stood {ragdoll.ProceduralStood}");
                Check($"the procedural rise does not drift more than 1 m ({side})", drift < 1.0f, $"drift {drift:0.00} m");
            }
        }
        finally
        {
            world.QueueFree();
            await Frames(5);
        }
    }

    /// <summary>
    /// A co-op partner sees the same belly get-up: a remote copy fed what the game sends (the pose in the
    /// skeleton's space, the body's place and turn; no smoothing delay here) is compared with the carrier
    /// in the world, frame by frame, from the fall through the roll to standing.
    /// </summary>
    private async Task CoopCheck()
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        Skeleton3D? source = null;
        Skeleton3D? target = null;
        Action? capture = null;
        Action? compare = null;
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
            ragdoll.SafeImpactSpeed = 100;
            source = body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            Node3D model = body.GetNode<Node3D>("Model");
            Node3D look = body.GetNode<Node3D>("CarrierModel");
            RemoteCarrier remote = new() { Name = "Partner", InterpolationDelay = 0 };
            remote.UseModelPlacement(model.Transform, look.Transform);
            world.AddChild(remote);
            remote.Pose!.MaxPacketsPerSecond = 100000;
            target = remote.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            int[] indices = NetPoseCodec.Bind(source);
            int head = Array.IndexOf(NetPoseCodec.Bones, "DEF-head");
            float[] data = new float[NetPoseCodec.Size];
            int sequence = 0;
            Vector3 carrierHead = Vector3.Zero;
            Vector3 previousHead = Vector3.Zero;
            float worst = 0.0f;
            int compared = 0;
            bool rolled = false;
            Skeleton3D from = source;
            Skeleton3D to = target;
            capture = () =>
            {
                if (!NetPoseCodec.Capture(from, model, null, indices, data)) { return; }
                previousHead = carrierHead;
                carrierHead = from.GlobalTransform * from.GetBoneGlobalPose(indices[head]).Origin;
                int flags = ragdoll.Downed ? NetPoseCodec.Falling : 0;
                remote.Pose.Receive(++sequence, flags, data);
                remote.Receive(body.GlobalPosition, model.GlobalRotation.Y, "", 0.0f, 1.0f, false, 0);
            };
            compare = () =>
            {
                if (sequence == 0 || !ragdoll.Downed && !ragdoll.Rising) { return; }
                rolled |= ragdoll.TurningOver;
                Vector3 seen = to.GlobalTransform * to.GetBoneGlobalPose(to.FindBone(NetPoseCodec.Bones[head])).Origin;
                // The copy may be a frame behind the carrier: take the closer of the last two.
                worst = Mathf.Max(worst, Mathf.Min(seen.DistanceTo(carrierHead), seen.DistanceTo(previousHead)));
                compared++;
            };
            source.SkeletonUpdated += capture;
            target.SkeletonUpdated += compare;
            ragdoll.GoDown(-body.GlobalBasis.Z * 2, Vector3.Zero);
            for (int i = 0; i < 1200 && (ragdoll.Downed || ragdoll.Rising); i++)
            {
                await Frames(1);
            }
            Check("a co-op partner sees the same belly get-up, roll included", rolled && compared > 100 && worst < 0.1f && !ragdoll.Downed,
                $"{compared} frames, rolled {rolled}, head apart at most {worst:0.000} m");
        }
        finally
        {
            if (source != null && capture != null) { source.SkeletonUpdated -= capture; }
            if (target != null && compare != null) { target.SkeletonUpdated -= compare; }
            world.QueueFree();
            await Frames(5);
        }
    }

    /// <summary>
    /// Measures the ragdoll's joints the way RagdollJointTests does, from the rig's rest pose: knees and
    /// elbows (signed bend about the hinge, past straight or past the limit) and the ball joints' swing.
    /// A value counts only when it stays out for HoldFrames frames in a row.
    /// </summary>
    private sealed class JointWatch
    {
        private static readonly (string Parent, string Child)[] Hinges =
        {
            ("DEF-thigh.L", "DEF-shin.L"), ("DEF-thigh.R", "DEF-shin.R"),
            ("DEF-upper_arm.L", "DEF-forearm.L"), ("DEF-upper_arm.R", "DEF-forearm.R"),
        };

        private static readonly (string Parent, string Child)[] Balls =
        {
            ("DEF-hips", "DEF-thigh.L"), ("DEF-hips", "DEF-thigh.R"),
            ("DEF-spine.003", "DEF-head"),
            ("DEF-shin.L", "DEF-foot.L"), ("DEF-shin.R", "DEF-foot.R"),
            ("DEF-forearm.L", "DEF-hand.L"), ("DEF-forearm.R", "DEF-hand.R"),
        };

        private readonly PlayerRagdoll _ragdoll;
        private readonly Dictionary<string, Vector3> _childAxes = new();
        private readonly Dictionary<string, Vector3> _parentAxes = new();
        private readonly Dictionary<string, Queue<float>> _recent = new();
        private readonly Dictionary<string, (float Value, string Joint)> _worst = new();

        public JointWatch(PlayerRagdoll ragdoll, Skeleton3D skeleton)
        {
            _ragdoll = ragdoll;
            foreach ((string parentName, string childName) in Hinges.Concat(Balls))
            {
                PhysicalBone3D child = ragdoll.BodyOf(childName)!;
                Basis parentRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(parentName)).Basis.Orthonormalized();
                Basis childRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(childName)).Basis.Orthonormalized();
                Vector3 axis = Hinges.Contains((parentName, childName)) ? child.JointOffset.Basis.Z.Normalized() : Vector3.Up;
                _childAxes[childName] = axis;
                _parentAxes[childName] = (parentRest.Inverse() * childRest * axis).Normalized();
            }
        }

        public void Measure()
        {
            foreach ((string parentName, string childName) in Hinges.Concat(Balls))
            {
                PhysicalBone3D parent = _ragdoll.BodyOf(parentName)!;
                PhysicalBone3D child = _ragdoll.BodyOf(childName)!;
                Basis p = parent.GlobalBasis.Orthonormalized();
                Basis c = child.GlobalBasis.Orthonormalized();
                Vector3 held = (p * _parentAxes[childName]).Normalized();
                Vector3 own = (c * _childAxes[childName]).Normalized();
                if (Hinges.Contains((parentName, childName)))
                {
                    float bend = Mathf.RadToDeg(Mathf.Atan2(p.Y.Cross(c.Y).Dot(own), p.Y.Dot(c.Y)));
                    float limit = Mathf.Abs(ValueAt(_ragdoll.HingeBend, childName, 0.0f));
                    Hold("hinge", childName, Mathf.Max(-bend - _ragdoll.HingeOverextension, bend - limit));
                }
                else
                {
                    float off = Mathf.RadToDeg(held.AngleTo(own));
                    Hold("swing", childName, off - ValueAt(_ragdoll.SwingLimits, childName, _ragdoll.SwingLimit));
                }
            }
        }

        public float Worst(string what)
        {
            return _worst.TryGetValue(what, out (float Value, string Joint) worst) ? worst.Value : float.MinValue;
        }

        public string Detail(string what)
        {
            return _worst.TryGetValue(what, out (float Value, string Joint) worst)
                ? $"worst {worst.Value:0.0} deg past the limit ({worst.Joint})"
                : "nothing measured";
        }

        private float ValueAt(float[] values, string boneName, float fallback)
        {
            int index = Array.IndexOf(_ragdoll.BoneNames, boneName);
            return index >= 0 && index < values.Length ? values[index] : fallback;
        }

        private void Hold(string what, string joint, float value)
        {
            string key = $"{what}|{joint}";
            if (!_recent.TryGetValue(key, out Queue<float>? recent))
            {
                recent = new Queue<float>();
                _recent[key] = recent;
            }
            recent.Enqueue(value);
            if (recent.Count > HoldFrames)
            {
                recent.Dequeue();
            }
            if (recent.Count < HoldFrames)
            {
                return;
            }
            float held = recent.Min();
            if (!_worst.TryGetValue(what, out (float Value, string Joint) worst) || held > worst.Value)
            {
                _worst[what] = (held, joint);
            }
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
