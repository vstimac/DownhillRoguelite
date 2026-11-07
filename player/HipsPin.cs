using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// While an animation with its travel baked into the hips plays (the Jump Over vault), keeps the hips
/// over the body: their sideways and forward position goes back to rest, the height stays. The
/// body itself moves by that travel instead (Vault), so the character never leaves its collider.
/// </summary>
public partial class HipsPin : SkeletonModifier3D
{
    [Export] public string HipsBone { get; set; } = "mixamorig1_Hips";

    private int _hips = -1;
    private Vector3 _rest;

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null)
        {
            return;
        }
        if (_hips < 0)
        {
            _hips = skeleton.FindBone(HipsBone);
            if (_hips < 0)
            {
                return;
            }
            _rest = skeleton.GetBoneRest(_hips).Origin;
        }
        Vector3 pose = skeleton.GetBonePosePosition(_hips);
        skeleton.SetBonePosePosition(_hips, new Vector3(_rest.X, pose.Y, _rest.Z));
    }
}
