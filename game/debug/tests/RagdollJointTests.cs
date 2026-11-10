using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The ragdoll's joints behave like a body's: through falls forwards, up, sideways and backwards, knees
/// only bend backwards and elbows only forwards (never past straight the wrong way, never sideways),
/// they stop at their limit, nothing comes apart, and hips, neck, ankles and wrists stay inside their
/// swing. A joint may give for a moment when a limb slams into the ground; what counts is how far it
/// stays out for <see cref="HoldFrames"/> frames. The weight is split over the body as measured
/// (Plagenhoef), hands and feet included.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RagdollJointTests.tscn
/// </summary>
public partial class RagdollJointTests : Node3D
{
    /// <summary>How far past a limit a joint may stay (degrees).</summary>
    [Export] public float Slack { get; set; } = 10.0f;
    /// <summary>How far a knee or elbow may turn off its axis (degrees): a forearm rolls a little about itself.</summary>
    [Export] public float SidewaysSlack { get; set; } = 16.0f;
    /// <summary>How long a joint must stay out to count (physics frames): a slam may bend it for a moment.</summary>
    [Export] public int HoldFrames { get; set; } = 6;
    /// <summary>How far a knee or elbow may come apart (m).</summary>
    [Export] public float MaxGap { get; set; } = 0.05f;
    /// <summary>Frames measured per fall.</summary>
    [Export] public int FallFrames { get; set; } = 150;
    /// <summary>Prints each joint's worst moment.</summary>
    [Export] public bool Verbose { get; set; }

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

    private readonly List<string> _failures = new();
    private readonly Dictionary<string, Queue<float>> _recent = new();
    private readonly Dictionary<string, (float Value, string Where)> _worst = new();

    public override async void _Ready()
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        world.GetNode("BlueStep").Free();
        world.GetNode("OrangeBlock").Free();
        PlayerController player = world.GetNode<PlayerController>("Player");
        player.DirectionOverride = Vector3.Zero;
        PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
        Skeleton3D skeleton = player.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        for (int i = 0; i < 30; i++)
        {
            await Frame();
        }

        TestMasses(ragdoll);
        float mostBent = 0.0f;
        Vector3[] pushes = { Vector3.Zero, Vector3.Up * 3.0f, Vector3.Right * 4.0f, Vector3.Back * 4.0f };
        for (int fall = 0; fall < pushes.Length; fall++)
        {
            player.Health?.Refill();
            player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), fall * 1.3f);
            for (int i = 0; i < 30; i++)
            {
                await Frame();
            }
            ragdoll.GoDown(-player.GlobalBasis.Z * 5.0f, pushes[fall]);
            await Frame();
            _recent.Clear();
            // The joints are built in the rig's rest pose: their axes and the middle of their swing are
            // measured from there; coming apart is measured from the first frame down.
            Dictionary<string, Vector3> anchors = new();
            Dictionary<string, Vector3> childAxes = new();
            Dictionary<string, Vector3> parentAxes = new();
            foreach ((string parentName, string childName) in Hinges.Concat(Balls))
            {
                PhysicalBone3D parent = ragdoll.BodyOf(parentName)!;
                PhysicalBone3D child = ragdoll.BodyOf(childName)!;
                anchors[childName] = parent.GlobalTransform.AffineInverse() * (child.GlobalTransform * child.JointOffset.Origin);
                Basis parentRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(parentName)).Basis.Orthonormalized();
                Basis childRest = skeleton.GetBoneGlobalRest(skeleton.FindBone(childName)).Basis.Orthonormalized();
                // Hinges: the axis (joint Z) in each body; balls: the bone's direction (its Y).
                Vector3 axis = Vector3.Up;
                if (Hinges.Contains((parentName, childName)))
                {
                    axis = child.JointOffset.Basis.Z.Normalized();
                }
                childAxes[childName] = axis;
                parentAxes[childName] = (parentRest.Inverse() * childRest * axis).Normalized();
            }

            for (int f = 0; f < FallFrames && ragdoll.Downed; f++)
            {
                await Frame();
                string where = $"fall {fall} frame {f}";
                foreach ((string parentName, string childName) in Hinges.Concat(Balls))
                {
                    PhysicalBone3D parent = ragdoll.BodyOf(parentName)!;
                    PhysicalBone3D child = ragdoll.BodyOf(childName)!;
                    Basis p = parent.GlobalBasis.Orthonormalized();
                    Basis c = child.GlobalBasis.Orthonormalized();
                    Vector3 held = (p * parentAxes[childName]).Normalized();
                    Vector3 own = (c * childAxes[childName]).Normalized();
                    float apart = (parent.GlobalTransform * anchors[childName]).DistanceTo(child.GlobalTransform * child.JointOffset.Origin);
                    Hold("apart", childName, apart, where);
                    float off = Mathf.RadToDeg(held.AngleTo(own));
                    if (Hinges.Contains((parentName, childName)))
                    {
                        // Signed bend about the hinge: + is the way the joint really bends.
                        Vector3 t = p.Y;
                        Vector3 s = c.Y;
                        float bend = Mathf.RadToDeg(Mathf.Atan2(t.Cross(s).Dot(own), t.Dot(s)));
                        mostBent = Mathf.Max(mostBent, bend);
                        Hold("wrong way", childName, -bend - ragdoll.HingeOverextension, where);
                        Hold("past the limit", childName, bend - Mathf.Abs(LimitOf(ragdoll, childName)), where);
                        Hold("sideways", childName, off, where);
                    }
                    else
                    {
                        Hold("swing", childName, off - SwingOf(ragdoll, childName), where);
                    }
                }
            }
            for (int i = 0; i < 600 && (ragdoll.Downed || player.Health?.Dead == true); i++)
            {
                await Frame();
            }
        }

        if (Verbose)
        {
            foreach (KeyValuePair<string, (float Value, string Where)> worst in _worst.OrderBy(w => w.Key))
            {
                GD.Print($"  {worst.Key}: {worst.Value.Value:0.00} at {worst.Value.Where}");
            }
        }
        Check("knees bend only backwards and elbows only forwards", Worst("wrong way") <= Slack, Detail("wrong way", "deg past straight", ragdoll.HingeOverextension));
        Check("knees and elbows don't bend sideways", Worst("sideways") <= SidewaysSlack, Detail("sideways", "deg off their axis", 0.0f));
        Check("knees and elbows stop at their limit", Worst("past the limit") <= Slack, Detail("past the limit", "deg past it", 0.0f));
        Check("the falls really bend knees and elbows", mostBent > 20.0f, $"most bent {mostBent:0} deg");
        Check("hips, neck, ankles and wrists stay inside their swing", Worst("swing") <= Slack, Detail("swing", "deg past it", 0.0f));
        Check("no joint comes apart", Worst("apart") <= MaxGap, Detail("apart", "m", 0.0f));

        if (_failures.Count == 0)
        {
            GD.Print("RAGDOLL JOINT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"RAGDOLL JOINT TESTS: FAIL ({string.Join(", ", _failures)})");
            GetTree().Quit(1);
        }
    }

    /// <summary>Counts how far <paramref name="what"/> stayed out over the last HoldFrames frames.</summary>
    private void Hold(string what, string joint, float value, string where)
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
        if (!_worst.TryGetValue(key, out (float Value, string Where) worst) || held > worst.Value)
        {
            _worst[key] = (held, $"{where} ({joint})");
        }
    }

    private float Worst(string what)
    {
        float worst = float.MinValue;
        foreach (KeyValuePair<string, (float Value, string Where)> entry in _worst)
        {
            if (entry.Key.StartsWith(what + "|"))
            {
                worst = Mathf.Max(worst, entry.Value.Value);
            }
        }
        return worst;
    }

    private string Detail(string what, string unit, float allowedExtra)
    {
        string where = "";
        float worst = float.MinValue;
        foreach (KeyValuePair<string, (float Value, string Where)> entry in _worst)
        {
            if (entry.Key.StartsWith(what + "|") && entry.Value.Value > worst)
            {
                worst = entry.Value.Value;
                where = entry.Value.Where;
            }
        }
        string extra = "";
        if (allowedExtra > 0.0f)
        {
            extra = $" beyond the {allowedExtra:0} allowed";
        }
        return $"worst {worst:0.00} {unit}{extra}, held {HoldFrames} frames, {where}";
    }

    private void TestMasses(PlayerRagdoll ragdoll)
    {
        float total = 0.0f;
        foreach (PhysicalBone3D body in ragdoll.Bodies)
        {
            total += body.Mass;
        }
        PhysicalBone3D? hand = ragdoll.BodyOf("DEF-hand.L");
        PhysicalBone3D? foot = ragdoll.BodyOf("DEF-foot.R");
        PhysicalBone3D? thigh = ragdoll.BodyOf("DEF-thigh.L");
        Check("hands and feet are part of the ragdoll", hand != null && foot != null, $"{ragdoll.Bodies.Count} bodies");
        Check("the body's weight is split as measured (Plagenhoef): the whole body, a thigh a tenth",
            Mathf.Abs(total - ragdoll.BodyMass) < ragdoll.BodyMass * 0.02f && thigh != null && Mathf.Abs(thigh.Mass / ragdoll.BodyMass - 0.105f) < 0.005f,
            $"total {total:0.0} kg of {ragdoll.BodyMass:0} (before the pack), thigh {thigh?.Mass:0.0} kg");
    }

    private static float LimitOf(PlayerRagdoll ragdoll, string boneName)
    {
        int index = System.Array.IndexOf(ragdoll.BoneNames, boneName);
        if (index < 0 || index >= ragdoll.HingeBend.Length)
        {
            return 0.0f;
        }
        return ragdoll.HingeBend[index];
    }

    private static float SwingOf(PlayerRagdoll ragdoll, string boneName)
    {
        int index = System.Array.IndexOf(ragdoll.BoneNames, boneName);
        if (index < 0 || index >= ragdoll.SwingLimits.Length)
        {
            return ragdoll.SwingLimit;
        }
        return ragdoll.SwingLimits[index];
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
