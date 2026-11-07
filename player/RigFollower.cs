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

    private readonly List<(int Target, int Source)> _pairs = new();
    private Quaternion[] _sourceRest = System.Array.Empty<Quaternion>();
    private Quaternion[] _targetRest = System.Array.Empty<Quaternion>();
    private Quaternion _space = Quaternion.Identity;
    private int _hips = -1;
    private int _driverHips = -1;
    private Vector3 _sourceHipsRest;
    private Vector3 _targetHipsRest;
    private float _hipsScale = 1.0f;
    private bool _ready;

    public int Followed => _pairs.Count;

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
            int t = target.FindBone(BonePairs[i]);
            int s = Driver.FindBone(BonePairs[i + 1]);
            if (t >= 0 && s >= 0)
            {
                _pairs.Add((t, s));
            }
        }
        // Parents before children, so each bone's parent pose is set before it.
        _pairs.Sort((a, b) => a.Target.CompareTo(b.Target));
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
        _hips = target.FindBone(HipsBone);
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
            Quaternion driverNow = Driver.GetBoneGlobalPose(s).Basis.GetRotationQuaternion();
            Quaternion away = _space * driverNow * _sourceRest[s].Inverse() * inverseSpace;
            Quaternion wanted = (away * _targetRest[t]).Normalized();
            int parent = target.GetBoneParent(t);
            Quaternion parentGlobal = Quaternion.Identity;
            if (parent >= 0)
            {
                parentGlobal = target.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion();
            }
            target.SetBonePoseRotation(t, (parentGlobal.Inverse() * wanted).Normalized());
            if (t == _hips && _driverHips >= 0)
            {
                Vector3 moved = Driver.GetBoneGlobalPose(_driverHips).Origin - _sourceHipsRest;
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
    }
}
