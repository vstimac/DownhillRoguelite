using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Procedural foot placement on top of the animation. At startup it adds a modifier chain under the
/// skeleton: FootPlacementModifier, one TwoBoneIK3D per leg, then FootAlignModifier. Each frame it
/// detects which feet the animation has planted and locks them in place, raycasts the ground under
/// each foot (and ahead of a swinging foot, so it lifts onto steps), lowers the hips so the lower
/// foot can reach, and tilts planted feet to the surface. IK blends out when airborne or sliding.
/// </summary>
public partial class FootIK : Node3D
{
    [Export] public PlayerController? Body { get; set; }
    [Export] public Skeleton3D? Skeleton { get; set; }

    [ExportGroup("Bones")]
    [Export] public string HipsBone { get; set; } = "DEF-hips";
    [Export] public string LeftThighBone { get; set; } = "DEF-thigh.L";
    [Export] public string LeftShinBone { get; set; } = "DEF-shin.L";
    [Export] public string LeftFootBone { get; set; } = "DEF-foot.L";
    [Export] public string RightThighBone { get; set; } = "DEF-thigh.R";
    [Export] public string RightShinBone { get; set; } = "DEF-shin.R";
    [Export] public string RightFootBone { get; set; } = "DEF-foot.R";

    [ExportGroup("Planting")]
    /// <summary>An animated ankle lower than this above the sole plane can plant, in m.</summary>
    [Export] public float PlantHeight { get; set; } = 0.14f;
    /// <summary>An animated ankle slower than this in world space can plant, in m/s.</summary>
    [Export] public float PlantSpeed { get; set; } = 0.6f;
    /// <summary>A locked foot lets go when the animation pulls it further than this, in m.</summary>
    [Export] public float MaxLockDistance { get; set; } = 0.3f;
    [Export] public float PlantBlendTime { get; set; } = 0.08f;

    [ExportGroup("Ground")]
    /// <summary>Ground rays start this far above the body's origin, in m.</summary>
    [Export] public float RayAbove { get; set; } = 0.6f;
    /// <summary>Ground rays end this far below the body's origin, in m.</summary>
    [Export] public float RayBelow { get; set; } = 0.8f;
    /// <summary>How far ahead (in seconds of body velocity) a swinging foot looks for its landing.</summary>
    [Export] public float StepLookAhead { get; set; } = 0.15f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Hips and blending")]
    [Export] public float MaxHipDrop { get; set; } = 0.45f;
    [Export] public float HipSharpness { get; set; } = 14.0f;
    /// <summary>Time to blend IK fully in or out, in s.</summary>
    [Export] public float IkBlendTime { get; set; } = 0.15f;
    /// <summary>Feet stop tilting to the ground once lifted this far above PlantHeight, in m.</summary>
    [Export] public float AlignFadeHeight { get; set; } = 0.15f;
    /// <summary>Distance in front of each knee where its pole target sits, in m.</summary>
    [Export] public float PoleDistance { get; set; } = 1.0f;
    /// <summary>A body jump larger than this in one frame resets planted feet, in m.</summary>
    [Export] public float TeleportDistance { get; set; } = 1.0f;
    /// <summary>A planted foot lets go when its target is further from the hip than this fraction of the leg.</summary>
    [Export] public float MaxReachFraction { get; set; } = 0.98f;

    public IReadOnlyList<FootState> Feet => _feet;
    public float IkWeight { get; private set; }
    public float HipOffset { get; private set; }

    private readonly List<FootState> _feet = new();
    private int _hipsIndex = -1;
    private Vector3 _lastOrigin;
    private bool _hasLastOrigin;
    private Godot.Collections.Array<Rid> _rayExclude = new();

    public override void _Ready()
    {
        if (Body == null || Skeleton == null)
        {
            GD.PushError("FootIK: Body and Skeleton must be assigned.");
            return;
        }

        _rayExclude = new Godot.Collections.Array<Rid> { Body.GetRid() };
        _hipsIndex = Skeleton.FindBone(HipsBone);
        _feet.Add(new FootState("L", LeftThighBone, LeftShinBone, LeftFootBone));
        _feet.Add(new FootState("R", RightThighBone, RightShinBone, RightFootBone));

        Skeleton.AddChild(new FootPlacementModifier { Name = "FootPlacement", Rig = this });
        foreach (FootState foot in _feet)
        {
            BuildLeg(Skeleton, foot);
        }
        Skeleton.AddChild(new FootAlignModifier { Name = "FootAlign", Rig = this });
    }

    private void BuildLeg(Skeleton3D skeleton, FootState foot)
    {
        foot.ThighIndex = skeleton.FindBone(foot.ThighBone);
        foot.ShinIndex = skeleton.FindBone(foot.ShinBone);
        foot.FootIndex = skeleton.FindBone(foot.FootBone);
        if (foot.ThighIndex < 0 || foot.ShinIndex < 0 || foot.FootIndex < 0)
        {
            GD.PushError($"FootIK: leg bones for side {foot.Side} not found in the skeleton.");
            return;
        }

        Vector3 thighRest = skeleton.GetBoneGlobalRest(foot.ThighIndex).Origin;
        Vector3 shinRest = skeleton.GetBoneGlobalRest(foot.ShinIndex).Origin;
        Vector3 footRest = skeleton.GetBoneGlobalRest(foot.FootIndex).Origin;
        foot.RestHeight = footRest.Y;
        foot.LegLength = thighRest.DistanceTo(shinRest) + shinRest.DistanceTo(footRest);

        foot.Target = CreateMarker($"FootTarget{foot.Side}");
        foot.Pole = CreateMarker($"KneePole{foot.Side}");

        TwoBoneIK3D ik = new() { Name = $"LegIK{foot.Side}", Influence = 0.0f };
        skeleton.AddChild(ik);
        ik.SettingCount = 1;
        ik.SetRootBoneName(0, foot.ThighBone);
        ik.SetMiddleBoneName(0, foot.ShinBone);
        ik.SetEndBoneName(0, foot.FootBone);
        ik.SetTargetNode(0, ik.GetPathTo(foot.Target));
        ik.SetPoleNode(0, ik.GetPathTo(foot.Pole));
        foot.Ik = ik;
    }

    private Marker3D CreateMarker(string name)
    {
        Marker3D marker = new()
        {
            Name = name,
            TopLevel = true,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(marker);
        return marker;
    }

    /// <summary>Called by FootPlacementModifier with the animated pose, before the leg IK runs.</summary>
    public void BeforeLegIK(Skeleton3D skeleton, float dt)
    {
        if (Body == null || _feet.Count == 0 || dt <= 0.0f)
        {
            return;
        }

        // The skeleton renders at the interpolated transform; plan in that space so feet don't jitter.
        Transform3D skeletonWorld = skeleton.GetGlobalTransformInterpolated();
        Vector3 origin = skeletonWorld.Origin;
        bool teleported = _hasLastOrigin && origin.DistanceTo(_lastOrigin) > TeleportDistance;
        _lastOrigin = origin;
        _hasLastOrigin = true;

        float targetWeight = 0.0f;
        if (Body.State == LocomotionState.Idle || Body.State == LocomotionState.Striding || Body.State == LocomotionState.Braking)
        {
            targetWeight = 1.0f;
        }
        IkWeight = Mathf.MoveToward(IkWeight, targetWeight, dt / IkBlendTime);

        PhysicsDirectSpaceState3D space = Body.GetWorld3D().DirectSpaceState;
        Vector3 bodyVelocity = Flat(Body.Velocity);
        float lowestDelta = 0.0f;

        foreach (FootState foot in _feet)
        {
            if (foot.FootIndex < 0)
            {
                continue;
            }

            Transform3D animatedLocal = skeleton.GetBoneGlobalPose(foot.FootIndex);
            Transform3D animated = skeletonWorld * animatedLocal;
            foot.AnimatedTransform = animated;
            foot.AnimatedSkeletonBasis = animatedLocal.Basis;
            foot.AnimatedHeight = animated.Origin.Y - origin.Y;

            Vector3 animatedVelocity = Vector3.Zero;
            if (foot.HasPrevious && !teleported)
            {
                animatedVelocity = (animated.Origin - foot.PreviousAnimatedPosition) / dt;
            }
            foot.PreviousAnimatedPosition = animated.Origin;
            foot.HasPrevious = true;

            if (teleported)
            {
                foot.Planted = false;
                foot.PlantWeight = 0.0f;
            }
            UpdatePlanting(foot, animated.Origin, animatedVelocity, dt);

            Vector3 locked = new(foot.LockPosition.X, animated.Origin.Y, foot.LockPosition.Z);
            Vector3 planar = animated.Origin.Lerp(locked, foot.PlantWeight);
            PlaceOnGround(space, foot, planar, origin.Y, bodyVelocity);

            float groundTarget = 0.0f;
            if (foot.HasGround)
            {
                groundTarget = 1.0f;
            }
            foot.GroundWeight = Mathf.MoveToward(foot.GroundWeight, groundTarget, dt / IkBlendTime);
            lowestDelta = Mathf.Min(lowestDelta, foot.HeightDelta * foot.GroundWeight * IkWeight);
        }

        float hipWeight = 1.0f - Mathf.Exp(-HipSharpness * dt);
        HipOffset = Mathf.Lerp(HipOffset, Mathf.Max(lowestDelta, -MaxHipDrop), hipWeight);
        if (_hipsIndex >= 0)
        {
            Transform3D hips = skeleton.GetBoneGlobalPose(_hipsIndex);
            hips.Origin += skeletonWorld.Basis.Inverse() * (Vector3.Up * HipOffset);
            skeleton.SetBoneGlobalPose(_hipsIndex, hips);
        }

        Vector3 forward = Flat(-Body.GetGlobalTransformInterpolated().Basis.Z);
        foreach (FootState foot in _feet)
        {
            if (foot.Ik == null || foot.Target == null || foot.Pole == null)
            {
                continue;
            }

            // A locked foot the leg can no longer reach would drag: let it go and wait for the next step.
            Vector3 hip = skeletonWorld * skeleton.GetBoneGlobalPose(foot.ThighIndex).Origin;
            if (foot.Planted && hip.DistanceTo(foot.TargetPosition) > foot.LegLength * MaxReachFraction)
            {
                foot.Planted = false;
                foot.WaitForLift = true;
            }

            foot.Target.GlobalTransform = new Transform3D(foot.AnimatedTransform.Basis, foot.TargetPosition);
            Vector3 knee = skeletonWorld * skeleton.GetBoneGlobalPose(foot.ShinIndex).Origin;
            foot.Pole.GlobalPosition = knee + forward * PoleDistance;
            foot.Ik.Influence = IkWeight * foot.GroundWeight;
        }

        Publish();
    }

    /// <summary>
    /// Called by FootAlignModifier after the leg IK. The IK keeps each foot's rotation relative to
    /// the shin, so bending the knee pitches the foot. Restore the animated foot orientation, then
    /// tilt low feet to the ground normal.
    /// </summary>
    public void AfterLegIK(Skeleton3D skeleton)
    {
        Basis toSkeleton = skeleton.GetGlobalTransformInterpolated().Basis.Inverse();
        Vector3 up = (toSkeleton * Vector3.Up).Normalized();

        foreach (FootState foot in _feet)
        {
            float legWeight = IkWeight * foot.GroundWeight;
            if (foot.FootIndex < 0 || legWeight <= 0.0f)
            {
                continue;
            }

            float lift = Mathf.Max(0.0f, foot.AnimatedHeight - PlantHeight);
            float align = legWeight * Mathf.Clamp(1.0f - lift / AlignFadeHeight, 0.0f, 1.0f);
            Vector3 normal = (toSkeleton * foot.GroundNormal).Normalized();
            Quaternion tilt = Quaternion.Identity.Slerp(new Quaternion(up, normal), align);
            Quaternion aligned = (tilt * foot.AnimatedSkeletonBasis.GetRotationQuaternion()).Normalized();

            Transform3D pose = skeleton.GetBoneGlobalPose(foot.FootIndex);
            Quaternion current = pose.Basis.GetRotationQuaternion();
            Basis final = new(current.Slerp(aligned, legWeight));
            skeleton.SetBoneGlobalPose(foot.FootIndex, new Transform3D(final, pose.Origin));
        }
    }

    private void UpdatePlanting(FootState foot, Vector3 animatedPosition, Vector3 animatedVelocity, float dt)
    {
        bool low = foot.AnimatedHeight < PlantHeight;
        bool still = Flat(animatedVelocity).Length() < PlantSpeed;
        if (!low)
        {
            foot.WaitForLift = false;
        }

        if (!foot.Planted)
        {
            if (low && still && !foot.WaitForLift)
            {
                foot.Planted = true;
                foot.LockPosition = animatedPosition;
            }
        }
        else
        {
            bool stretched = Flat(foot.LockPosition - animatedPosition).Length() > MaxLockDistance;
            if (!low || stretched)
            {
                foot.Planted = false;
            }
        }

        float plantTarget = 0.0f;
        if (foot.Planted)
        {
            plantTarget = 1.0f;
        }
        foot.PlantWeight = Mathf.MoveToward(foot.PlantWeight, plantTarget, dt / PlantBlendTime);
    }

    /// <summary>
    /// Finds the ground under the foot and sets its IK target: the ankle's rest height along the
    /// surface normal, plus any extra animated lift (heel-off, swing) straight up. Lifting along a
    /// tilted normal would slide planted feet sideways. A swinging foot also checks its landing
    /// point and lifts to it when higher.
    /// </summary>
    private void PlaceOnGround(PhysicsDirectSpaceState3D space, FootState foot, Vector3 planar, float originY, Vector3 bodyVelocity)
    {
        foot.HasGround = CastGround(space, planar, originY, out Vector3 point, out Vector3 normal);
        foot.Landing = planar + bodyVelocity * StepLookAhead;

        if (!foot.Planted && CastGround(space, foot.Landing, originY, out Vector3 landingPoint, out Vector3 landingNormal))
        {
            foot.Landing = landingPoint;
            if (!foot.HasGround || landingPoint.Y > point.Y)
            {
                point = new Vector3(planar.X, landingPoint.Y, planar.Z);
                normal = landingNormal;
                foot.HasGround = true;
            }
        }

        if (foot.HasGround)
        {
            foot.GroundPoint = point;
            foot.GroundNormal = normal;
            // Blended clips (e.g. walk + crouch) can dip the animated ankle below its rest height;
            // lift may raise a foot but never push it into the ground.
            float lift = Mathf.Max(0.0f, foot.AnimatedHeight - foot.RestHeight);
            foot.TargetPosition = point + normal * foot.RestHeight + Vector3.Up * lift;
        }
        else
        {
            foot.TargetPosition = planar;
        }
        foot.HeightDelta = foot.TargetPosition.Y - foot.AnimatedTransform.Origin.Y;
    }

    private bool CastGround(PhysicsDirectSpaceState3D space, Vector3 at, float originY, out Vector3 point, out Vector3 normal)
    {
        Vector3 from = new(at.X, originY + RayAbove, at.Z);
        Vector3 to = new(at.X, originY - RayBelow, at.Z);
        Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, GroundMask, _rayExclude));
        if (hit.Count == 0)
        {
            point = to;
            normal = Vector3.Up;
            return false;
        }

        point = hit["position"].AsVector3();
        normal = hit["normal"].AsVector3();
        return true;
    }

    private void Publish()
    {
        GameEvents.PublishFootPlacement(new FootPlacementSnapshot(Snapshot(_feet[0]), Snapshot(_feet[1]), HipOffset, IkWeight));
    }

    private static FootSnapshot Snapshot(FootState foot)
    {
        Vector3 pole = Vector3.Zero;
        if (foot.Pole != null)
        {
            pole = foot.Pole.GlobalPosition;
        }
        float weight = 0.0f;
        if (foot.Ik != null)
        {
            weight = foot.Ik.Influence;
        }
        return new FootSnapshot(foot.TargetPosition, foot.Landing, foot.GroundPoint, foot.GroundNormal, pole, foot.Planted, weight, foot.HasGround);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        return new Vector3(vector.X, 0.0f, vector.Z);
    }
}
