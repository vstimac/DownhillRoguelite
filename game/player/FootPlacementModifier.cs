using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// First modifier in the leg chain: hands the animated pose to FootIK, which plants the feet,
/// lowers the hips and moves the leg IK targets before the TwoBoneIK3D nodes run.
/// </summary>
public partial class FootPlacementModifier : SkeletonModifier3D
{
    public FootIK? Rig { get; set; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (Rig != null && skeleton != null)
        {
            Rig.BeforeLegIK(skeleton, (float)delta);
        }
    }
}
