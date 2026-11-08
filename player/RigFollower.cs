using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Makes a character model follow the carrier's driving skeleton bone for bone. The driving
/// skeleton (the Quaternius mannequin rig) keeps doing all the work: animations, foot IK, the spine's
/// balance lean and the ragdoll. This modifier sits on the shown character's skeleton (the Mixamo
/// carrier) and each frame copies the driver's final pose onto it: every mapped bone takes the
/// driver's rotation away from its rest pose, applied to its own rest pose (both rigs rest in a
/// T-pose), and the hips also follow the driver's position, scaled by the two rigs' hip heights.
/// The driver's own meshes are hidden. Unmapped bones keep their rest pose.
/// </summary>
public partial class RigFollower : SkeletonModifier3D
{
    /// <summary>The skeleton to follow (the mannequin rig that animations, IK and ragdoll drive).</summary>
    [Export] public Skeleton3D? Driver { get; set; }
    /// <summary>Hide the driver's meshes, so only this character is seen.</summary>
    [Export] public bool HideDriverMeshes { get; set; } = true;
    /// <summary>Pairs of bone names: this skeleton's bone, then the driver's bone it follows.</summary>
    [Export] public string[] BonePairs { get; set; } = DefaultPairs();
    [Export] public string HipsBone { get; set; } = "mixamorig1_Hips";
    [Export] public string DriverHipsBone { get; set; } = "DEF-hips";
    /// <summary>Turn each limb's rest onto the mannequin's (for models not resting in its T-pose).</summary>
    [Export] public bool AlignRestPose { get; set; } = true;
    /// <summary>
    /// The bones whose rest is aligned: the long limb bones, where A-pose and T-pose models differ.
    /// Clavicles, spine and fingers keep their own rest (their angles differ between rigs only because
    /// the joints sit in different places, and turning them would hunch the shoulders).
    /// </summary>
    [Export] public string[] AlignBones { get; set; } =
    {
        "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg",
    };

    private readonly List<(int Target, int Source)> _pairs = new();
    private Quaternion[] _sourceRest = System.Array.Empty<Quaternion>();
    /// <summary>
    /// The driver's final pose (after its own modifiers: foot IK, spine lean, body dynamics), taken on
    /// its SkeletonUpdated. A modifier's result can't be read back later in the frame, so reading the
    /// driver directly would copy the bare animation without the procedural layers.
    /// </summary>
    private Quaternion[] _driverRotations = System.Array.Empty<Quaternion>();
    private Vector3 _driverHipsNow;
    private bool _driverPosed;
    private Quaternion[] _targetRest = System.Array.Empty<Quaternion>();
    /// <summary>Per target bone: turns its rest so it points where the driver's bone points at rest (T-pose vs A-pose).</summary>
    private Quaternion[] _align = System.Array.Empty<Quaternion>();
    private Quaternion _space = Quaternion.Identity;
    private int _hips = -1;
    private int _driverHips = -1;
    private Vector3 _sourceHipsRest;
    private Vector3 _targetHipsRest;
    private float _hipsScale = 1.0f;
    private bool _ready;

    public int Followed => _pairs.Count;
    /// <summary>
    /// Tests: keep each frame's resulting bone origins (skeleton space), since a modifier's pose is
    /// only used for skinning and can't be read back from the skeleton afterwards. Driver origins too.
    /// </summary>
    public bool RecordPose { get; set; }
    public Vector3[] TargetPose { get; private set; } = System.Array.Empty<Vector3>();
    public Vector3[] DriverPose { get; private set; } = System.Array.Empty<Vector3>();

    /// <summary>Mixamo bones and the mannequin (Rigify) bones they follow.</summary>
    public static string[] DefaultPairs()
    {
        List<string> pairs = new()
        {
            "Hips", "hips", "Spine", "spine.001", "Spine1", "spine.002", "Spine2", "spine.003", "Neck", "neck", "Head", "head",
        };
        foreach ((string side, string s) in new[] { ("Left", "L"), ("Right", "R") })
        {
            pairs.AddRange(new[]
            {
                $"{side}Shoulder", $"shoulder.{s}", $"{side}Arm", $"upper_arm.{s}", $"{side}ForeArm", $"forearm.{s}", $"{side}Hand", $"hand.{s}",
                $"{side}UpLeg", $"thigh.{s}", $"{side}Leg", $"shin.{s}", $"{side}Foot", $"foot.{s}", $"{side}ToeBase", $"toe.{s}",
            });
            foreach ((string finger, string f) in new[] { ("Index", "f_index"), ("Middle", "f_middle"), ("Ring", "f_ring"), ("Pinky", "f_pinky") })
            {
                for (int k = 1; k <= 3; k++)
                {
                    pairs.Add($"{side}Hand{finger}{k}");
                    pairs.Add($"{f}.0{k}.{s}");
                }
            }
            for (int k = 1; k <= 3; k++)
            {
                pairs.Add($"{side}HandThumb{k}");
                pairs.Add($"thumb.0{k}.{s}");
            }
        }
        List<string> named = new();
        for (int i = 0; i < pairs.Count; i += 2)
        {
            named.Add("mixamorig1_" + pairs[i]);
            named.Add("DEF-" + pairs[i + 1]);
        }
        return named.ToArray();
    }

    public override void _Ready()
    {
        CallDeferred(MethodName.Setup);
    }

    /// <summary>Matches bones and records both rigs' rest rotations (in each skeleton's space).</summary>
    private void Setup()
    {
        Skeleton3D? target = GetSkeleton();
        if (target == null || Driver == null)
        {
            GD.PushWarning("RigFollower: no skeleton or driver.");
            return;
        }
        _pairs.Clear();
        for (int i = 0; i + 1 < BonePairs.Length; i += 2)
        {
            int t = FindBone(target, BonePairs[i]);
            int s = Driver.FindBone(BonePairs[i + 1]);
            if (t >= 0 && s >= 0)
            {
                _pairs.Add((t, s));
            }
        }
        // Parents before children, so each bone's parent pose is set before it.
        _pairs.Sort((a, b) => a.Target.CompareTo(b.Target));
        _driverRotations = new Quaternion[Driver.GetBoneCount()];
        Driver.SkeletonUpdated += OnDriverUpdated;
        _sourceRest = new Quaternion[Driver.GetBoneCount()];
        for (int i = 0; i < _sourceRest.Length; i++)
        {
            _sourceRest[i] = Driver.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
        _targetRest = new Quaternion[target.GetBoneCount()];
        for (int i = 0; i < _targetRest.Length; i++)
        {
            _targetRest[i] = target.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
        // The driver's skeleton space seen from this one (constant: both ride on the same body).
        _space = (target.GlobalBasis.Orthonormalized().Inverse() * Driver.GlobalBasis.Orthonormalized()).GetRotationQuaternion();
        AlignRests(target);
        _hips = FindBone(target, HipsBone);
        _driverHips = Driver.FindBone(DriverHipsBone);
        if (_hips >= 0 && _driverHips >= 0)
        {
            _sourceHipsRest = Driver.GetBoneGlobalRest(_driverHips).Origin;
            _targetHipsRest = target.GetBoneGlobalRest(_hips).Origin;
            _hipsScale = _targetHipsRest.Y / Mathf.Max(_sourceHipsRest.Y, 0.01f);
        }
        if (HideDriverMeshes)
        {
            foreach (Node node in Driver.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is MeshInstance3D mesh)
                {
                    mesh.Visible = false;
                }
            }
        }
        _ready = true;
    }

    /// <summary>
    /// Models don't all rest in the mannequin's T-pose (an A-pose has the arms down at 45°). For each
    /// followed bone with exactly one followed child, the shortest turn that makes its rest direction
    /// (bone to child) match the driver's is recorded and applied under the driver's motion, so arms
    /// and legs end up where the mannequin's are. Rigs that already match get (almost) no turn.
    /// </summary>
    private void AlignRests(Skeleton3D target)
    {
        _align = new Quaternion[target.GetBoneCount()];
        for (int i = 0; i < _align.Length; i++)
        {
            _align[i] = Quaternion.Identity;
        }
        if (!AlignRestPose)
        {
            return;
        }
        foreach ((int t, int s) in _pairs)
        {
            if (!Listed(target.GetBoneName(t)))
            {
                continue;
            }
            int childT = -1;
            int childS = -1;
            int children = 0;
            foreach ((int t2, int s2) in _pairs)
            {
                if (target.GetBoneParent(t2) == t)
                {
                    childT = t2;
                    childS = s2;
                    children++;
                }
            }
            if (children != 1)
            {
                continue;
            }
            Vector3 own = target.GetBoneGlobalRest(childT).Origin - target.GetBoneGlobalRest(t).Origin;
            Vector3 driver = _space * (Driver!.GetBoneGlobalRest(childS).Origin - Driver.GetBoneGlobalRest(s).Origin);
            if (own.LengthSquared() < 1e-10f || driver.LengthSquared() < 1e-10f)
            {
                continue;
            }
            own = own.Normalized();
            driver = driver.Normalized();
            if (own.Dot(driver) < -0.99f)
            {
                continue;
            }
            _align[t] = new Quaternion(own, driver).Normalized();
        }
    }

    private bool Listed(string bone)
    {
        foreach (string name in AlignBones)
        {
            if (bone == name || bone.EndsWith("_" + name) || bone.EndsWith(":" + name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The bones whose rest is turned more than <paramref name="degrees"/>, with the turn (diagnostics and tests).</summary>
    public string AlignedBones(float degrees)
    {
        Skeleton3D? target = GetSkeleton();
        System.Text.StringBuilder list = new();
        for (int i = 0; target != null && i < _align.Length; i++)
        {
            float angle = Mathf.RadToDeg(_align[i].GetAngle());
            if (angle > degrees)
            {
                list.Append($"{target.GetBoneName(i)} {angle:0}°, ");
            }
        }
        return list.ToString().TrimEnd(',', ' ');
    }

    /// <summary>Largest rest correction applied (degrees): near 0 for a model in the same rest pose as the mannequin.</summary>
    public float LargestAlignDegrees
    {
        get
        {
            float largest = 0.0f;
            foreach (Quaternion q in _align)
            {
                largest = Mathf.Max(largest, Mathf.RadToDeg(q.GetAngle()));
            }
            return largest;
        }
    }

    /// <summary>
    /// A bone by its exact name, or else by the part after the Mixamo prefix, so any prefix works
    /// (mixamorig1_Hips, mixamorig_Hips from "mixamorig:Hips", or plain Hips).
    /// </summary>
    public static int FindBone(Skeleton3D skeleton, string name)
    {
        int exact = skeleton.FindBone(name);
        if (exact >= 0)
        {
            return exact;
        }
        string bare = name.Substring(name.IndexOf('_') + 1);
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string bone = skeleton.GetBoneName(i);
            if (bone == bare || bone.EndsWith("_" + bare) || bone.EndsWith(":" + bare))
            {
                return i;
            }
        }
        return -1;
    }

    public override void _ExitTree()
    {
        if (Driver != null && _driverRotations.Length > 0)
        {
            Driver.SkeletonUpdated -= OnDriverUpdated;
        }
    }

    private void OnDriverUpdated()
    {
        if (Driver == null)
        {
            return;
        }
        for (int i = 0; i < _driverRotations.Length; i++)
        {
            _driverRotations[i] = Driver.GetBoneGlobalPose(i).Basis.GetRotationQuaternion();
        }
        if (_driverHips >= 0)
        {
            _driverHipsNow = Driver.GetBoneGlobalPose(_driverHips).Origin;
        }
        _driverPosed = true;
        if (RecordPose)
        {
            Vector3[] origins = new Vector3[_driverRotations.Length];
            for (int i = 0; i < origins.Length; i++)
            {
                origins[i] = Driver.GetBoneGlobalPose(i).Origin;
            }
            DriverPose = origins;
        }
    }

    private Quaternion DriverRotation(int bone)
    {
        if (_driverPosed && bone < _driverRotations.Length)
        {
            return _driverRotations[bone];
        }
        return Driver!.GetBoneGlobalPose(bone).Basis.GetRotationQuaternion();
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? target = GetSkeleton();
        if (!_ready || target == null || Driver == null)
        {
            return;
        }
        Quaternion inverseSpace = _space.Inverse();
        foreach ((int t, int s) in _pairs)
        {
            // The driver's rotation away from its rest, carried into this skeleton's space and onto this bone's rest.
            Quaternion driverNow = DriverRotation(s);
            Quaternion away = _space * driverNow * _sourceRest[s].Inverse() * inverseSpace;
            Quaternion wanted = (away * _align[t] * _targetRest[t]).Normalized();
            int parent = target.GetBoneParent(t);
            Quaternion parentGlobal = Quaternion.Identity;
            if (parent >= 0)
            {
                parentGlobal = target.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion();
            }
            target.SetBonePoseRotation(t, (parentGlobal.Inverse() * wanted).Normalized());
            if (t == _hips && _driverHips >= 0)
            {
                Vector3 hipsNow = Driver.GetBoneGlobalPose(_driverHips).Origin;
                if (_driverPosed)
                {
                    hipsNow = _driverHipsNow;
                }
                Vector3 moved = hipsNow - _sourceHipsRest;
                Vector3 hipsGlobal = _targetHipsRest + _space * moved * _hipsScale;
                Vector3 parentOrigin = Vector3.Zero;
                Basis parentBasis = Basis.Identity;
                if (parent >= 0)
                {
                    Transform3D parentPose = target.GetBoneGlobalPose(parent);
                    parentOrigin = parentPose.Origin;
                    parentBasis = parentPose.Basis;
                }
                target.SetBonePosePosition(t, parentBasis.Inverse() * (hipsGlobal - parentOrigin));
            }
        }
        if (RecordPose)
        {
            TargetPose = new Vector3[target.GetBoneCount()];
            for (int i = 0; i < TargetPose.Length; i++)
            {
                TargetPose[i] = target.GetBoneGlobalPose(i).Origin;
            }
        }
    }
}
