using System;
using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Procedural get-up: physical torques only, no clip and no position writes. Every bone pair of the
/// ragdoll is driven toward a target relative orientation (the tuned RagdollMuscles law: bounded implicit
/// spring/damping, equal and opposite impulses). The target blends from the lying shape to the rest
/// (standing) pose by one phase variable, 0 lying to 1 standing. The hips (root) get a self-righting
/// torque toward upright; the ground reaction does the rest. The ragdoll keeps simulating throughout.
/// </summary>
public sealed class RagdollRise
{
    private sealed class Joint
    {
        public PhysicalBone3D Child = null!;
        public PhysicalBone3D Parent = null!;
        public Quaternion StandRelative;
        public Quaternion LyingRelative;
        public float Inertia;
        public float Cap;
    }

    private readonly List<Joint> _joints = new();
    private readonly PhysicalBone3D _root;
    private readonly Vector3 _rootUpLocal;
    private readonly float _rootInertia;
    private float _elapsed;
    private float _uprightTime;

    /// <summary>Drive stiffness (Hz) and damping ratio for every joint, and for the root's tilt.</summary>
    public float Frequency { get; set; } = 1.5f;
    public float DampingRatio { get; set; } = 1.0f;
    public float RootFrequency { get; set; } = 2.0f;
    /// <summary>Torque cap per kilogram of the child body (N·m/kg).</summary>
    public float TorquePerKilogram { get; set; } = 12.0f;
    public float RootTorqueCap { get; set; } = 400.0f;
    /// <summary>Time from the start of the rise to standing (s); the phase variable runs over this.</summary>
    public float RiseSeconds { get; set; } = 2.5f;
    /// <summary>Upright means the hips' up axis is within this of world up (cos, 0.9 ≈ 25 degrees).</summary>
    public float UprightCos { get; set; } = 0.9f;
    /// <summary>Upright must hold this long before the rise ends (s).</summary>
    public float HoldSeconds { get; set; } = 0.25f;
    /// <summary>The rise gives up after this long, whatever he is doing (s).</summary>
    public float MaxSeconds { get; set; } = 8.0f;

    /// <summary>Prints the phase and hips every half second (diagnostics; off by default).</summary>
    public static bool Trace { get; set; }

    /// <summary>0 = lying (the shape he fell into), 1 = standing (the rest pose).</summary>
    public float Phase { get; private set; }
    public bool Finished { get; private set; }
    /// <summary>True when the rise ended with the hips upright; false when it timed out.</summary>
    public bool StoodUp { get; private set; }
    /// <summary>Hips' up axis, world (tests read it).</summary>
    public Vector3 HipsUp => _root.GlobalBasis * _rootUpLocal;

    /// <summary>
    /// Builds the joints from the rig's rest pose. <paramref name="names"/> is the bone list (root first);
    /// <paramref name="bodyOf"/> maps a bone name to its physical body (null when absent).
    /// </summary>
    public RagdollRise(Skeleton3D skeleton, IReadOnlyList<string> names, Func<string, PhysicalBone3D?> bodyOf)
    {
        _root = bodyOf(names[0]) ?? throw new ArgumentException("the rig has no root body");
        Vector3 upSkeleton = skeleton.GlobalBasis.Inverse() * Vector3.Up;
        int rootBone = skeleton.FindBone(names[0]);
        _rootUpLocal = skeleton.GetBoneGlobalRest(rootBone).Basis.Orthonormalized().Inverse() * upSkeleton;
        _rootInertia = Mathf.Max(0.01f, _root.Mass * 0.2f);
        for (int i = 1; i < names.Count; i++)
        {
            PhysicalBone3D? child = bodyOf(names[i]);
            int childBone = skeleton.FindBone(names[i]);
            if (child == null || childBone < 0) { continue; }
            // Walk up the rig to the nearest bone that has a body: that is the joint's parent.
            int parentBone = skeleton.GetBoneParent(childBone);
            PhysicalBone3D? parent = null;
            while (parentBone >= 0 && parent == null)
            {
                parent = bodyOf(skeleton.GetBoneName(parentBone));
                if (parent == null) { parentBone = skeleton.GetBoneParent(parentBone); }
            }
            if (parent == null) { continue; }
            Basis childRest = skeleton.GetBoneGlobalRest(childBone).Basis.Orthonormalized() * child.BodyOffset.Basis.Orthonormalized();
            Basis parentRest = skeleton.GetBoneGlobalRest(parentBone).Basis.Orthonormalized() * parent.BodyOffset.Basis.Orthonormalized();
            float childInertia = Mathf.Max(0.01f, child.Mass * 0.1f);
            float parentInertia = Mathf.Max(0.01f, parent.Mass * 0.1f);
            _joints.Add(new Joint
            {
                Child = child,
                Parent = parent,
                StandRelative = (parentRest.Inverse() * childRest).GetRotationQuaternion().Normalized(),
                Inertia = 1 / (1 / childInertia + 1 / parentInertia),
                Cap = TorquePerKilogram * child.Mass,
            });
        }
    }

    /// <summary>Starts the rise from the pose he is in now (the lying shape is the phase-0 target).</summary>
    public void Begin()
    {
        _elapsed = 0;
        _uprightTime = 0;
        Phase = 0;
        Finished = false;
        StoodUp = false;
        foreach (Joint joint in _joints)
        {
            joint.LyingRelative = (joint.Parent.GlobalBasis.Orthonormalized().Inverse() * joint.Child.GlobalBasis.Orthonormalized()).GetRotationQuaternion().Normalized();
        }
    }

    /// <summary>One physics step: torques only. Call once per physics frame while he is down.</summary>
    public void Step(float dt)
    {
        if (Finished || dt <= 0) { return; }
        _elapsed += dt;
        float t = Mathf.Clamp(_elapsed / Mathf.Max(0.01f, RiseSeconds), 0, 1);
        Phase = t * t * (3 - 2 * t);
        // Implicit gains stay bounded even after a long frame, as in RagdollMuscles.
        float omega = Mathf.Tau * Mathf.Max(0, Frequency);
        float damping = 2 * Mathf.Max(0, DampingRatio) * omega;
        float stability = 1 + damping * dt + omega * omega * dt * dt;
        foreach (Joint joint in _joints)
        {
            Quaternion parent = joint.Parent.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            Quaternion relative = joint.LyingRelative.Slerp(joint.StandRelative, Phase).Normalized();
            Quaternion target = (parent * relative).Normalized();
            Quaternion current = joint.Child.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            Vector3 relativeSpeed = joint.Child.AngularVelocity - joint.Parent.AngularVelocity;
            float stiffness = joint.Inertia * omega * omega / stability;
            float dampingGain = joint.Inertia * damping / stability;
            Vector3 torque = RagdollMuscles.CalculateTorque(target, current, relativeSpeed, stiffness, dampingGain, joint.Cap);
            if (!torque.IsFinite()) { continue; }
            Vector3 impulse = torque * dt;
            PhysicsServer3D.BodyApplyTorqueImpulse(joint.Child.GetRid(), impulse);
            PhysicsServer3D.BodyApplyTorqueImpulse(joint.Parent.GetRid(), -impulse);
        }
        // Self-righting: the hips' tilt toward world up, from the ground's reaction only.
        Vector3 up = HipsUp;
        Vector3 error = up.Cross(Vector3.Up);
        float rootOmega = Mathf.Tau * Mathf.Max(0, RootFrequency);
        float rootStiffness = _rootInertia * rootOmega * rootOmega;
        float rootDamping = _rootInertia * 2 * Mathf.Max(0, DampingRatio) * rootOmega;
        float rootStability = 1 + 2 * Mathf.Max(0, DampingRatio) * rootOmega * dt + rootOmega * rootOmega * dt * dt;
        Vector3 rootTorque = (error * rootStiffness - _root.AngularVelocity * rootDamping) / rootStability;
        rootTorque = rootTorque.LimitLength(Mathf.Max(0, RootTorqueCap));
        if (rootTorque.IsFinite())
        {
            PhysicsServer3D.BodyApplyTorqueImpulse(_root.GetRid(), rootTorque * dt);
        }
        // Standing = hips upright, held for a short while. A timeout ends the rise regardless.
        _uprightTime = up.Y >= UprightCos ? _uprightTime + dt : 0;
        if (Trace && Mathf.Abs(_elapsed - Mathf.Round(_elapsed * 2) / 2) < dt)
        {
            GD.Print($"RISE t={_elapsed:0.0} phase={Phase:0.00} hipsUpY={up.Y:0.00} hipsY={_root.GlobalPosition.Y:0.00} hipsSpeed={_root.LinearVelocity.Length():0.00}");
        }
        if (Phase >= 1 && _uprightTime >= HoldSeconds)
        {
            Finished = true;
            StoodUp = true;
        }
        else if (_elapsed >= MaxSeconds)
        {
            Finished = true;
        }
    }
}
