using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>What a <see cref="FootSlipMeter"/> measured; kept apart so it can be read after the creature is gone.</summary>
public sealed class FootSlipStats
{
    public float SlipSum;
    public float SpeedSum;
    public int Samples;

    /// <summary>Mean speed of the planted foot over the ground while the body moved (m/s).</summary>
    public float Slip => Samples > 0 ? SlipSum / Samples : 0.0f;
    /// <summary>Mean body speed over the same frames (m/s).</summary>
    public float Speed => Samples > 0 ? SpeedSum / Samples : 0.0f;
    /// <summary>
    /// Planted foot's speed against the body's: 0 is a foot that stays put on the ground, 1 a foot that
    /// glides along with the body as if the legs weren't moving at all (a creature that "swims").
    /// </summary>
    public float Skate => SpeedSum > 0.0f ? SlipSum / SpeedSum : 0.0f;

    public string Feet = "";
    public override string ToString() => $"{(Feet.Length > 0 ? Feet + ": " : "")}planted foot {Slip:0.00} m/s at body {Speed:0.0} m/s (skate {Skate:0.00}, {Samples} frames)";
}

/// <summary>
/// Added under a creature (tests): each physics frame it takes the foot lowest in the creature's own
/// frame as the planted one and measures how fast that foot moves over the ground while the body
/// moves. A foot that's really on the ground doesn't move; one that slides shows a gait clip playing
/// at the wrong rate for the body's speed.
/// </summary>
public partial class FootSlipMeter : Node
{
    /// <summary>
    /// The model's foot bones. Empty: the wolf model's paws if it has them, else every bone named
    /// "foot", else the lowest end bones of the rest pose (a spider's leg tips).
    /// </summary>
    public string[] Feet { get; set; } = System.Array.Empty<string>();
    /// <summary>A foot counts as on the ground within this height of the lowest any foot has been (m).</summary>
    public float GroundTolerance { get; set; } = 0.03f;
    /// <summary>Measure this skeleton (its node's parent's name, e.g. "Rig" for a humanoid's hidden driver) instead of the shown model.</summary>
    public string SkeletonOwner { get; set; } = "";
    /// <summary>Only frames where the body moves faster than this count (m/s).</summary>
    public float MovingAbove { get; set; } = 1.0f;
    public FootSlipStats Stats { get; set; } = new();

    private Skeleton3D? _skeleton;
    private int[] _bones = System.Array.Empty<int>();
    private Vector3[] _previous = System.Array.Empty<Vector3>();
    private Vector3 _previousBody;
    private bool _primed;
    private float _groundY = float.MaxValue;
    private float _previousSpeed;
    /// <summary>A model posed by a RigFollower (humanoid enemies): its pose is read from the follower's record.</summary>
    private DownhillRoguelite.Player.RigFollower? _follower;

    public override void _PhysicsProcess(double delta)
    {
        if (GetParent() is not Node3D body || delta <= 0.0)
        {
            return;
        }
        if (_skeleton == null || !IsInstanceValid(_skeleton))
        {
            // The visible model's skeleton (a humanoid enemy also carries a hidden driver rig).
            _skeleton = null;
            foreach (Node node in body.FindChildren("*", "Skeleton3D", true, false))
            {
                if (node is not Skeleton3D candidate)
                {
                    continue;
                }
                if (SkeletonOwner.Length > 0 ? candidate.GetParent()?.Name == SkeletonOwner : candidate.IsVisibleInTree())
                {
                    _skeleton = candidate;
                }
            }
            if (_skeleton == null)
            {
                return;
            }
            _bones = FindFeet(_skeleton, Feet);
            // A modifier's pose only reaches skinning; the follower records it for tests.
            _follower = null;
            foreach (Node child in _skeleton.GetChildren())
            {
                if (child is DownhillRoguelite.Player.RigFollower follower)
                {
                    _follower = follower;
                    follower.RecordPose = true;
                }
            }
            _previous = new Vector3[_bones.Length];
            _primed = false;
            _groundY = float.MaxValue;
            FootNames = string.Join(", ", System.Array.ConvertAll(_bones, b => _skeleton.GetBoneName(b)));
            Stats.Feet = FootNames;
        }
        float dt = (float)delta;
        Transform3D toBody = body.GlobalTransform.AffineInverse();
        Vector3[] now = new Vector3[_bones.Length];
        int lowest = -1;
        float lowestY = float.MaxValue;
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] < 0)
            {
                continue;
            }
            Vector3 local = _follower != null && _bones[i] < _follower.TargetPose.Length
                ? _follower.TargetPose[_bones[i]]
                : _skeleton.GetBoneGlobalPose(_bones[i]).Origin;
            now[i] = _skeleton.GlobalTransform * local;
            float y = (toBody * now[i]).Y;
            _groundY = Mathf.Min(_groundY, y);
            if (y < lowestY)
            {
                lowestY = y;
                lowest = i;
            }
        }
        Vector3 bodyNow = body.GlobalPosition;
        float speedNow = _primed ? Flat(bodyNow - _previousBody) / dt : 0.0f;
        // A frame with every foot in the air (a gallop's suspension, a jump) has no planted foot.
        if (_primed && lowest >= 0 && lowestY <= _groundY + GroundTolerance)
        {
            float bodySpeed = Flat(bodyNow - _previousBody) / dt;
            // Faster than anything runs is a test (or the game) placing the body: not a step. Only a
            // steady pace counts: while it speeds up or brakes the clip's rate trails a moment behind.
            bool steady = Mathf.Abs(bodySpeed - _previousSpeed) < 0.08f * Mathf.Max(bodySpeed, 1.0f);
            if (bodySpeed > MovingAbove && bodySpeed < 15.0f && steady)
            {
                Stats.SlipSum += Flat(now[lowest] - _previous[lowest]) / dt;
                Stats.SpeedSum += bodySpeed;
                Stats.Samples++;
            }
        }
        _previous = now;
        _previousBody = bodyNow;
        _previousSpeed = speedNow;
        _primed = true;
    }

    /// <summary>The feet this meter watches, for the report.</summary>
    public string FootNames { get; private set; } = "";

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    private static int[] FindFeet(Skeleton3D skeleton, string[] named)
    {
        System.Collections.Generic.List<int> feet = new();
        string[] wolf = { "Bone.010", "Bone.013", "Bone.016", "Bone.019" };
        foreach (string name in named.Length > 0 ? named : wolf)
        {
            int bone = skeleton.FindBone(name);
            if (bone >= 0)
            {
                feet.Add(bone);
            }
        }
        if (feet.Count >= 2)
        {
            return feet.ToArray();
        }
        feet.Clear();
        // Toes are where a walking or running biped touches the ground (the ankle lifts early).
        foreach (string part in new[] { "toe", "foot" })
        {
            for (int i = 0; i < skeleton.GetBoneCount(); i++)
            {
                string bone = skeleton.GetBoneName(i).ToLowerInvariant();
                if (bone.Contains(part) && !bone.Contains("end") && !bone.Contains("ik"))
                {
                    feet.Add(i);
                }
            }
            if (feet.Count >= 2)
            {
                break;
            }
            feet.Clear();
        }
        if (feet.Count >= 2)
        {
            return feet.ToArray();
        }
        // End bones (no children) lowest in the rest pose: the leg tips.
        feet.Clear();
        System.Collections.Generic.List<(float y, int bone)> ends = new();
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            if (skeleton.GetBoneChildren(i).Length == 0)
            {
                ends.Add((skeleton.GetBoneGlobalRest(i).Origin.Y, i));
            }
        }
        ends.Sort((a, b) => a.y.CompareTo(b.y));
        float floor = ends.Count > 0 ? ends[0].y : 0.0f;
        float height = 0.0f;
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            height = Mathf.Max(height, skeleton.GetBoneGlobalRest(i).Origin.Y - floor);
        }
        foreach ((float y, int bone) in ends)
        {
            if (y <= floor + height * 0.08f)
            {
                feet.Add(bone);
            }
        }
        return feet.ToArray();
    }
}
