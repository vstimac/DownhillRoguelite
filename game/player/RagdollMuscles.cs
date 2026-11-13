using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Brief, bounded arm tension during a living fall. Each muscle holds the last animated relative
/// orientation with equal and opposite torques on the two bodies. No root forces, position writes
/// or upright constraint: collisions and the existing ragdoll still determine the fall.
/// </summary>
public partial class RagdollMuscles : Node
{
    [Export] public PlayerRagdoll? Ragdoll { get; set; }
    [Export] public Skeleton3D? Skeleton { get; set; }
    [Export] public bool Enabled { get; set; } = true;
    [Export] public float HoldSeconds { get; set; } = 0.45f;
    [Export] public float Frequency { get; set; } = 2.5f;
    [Export] public float DampingRatio { get; set; } = 1.0f;
    [Export] public float MaxTorque { get; set; } = 1.5f;
    [Export] public float MinimumInertia { get; set; } = 0.0001f;
    [Export] public float MaxImpulseDelta { get; set; } = 0.05f;
    /// <summary>Fast throws/impacts relax the muscles for the remainder of the fall (m/s).</summary>
    [Export] public float MaxControlledSpeed { get; set; } = 4.0f;
    [Export] public string ChestBone { get; set; } = "DEF-spine.003";
    [Export] public string[] ArmBones { get; set; } = { "DEF-upper_arm.L", "DEF-forearm.L", "DEF-upper_arm.R", "DEF-forearm.R" };
    public float Weight { get; private set; }
    public float PeakTorque { get; private set; }
    public int AppliedPairs { get; private set; }

    private readonly List<RagdollMuscle> _muscles = new();
    private float _elapsed;
    private bool _relaxed;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 25;
        if (Ragdoll == null || Skeleton == null) { return; }
        for (int i = 0; i < ArmBones.Length; i++)
        {
            string parent = ChestBone;
            if (i % 2 == 1) { parent = ArmBones[i - 1]; }
            PhysicalBone3D? childBody = Ragdoll.BodyOf(ArmBones[i]);
            PhysicalBone3D? parentBody = Ragdoll.BodyOf(parent);
            int childIndex = Skeleton.FindBone(ArmBones[i]);
            int parentIndex = Skeleton.FindBone(parent);
            if (childBody != null && parentBody != null && childIndex >= 0 && parentIndex >= 0)
            {
                float childInertia = InertiaOf(childBody);
                float parentInertia = InertiaOf(parentBody);
                float effectiveInertia = 1 / (1 / childInertia + 1 / parentInertia);
                _muscles.Add(new RagdollMuscle(childBody, parentBody, childIndex, parentIndex, effectiveInertia));
            }
        }
        // Modifier results are transient. Capture the final pose in this callback, never later.
        Skeleton.SkeletonUpdated += CapturePose;
        CapturePose();
        GameEvents.DownedChanged += OnDowned;
        if (Ragdoll.Body != null) { Ragdoll.Body.Teleported += Clear; }
    }

    public override void _ExitTree()
    {
        if (Skeleton != null) { Skeleton.SkeletonUpdated -= CapturePose; }
        GameEvents.DownedChanged -= OnDowned;
        if (Ragdoll?.Body != null) { Ragdoll.Body.Teleported -= Clear; }
    }

    private void CapturePose()
    {
        if (Skeleton == null || Ragdoll == null || Ragdoll.Downed) { return; }
        foreach (RagdollMuscle muscle in _muscles)
        {
            Basis child = Skeleton.GetBoneGlobalPose(muscle.ChildIndex).Basis.Orthonormalized() * muscle.Child.BodyOffset.Basis.Orthonormalized();
            Basis parent = Skeleton.GetBoneGlobalPose(muscle.ParentIndex).Basis.Orthonormalized() * muscle.Parent.BodyOffset.Basis.Orthonormalized();
            muscle.Target = (parent.Inverse() * child).GetRotationQuaternion().Normalized();
        }
    }

    private void Clear() { Weight = 0; _elapsed = HoldSeconds; }

    private float InertiaOf(PhysicalBone3D body)
    {
        float minimum = Mathf.Max(0.000001f, MinimumInertia);
        foreach (Node child in body.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is CapsuleShape3D capsule)
            {
                // Use the smallest moment (about the capsule's length), conservatively for every
                // axis. Tiny forearms must not receive the same stiffness as the heavy thorax.
                return Mathf.Max(minimum, 0.5f * body.Mass * capsule.Radius * capsule.Radius);
            }
        }
        return minimum;
    }
    private void OnDowned(bool downed)
    {
        Clear();
        PeakTorque = 0;
        AppliedPairs = 0;
        _relaxed = false;
        if (downed) { _elapsed = 0; }
    }

    public override void _PhysicsProcess(double delta)
    {
        Weight = 0;
        if (Ragdoll == null || !Ragdoll.Downed || delta <= 0) { return; }
        _elapsed += (float)delta;
        if (!Enabled || _relaxed || Ragdoll.Health?.Dead == true || HoldSeconds <= 0 || _elapsed >= HoldSeconds) { return; }
        float controlledSpeedSquared = Mathf.Max(0, MaxControlledSpeed) * Mathf.Max(0, MaxControlledSpeed);
        foreach (RagdollMuscle muscle in _muscles)
        {
            // On the first tick PlayerRagdoll has already assigned the launch velocities. Do not
            // resist fast launches, or re-engage partway through an uncontrolled high-speed fall.
            if (muscle.Child.LinearVelocity.LengthSquared() > controlledSpeedSquared
                || muscle.Parent.LinearVelocity.LengthSquared() > controlledSpeedSquared)
            {
                _relaxed = true;
                return;
            }
        }
        Weight = Mathf.Clamp(1 - _elapsed / HoldSeconds, 0, 1);
        float dt = Mathf.Min((float)delta, Mathf.Max(0, MaxImpulseDelta));
        foreach (RagdollMuscle muscle in _muscles)
        {
            if (!muscle.Child.IsSimulatingPhysics() || !muscle.Parent.IsSimulatingPhysics()) { continue; }
            Quaternion parent = muscle.Parent.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            Quaternion target = (parent * muscle.Target).Normalized();
            Quaternion current = muscle.Child.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            Vector3 relativeSpeed = muscle.Child.AngularVelocity - muscle.Parent.AngularVelocity;
            float omega = Mathf.Tau * Mathf.Max(0, Frequency);
            float dampingRate = 2 * Mathf.Max(0, DampingRatio) * omega;
            float stiffness = muscle.Inertia * omega * omega;
            float damping = muscle.Inertia * dampingRate;
            // Implicit spring/damping gains stay bounded even after a long physics frame.
            float stability = 1 + dampingRate * dt + omega * omega * dt * dt;
            Vector3 torque = CalculateTorque(target, current, relativeSpeed, stiffness / stability, damping / stability, MaxTorque) * Weight;
            if (muscle.Child.JointType == PhysicalBone3D.JointTypeEnum.Hinge)
            {
                // An elbow only has one degree of freedom. Never fight the constraint solver
                // by trying to reproduce an animated twist or sideways bend at this joint.
                Vector3 axis = (muscle.Child.GlobalBasis * muscle.Child.JointOffset.Basis.Z).Normalized();
                torque = axis * torque.Dot(axis);
            }
            if (!torque.IsFinite()) { continue; }
            Vector3 impulse = torque * dt;
            PhysicsServer3D.BodyApplyTorqueImpulse(muscle.Child.GetRid(), impulse);
            PhysicsServer3D.BodyApplyTorqueImpulse(muscle.Parent.GetRid(), -impulse);
            PeakTorque = Mathf.Max(PeakTorque, torque.Length());
            AppliedPairs++;
        }
    }

    /// <summary>Shortest-arc orientation error, plus damping of relative angular speed (world axes).</summary>
    public static Vector3 CalculateTorque(Quaternion target, Quaternion current, Vector3 relativeSpeed, float stiffness, float damping, float limit)
    {
        Quaternion error = (target * current.Inverse()).Normalized();
        if (error.W < 0) { error = new Quaternion(-error.X, -error.Y, -error.Z, -error.W); }
        Vector3 vector = new(error.X, error.Y, error.Z);
        float length = vector.Length();
        Vector3 rotation = Vector3.Zero;
        if (length > 0.00001f) { rotation = vector * (2 * Mathf.Atan2(length, error.W) / length); }
        return (rotation * Mathf.Max(0, stiffness) - relativeSpeed * Mathf.Max(0, damping)).LimitLength(Mathf.Max(0, limit));
    }
}
