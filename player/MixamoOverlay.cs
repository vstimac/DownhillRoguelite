using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Lays a Mixamo animation over the carrier's driving skeleton (the mannequin rig): a hidden Mixamo
/// skeleton (SwordAnimator) plays the clip, and this modifier turns each mapped driver bone toward
/// the Mixamo bone's rotation away from its rest (both rigs rest in a T-pose), carried into the
/// driver's skeleton space. Two weights: the upper body (spine, neck, head, arms, hands, fingers)
/// and the legs, so a slash plays over walking while a kick takes the legs too. It sits right after
/// the animation, before the spine lean, foot IK and body dynamics, which still act on top; the
/// shown character follows the driver as usual (RigFollower), so every carrier model gets it.
/// </summary>
public partial class MixamoOverlay : SkeletonModifier3D
{
    /// <summary>The hidden Mixamo skeleton playing the clip.</summary>
    [Export] public Skeleton3D? Source { get; set; }
    /// <summary>How much of the clip shows on the upper body and on the legs (0–1).</summary>
    [Export(PropertyHint.Range, "0,1")] public float UpperWeight { get; set; }
    [Export(PropertyHint.Range, "0,1")] public float LegWeight { get; set; }
    /// <summary>Driver bones counted as legs (the rest of the mapped bones, but the hips, are the upper body).</summary>
    [Export] public string[] LegBones { get; set; } =
    {
        "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L", "DEF-toe.L", "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R", "DEF-toe.R",
    };
    /// <summary>The hips are never turned (the body's facing stays the player's).</summary>
    [Export] public string HipsBone { get; set; } = "DEF-hips";

    public bool IsSetUp => _ready;
    public int Mapped => _pairs.Count;

    private readonly List<(int Driver, int Source, bool Leg)> _pairs = new();
    private Quaternion[] _driverRest = System.Array.Empty<Quaternion>();
    private Quaternion[] _sourceRest = System.Array.Empty<Quaternion>();
    /// <summary>Turns a direction in the Mixamo skeleton's space into the driver's.</summary>
    private Quaternion _space = Quaternion.Identity;
    private bool _ready;

    /// <summary>Matches the bones (the driver is this modifier's skeleton) once the source is set.</summary>
    public bool Setup()
    {
        _ready = false;
        _pairs.Clear();
        Skeleton3D? driver = GetSkeleton();
        if (driver == null || Source == null)
        {
            return false;
        }
        string[] pairs = RigFollower.DefaultPairs();
        HashSet<string> legs = new(LegBones);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            string driverName = pairs[i + 1];
            if (driverName == HipsBone)
            {
                continue;
            }
            int d = driver.FindBone(driverName);
            int s = RigFollower.FindBone(Source, pairs[i]);
            if (d >= 0 && s >= 0)
            {
                _pairs.Add((d, s, legs.Contains(driverName)));
            }
        }
        // Parents before children, so each bone's parent is posed before it.
        _pairs.Sort((a, b) => a.Driver.CompareTo(b.Driver));
        _driverRest = new Quaternion[driver.GetBoneCount()];
        for (int i = 0; i < _driverRest.Length; i++)
        {
            _driverRest[i] = driver.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
        _sourceRest = new Quaternion[Source.GetBoneCount()];
        for (int i = 0; i < _sourceRest.Length; i++)
        {
            _sourceRest[i] = Source.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
        Basis? driverFrame = BodyFrame(driver, "DEF-hips", "DEF-head", "DEF-thigh.L", "DEF-thigh.R");
        Basis? sourceFrame = BodyFrame(Source, "mixamorig_Hips", "mixamorig_Head", "mixamorig_LeftUpLeg", "mixamorig_RightUpLeg");
        if (driverFrame == null || sourceFrame == null || _pairs.Count == 0)
        {
            GD.PushWarning("MixamoOverlay: the rigs' hips, head or legs were not found.");
            return false;
        }
        _space = (driverFrame.Value * sourceFrame.Value.Transposed()).GetRotationQuaternion();
        _ready = true;
        return true;
    }

    /// <summary>
    /// The body's own frame in a skeleton's space, from its rest: sideways (left leg to right), up
    /// (hips to head) and their cross. Built the same way on both rigs, so it carries one onto the
    /// other whatever the import turned or scaled.
    /// </summary>
    public static Basis? BodyFrame(Skeleton3D skeleton, string hips, string head, string left, string right)
    {
        int h = RigFollower.FindBone(skeleton, hips);
        int top = RigFollower.FindBone(skeleton, head);
        int l = RigFollower.FindBone(skeleton, left);
        int r = RigFollower.FindBone(skeleton, right);
        if (h < 0 || top < 0 || l < 0 || r < 0)
        {
            return null;
        }
        Vector3 up = (skeleton.GetBoneGlobalRest(top).Origin - skeleton.GetBoneGlobalRest(h).Origin).Normalized();
        Vector3 side = skeleton.GetBoneGlobalRest(r).Origin - skeleton.GetBoneGlobalRest(l).Origin;
        side = (side - up * side.Dot(up)).Normalized();
        Vector3 third = side.Cross(up).Normalized();
        return new Basis(side, up, third);
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? driver = GetSkeleton();
        if (!_ready || driver == null || Source == null || !IsInstanceValid(Source))
        {
            return;
        }
        if (UpperWeight <= 0.001f && LegWeight <= 0.001f)
        {
            return;
        }
        Quaternion inverseSpace = _space.Inverse();
        foreach ((int d, int s, bool leg) in _pairs)
        {
            float weight = UpperWeight;
            if (leg)
            {
                weight = LegWeight;
            }
            if (weight <= 0.001f)
            {
                continue;
            }
            Quaternion away = _space * Source.GetBoneGlobalPose(s).Basis.GetRotationQuaternion() * _sourceRest[s].Inverse() * inverseSpace;
            Quaternion wanted = (away * _driverRest[d]).Normalized();
            Quaternion now = driver.GetBoneGlobalPose(d).Basis.GetRotationQuaternion();
            Quaternion blended = now.Slerp(wanted, Mathf.Clamp(weight, 0.0f, 1.0f)).Normalized();
            int parent = driver.GetBoneParent(d);
            Quaternion parentGlobal = Quaternion.Identity;
            if (parent >= 0)
            {
                parentGlobal = driver.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion();
            }
            driver.SetBonePoseRotation(d, (parentGlobal.Inverse() * blended).Normalized());
        }
    }
}
