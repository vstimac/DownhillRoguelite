using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Bends the spine by the balance tilt so the carrier visibly sways and counter-leans. Reads
/// BalanceSnapshot events; fades out while fallen, where the fall animation takes over.
/// </summary>
public partial class SpineLean : SkeletonModifier3D
{
    [Export] public string[] SpineBones { get; set; } = { "DEF-spine.001", "DEF-spine.002", "DEF-spine.003" };
    /// <summary>Share of the tilt each spine bone takes, root to chest.</summary>
    [Export] public float[] BoneWeights { get; set; } = { 0.3f, 0.35f, 0.35f };
    /// <summary>Visual tilt as a multiple of the simulated tilt.</summary>
    [Export] public float LeanScale { get; set; } = 1.0f;
    [Export] public float Sharpness { get; set; } = 14.0f;

    private Vector2 _targetTilt;
    private Vector2 _tilt;
    private Vector3 _forward = Vector3.Forward;
    private Vector3 _right = Vector3.Right;
    private float _targetWeight = 1.0f;
    private float _weight = 1.0f;
    private int[] _boneIndices = System.Array.Empty<int>();

    public override void _Ready()
    {
        GameEvents.BalanceUpdated += OnBalanceUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.BalanceUpdated -= OnBalanceUpdated;
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        _targetTilt = snapshot.Tilt;
        _forward = snapshot.Forward;
        _right = snapshot.Right;
        _targetWeight = 1.0f;
        if (snapshot.State == BalanceState.Fallen)
        {
            _targetWeight = 0.0f;
        }
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null)
        {
            return;
        }
        if (_boneIndices.Length != SpineBones.Length)
        {
            _boneIndices = new int[SpineBones.Length];
            for (int i = 0; i < SpineBones.Length; i++)
            {
                _boneIndices[i] = skeleton.FindBone(SpineBones[i]);
            }
        }

        float blend = 1.0f - Mathf.Exp(-Sharpness * (float)delta);
        _tilt = _tilt.Lerp(_targetTilt, blend);
        _weight = Mathf.Lerp(_weight, _targetWeight, blend);

        Basis toSkeleton = skeleton.GetGlobalTransformInterpolated().Basis.Inverse();
        Vector3 forward = (toSkeleton * _forward).Normalized();
        Vector3 right = (toSkeleton * _right).Normalized();

        // Top-down, so each bone's rotation carries everything above it.
        for (int i = 0; i < _boneIndices.Length && i < BoneWeights.Length; i++)
        {
            int bone = _boneIndices[i];
            if (bone < 0)
            {
                continue;
            }

            // Rolling about forward tips the chest right; pitching about -right tips it forward.
            float share = BoneWeights[i] * LeanScale * _weight;
            Quaternion lean = new Quaternion(forward, _tilt.X * share) * new Quaternion(right, -_tilt.Y * share);
            Transform3D pose = skeleton.GetBoneGlobalPose(bone);
            skeleton.SetBoneGlobalPose(bone, new Transform3D(new Basis(lean) * pose.Basis, pose.Origin));
        }
    }
}
