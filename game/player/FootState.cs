using Godot;

namespace DownhillRoguelite.Player;

/// <summary>Per-leg bones, IK targets and planting state owned by FootIK.</summary>
public sealed class FootState
{
    public FootState(string side, string thighBone, string shinBone, string footBone)
    {
        Side = side;
        ThighBone = thighBone;
        ShinBone = shinBone;
        FootBone = footBone;
    }

    public string Side { get; }
    public string ThighBone { get; }
    public string ShinBone { get; }
    public string FootBone { get; }

    public int ShinIndex { get; set; } = -1;
    public int FootIndex { get; set; } = -1;
    public int ThighIndex { get; set; } = -1;
    /// <summary>Ankle height above the sole in the rest pose, in m.</summary>
    public float RestHeight { get; set; }
    /// <summary>Thigh plus shin length in the rest pose, in m.</summary>
    public float LegLength { get; set; }
    /// <summary>Set when a lock was dropped because the leg couldn't reach; cleared once the animation lifts the foot.</summary>
    public bool WaitForLift { get; set; }

    /// <summary>How much of the leg IK applies this frame (0 = the animation as it is).</summary>
    public float LegWeight { get; set; }
    /// <summary>Knee pole in world space: the knee bends toward it.</summary>
    public Vector3 PolePosition { get; set; }

    /// <summary>Animated ankle transform in world space, before any IK.</summary>
    public Transform3D AnimatedTransform { get; set; } = Transform3D.Identity;
    /// <summary>Animated foot orientation in skeleton space, before any IK.</summary>
    public Basis AnimatedSkeletonBasis { get; set; } = Basis.Identity;
    /// <summary>Animated ankle height above the body's origin (the sole plane).</summary>
    public float AnimatedHeight { get; set; }
    public Vector3 PreviousAnimatedPosition { get; set; }
    public bool HasPrevious { get; set; }

    public bool Planted { get; set; }
    /// <summary>
    /// The foot came down and is still on the ground: set when it plants, cleared when the animation
    /// lifts it. Unlike Planted it holds when the lock is dropped because the leg can't reach.
    /// </summary>
    public bool Down { get; set; }
    /// <summary>0 = follow the animation, 1 = locked to LockPosition.</summary>
    public float PlantWeight { get; set; }
    public Vector3 LockPosition { get; set; }

    public bool HasGround { get; set; }
    public float GroundWeight { get; set; }
    public Vector3 GroundPoint { get; set; }
    public Vector3 GroundNormal { get; set; } = Vector3.Up;
    public Vector3 Landing { get; set; }
    public Vector3 TargetPosition { get; set; }
    /// <summary>How far the IK target sits above (+) or below (-) the animated ankle.</summary>
    public float HeightDelta { get; set; }
}
