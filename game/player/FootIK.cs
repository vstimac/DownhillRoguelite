using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Procedural foot placement on top of the animation. At startup it adds a modifier chain under the
/// skeleton: FootPlacementModifier and FootAlignModifier. Each frame it detects which feet the
/// animation has planted and locks them in place, raycasts the ground under each foot (and ahead of
/// a swinging foot, so it lifts onto steps), lowers the hips so the lower foot can reach, bends each
/// leg onto its target (its own two-bone solve) and tilts planted feet to the surface. IK blends out
/// when airborne or sliding.
/// The legs are solved here rather than by TwoBoneIK3D: that node reads its target marker's cached
/// (interpolated) transform, which is filled before this frame's targets are placed, so every leg
/// reached for where its foot was a frame ago. A swinging foot trailed its clip by up to 10 cm and the
/// knee stayed bent where the clip straightens it, and the lag changed with the frame rate, so the legs
/// seemed to stretch and bounce (LegMotionTests).
/// </summary>
public partial class FootIK : Node3D
{
    // Dictionary string keys otherwise create a finalizable native Variant for every ray.
    // Cached Variants are borrowed by the lookup and must never be disposed per call.
    private static readonly Variant PositionKey = "position";
    private static readonly Variant NormalKey = "normal";
    [Export] public PlayerController? Body { get; set; }
    [Export] public Skeleton3D? Skeleton { get; set; }
    /// <summary>Remove the recovery's hip displacement when planning ankles; IK then solves against the shifted hips.</summary>
    [Export] public ActiveBalancePose? RecoveryPose { get; set; }

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

    [ExportGroup("Gait bounce")]
    /// <summary>
    /// Share of the gait clips' up-and-down hip motion kept while moving. The jog clip (the only clip at
    /// the normal pace) lifts and drops the hips 24 cm a step, a bound meant for its own 5 m/s, which
    /// slowed down read as a bouncing, stretching run. The feet keep the clip's heights, so the knees
    /// take up the difference.
    /// </summary>
    [Export(PropertyHint.Range, "0,1")] public float GaitBounceKept { get; set; } = 0.4f;
    /// <summary>The gait bounce is evened out between these speeds (m/s): none at the first, fully at the second.</summary>
    [Export] public Vector2 GaitBounceSpeeds { get; set; } = new(1.5f, 2.6f);
    /// <summary>How quickly the hips' mean height follows the clips, in s (longer than a step).</summary>
    [Export] public float GaitBounceMeanTime { get; set; } = 0.5f;
    /// <summary>
    /// The most the hips are raised (m): lifted further, a planted leg can't reach the ground and lets
    /// go (MaxReachFraction), worst while the mean is still catching up as he sets off.
    /// </summary>
    [Export] public float GaitBounceMaxRaise { get; set; } = 0.06f;
    /// <summary>Raised hips leave a planted leg at most this share of its length from its foot.</summary>
    [Export] public float GaitBounceReach { get; set; } = 0.95f;

    public IReadOnlyList<FootState> Feet => _feet;
    public float IkWeight { get; private set; }
    public float HipOffset { get; private set; }
    /// <summary>How far the hips were moved up (+) or down this frame to even out the gait bounce, in m (skeleton space).</summary>
    public float BounceShift { get; private set; }

    private readonly List<FootState> _feet = new();
    private int _hipsIndex = -1;
    private float _hipsMean = float.NaN;
    private float _bounceWeight;
    private Vector3 _lastOrigin;
    private bool _hasLastOrigin;
    private Godot.Collections.Array<Rid>? _rayExclude;
    private PhysicsRayQueryParameters3D? _groundQuery;

    public override void _Ready()
    {
        if (Body == null || Skeleton == null)
        {
            GD.PushError("FootIK: Body and Skeleton must be assigned.");
            return;
        }

        _rayExclude = new Godot.Collections.Array<Rid> { Body.GetRid() };
        _groundQuery = new PhysicsRayQueryParameters3D { CollisionMask = GroundMask, Exclude = _rayExclude };
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

    public override void _ExitTree()
    {
        _groundQuery?.Dispose();
        _groundQuery = null;
        if (_rayExclude != null) { ((Godot.Collections.Array)_rayExclude).Dispose(); }
        _rayExclude = null;
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
    }

    /// <summary>
    /// Keeps <see cref="GaitBounceKept"/> of the hips' up-and-down motion around their running mean
    /// while striding at pace (not crouching, not in the air, not in an action that moves the legs).
    /// Returns the world-space vertical shift applied to the hips.
    /// </summary>
    private float EvenOutBounce(Skeleton3D skeleton, Transform3D skeletonWorld, bool teleported, float dt)
    {
        BounceShift = 0.0f;
        if (_hipsIndex < 0 || Body == null)
        {
            return 0.0f;
        }
        Transform3D hips = skeleton.GetBoneGlobalPose(_hipsIndex);
        Vector3 up = (skeletonWorld.Basis.Inverse() * Vector3.Up).Normalized();
        float height = hips.Origin.Dot(up);
        if (RecoveryPose != null && RecoveryPose.Active)
        {
            // The balance recovery's crouch is meant: even out only what the clips do.
            height -= (skeletonWorld.Basis.Inverse() * (RecoveryPose.HipShift * RecoveryPose.Influence)).Dot(up);
        }
        if (float.IsNaN(_hipsMean) || teleported)
        {
            _hipsMean = height;
        }
        _hipsMean = Mathf.Lerp(_hipsMean, height, 1.0f - Mathf.Exp(-dt / Mathf.Max(GaitBounceMeanTime, 0.01f)));

        float speed = Flat(Body.Velocity).Length();
        float wanted = 0.0f;
        if (Body.State == LocomotionState.Striding && !Body.Crouching && !GameEvents.IsVaulting && !GameEvents.IsDowned)
        {
            wanted = Mathf.SmoothStep(GaitBounceSpeeds.X, GaitBounceSpeeds.Y, speed);
        }
        _bounceWeight = Mathf.MoveToward(_bounceWeight, wanted, dt / IkBlendTime);
        float keep = Mathf.Lerp(1.0f, GaitBounceKept, _bounceWeight * IkWeight);
        float shift = Mathf.Min((_hipsMean - height) * (1.0f - keep), GaitBounceMaxRaise);
        if (shift > 0.0f)
        {
            // Never lift the hips past what a planted leg can reach (it would let go of the ground).
            foreach (FootState foot in _feet)
            {
                // Every leg on the ground or swinging to it (its last target): a swinging foot's target
                // follows the clip, so the limit tightens smoothly as it comes down to plant.
                if (foot.ThighIndex < 0 || !foot.HasGround) { continue; }
                Vector3 thigh = skeletonWorld * skeleton.GetBoneGlobalPose(foot.ThighIndex).Origin;
                float reach = foot.LegLength * Mathf.Min(GaitBounceReach, MaxReachFraction);
                float across = Flat(thigh - foot.TargetPosition).Length();
                float room = across < reach ? Mathf.Sqrt(reach * reach - across * across) - (thigh.Y - foot.TargetPosition.Y) : 0.0f;
                shift = Mathf.Min(shift, Mathf.Max(room, 0.0f));
            }
        }
        if (Mathf.IsZeroApprox(shift))
        {
            return 0.0f;
        }
        BounceShift = shift;
        hips.Origin += up * shift;
        skeleton.SetBoneGlobalPose(_hipsIndex, hips);
        return (skeletonWorld.Basis * (up * shift)).Y;
    }

    /// <summary>
    /// Bends one leg so the ankle reaches its target this frame: the thigh turns toward the knee point
    /// the two bone lengths allow (in the plane of hip, target and knee pole), the shin toward the
    /// ankle. Blended by the leg's weight; the foot keeps its pose relative to the shin until
    /// AfterLegIK turns it back to the animation and the ground.
    /// </summary>
    private void SolveLeg(Skeleton3D skeleton, Transform3D skeletonWorld, FootState foot)
    {
        float weight = foot.LegWeight;
        if (weight <= 0.0f || foot.ThighIndex < 0 || foot.ShinIndex < 0 || foot.FootIndex < 0)
        {
            return;
        }
        Transform3D toSkeleton = skeletonWorld.AffineInverse();
        Transform3D thigh = skeleton.GetBoneGlobalPose(foot.ThighIndex);
        Transform3D shin = skeleton.GetBoneGlobalPose(foot.ShinIndex);
        Vector3 hip = thigh.Origin;
        Vector3 knee = shin.Origin;
        Vector3 ankle = skeleton.GetBoneGlobalPose(foot.FootIndex).Origin;
        Vector3 target = toSkeleton * foot.TargetPosition;
        Vector3 pole = toSkeleton * foot.PolePosition;

        float upper = hip.DistanceTo(knee);
        float lower = knee.DistanceTo(ankle);
        Vector3 reach = target - hip;
        if (upper < 1e-4f || lower < 1e-4f || reach.LengthSquared() < 1e-8f)
        {
            return;
        }
        float distance = Mathf.Clamp(reach.Length(), Mathf.Abs(upper - lower) + 1e-4f, upper + lower - 1e-4f);
        Vector3 along = reach.Normalized();
        // The knee bends toward the pole, or else the way the clip bends it.
        Vector3 bend = (pole - hip) - along * (pole - hip).Dot(along);
        if (bend.LengthSquared() < 1e-8f)
        {
            bend = (knee - hip) - along * (knee - hip).Dot(along);
        }
        if (bend.LengthSquared() < 1e-8f)
        {
            return;
        }
        bend = bend.Normalized();
        float cos = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2.0f * upper * distance), -1.0f, 1.0f);
        Vector3 newKnee = hip + along * (upper * cos) + bend * (upper * Mathf.Sqrt(1.0f - cos * cos));
        Vector3 newAnkle = hip + along * distance;

        Quaternion thighTurn = Turn(knee - hip, newKnee - hip);
        Quaternion thighRotation = thigh.Basis.GetRotationQuaternion();
        Quaternion thighSolved = (thighTurn * thighRotation).Normalized();
        Quaternion thighFinal = thighRotation.Slerp(thighSolved, weight);
        skeleton.SetBoneGlobalPose(foot.ThighIndex, new Transform3D(new Basis(thighFinal), hip));

        // The shin as the turned thigh carries it, then turned onto the ankle.
        Transform3D carried = skeleton.GetBoneGlobalPose(foot.ShinIndex);
        Vector3 carriedAnkle = carried.Origin + (thighFinal * thighRotation.Inverse()) * (ankle - knee);
        Vector3 wantedAnkle = ankle.Lerp(newAnkle, weight);
        Quaternion shinTurn = Turn(carriedAnkle - carried.Origin, wantedAnkle - carried.Origin);
        Quaternion shinFinal = (shinTurn * carried.Basis.GetRotationQuaternion()).Normalized();
        skeleton.SetBoneGlobalPose(foot.ShinIndex, new Transform3D(new Basis(shinFinal), carried.Origin));
    }

    /// <summary>The shortest turn taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    private static Quaternion Turn(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f)
        {
            return Quaternion.Identity;
        }
        Vector3 a = from.Normalized();
        Vector3 b = to.Normalized();
        if (a.Dot(b) > 0.999999f)
        {
            return Quaternion.Identity;
        }
        if (a.Dot(b) < -0.999999f)
        {
            Vector3 axis = a.Cross(Vector3.Right);
            if (axis.LengthSquared() < 1e-6f) { axis = a.Cross(Vector3.Up); }
            return new Quaternion(axis.Normalized(), Mathf.Pi);
        }
        return new Quaternion(a, b).Normalized();
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
        bool grounded = Body.State == LocomotionState.Idle || Body.State == LocomotionState.Striding || Body.State == LocomotionState.Braking;
        // A dive or a landing roll moves the legs itself: no feet on the ground.
        if (grounded && !GameEvents.IsVaulting)
        {
            targetWeight = 1.0f;
        }
        IkWeight = Mathf.MoveToward(IkWeight, targetWeight, dt / IkBlendTime);
        float bounceWorld = EvenOutBounce(skeleton, skeletonWorld, teleported, dt);

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
            if (RecoveryPose != null && RecoveryPose.Active)
            {
                // Pelvis correction must not drag the contact targets along with the body, even
                // when an animated foot is not locked. Only the leg solver sees the shifted hips.
                animated.Origin -= RecoveryPose.HipShift * RecoveryPose.Influence;
            }
            foot.AnimatedTransform = animated;
            foot.AnimatedSkeletonBasis = animatedLocal.Basis;
            // The clip's own foot height: the evened-out bounce moved the hips, not where the foot steps.
            foot.AnimatedHeight = animated.Origin.Y - bounceWorld - origin.Y;

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
            // Against the clip's foot: hips raised to even out the bounce aren't a reason to drop them again.
            foot.HeightDelta += bounceWorld;

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
            if (foot.FootIndex < 0 || foot.ThighIndex < 0 || foot.ShinIndex < 0)
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

            Vector3 knee = skeletonWorld * skeleton.GetBoneGlobalPose(foot.ShinIndex).Origin;
            foot.PolePosition = knee + forward * PoleDistance;
            foot.LegWeight = IkWeight * foot.GroundWeight;
            SolveLeg(skeleton, skeletonWorld, foot);
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
        PhysicsRayQueryParameters3D? query = _groundQuery;
        if (query == null)
        {
            point = to;
            normal = Vector3.Up;
            return false;
        }
        query.From = from;
        query.To = to;
        query.CollisionMask = GroundMask;
        using Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            point = to;
            normal = Vector3.Up;
            return false;
        }

        using Variant positionValue = hit[PositionKey];
        using Variant normalValue = hit[NormalKey];
        point = positionValue.AsVector3();
        normal = normalValue.AsVector3();
        return true;
    }

    private void Publish()
    {
        GameEvents.PublishFootPlacement(new FootPlacementSnapshot(Snapshot(_feet[0]), Snapshot(_feet[1]), HipOffset, IkWeight));
    }

    private static FootSnapshot Snapshot(FootState foot)
    {
        return new FootSnapshot(foot.TargetPosition, foot.Landing, foot.GroundPoint, foot.GroundNormal, foot.PolePosition, foot.Planted, foot.LegWeight, foot.HasGround);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        return new Vector3(vector.X, 0.0f, vector.Z);
    }
}
