using Godot;

namespace DownhillRoguelite.Player;

/// <summary>Last modifier in the leg chain: after the leg IK, FootIK tilts each foot to the ground.</summary>
public partial class FootAlignModifier : SkeletonModifier3D
{
    public FootIK? Rig { get; set; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (Rig != null && skeleton != null)
        {
            Rig.AfterLegIK(skeleton);
        }
    }
}
