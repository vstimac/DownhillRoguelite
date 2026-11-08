using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Gives the walk weight and life, on top of the clips (a modifier on the mannequin rig, so the shown
/// character inherits it through RigFollower):
/// - posture: leans forward under the pack and into an uphill, back on a steep descent, the head
///   kept level, the shoulders drawn up under the straps;
/// - momentum: leans into a start and back into a stop, then settles with a small sway (a spring);
/// - turns: banks into a turn by speed and turn rate, the upper body and head leading the way;
/// - breathing: the chest rises and falls, deeper and faster with a load and on the move.
/// Every amount is a tunable; Pitch, Roll and Twist are exposed for tests and the debug overlay.
/// </summary>
public partial class BodyDynamics : SkeletonModifier3D
{
    [Export] public PlayerController? Body { get; set; }
    /// <summary>Tests: a pack mass to use instead of the carried load (negative = the real load).</summary>
    [Export] public float LoadMassOverride { get; set; } = -1.0f;

    [ExportGroup("Bones")]
    [Export] public string[] SpineBones { get; set; } = { "DEF-spine.001", "DEF-spine.002", "DEF-spine.003" };
    /// <summary>Share of the lean, bank and twist each spine bone takes, root to chest.</summary>
    [Export] public float[] SpineWeights { get; set; } = { 0.25f, 0.35f, 0.4f };
    [Export] public string NeckBone { get; set; } = "DEF-neck";
    [Export] public string HeadBone { get; set; } = "DEF-head";
    [Export] public string[] ShoulderBones { get; set; } = { "DEF-shoulder.L", "DEF-shoulder.R" };
    [Export] public string[] UpperArmBones { get; set; } = { "DEF-upper_arm.L", "DEF-upper_arm.R" };

    [ExportGroup("Posture (radians)")]
    /// <summary>Forward lean per kg carried, up to MaxLoadPitch.</summary>
    [Export] public float LoadPitchPerKg { get; set; } = 0.0075f;
    [Export] public float MaxLoadPitch { get; set; } = 0.28f;
    /// <summary>Lean per unit of grade: into an uphill (forward), back on a downhill.</summary>
    [Export] public float GradePitch { get; set; } = 0.35f;
    [Export] public float MaxGradePitch { get; set; } = 0.2f;
    /// <summary>How much of the body's lean the neck and head take back, so the eyes stay level.</summary>
    [Export] public float HeadKeepsLevel { get; set; } = 0.7f;
    /// <summary>Shoulders drawn up under the straps per kg, up to MaxHunch.</summary>
    [Export] public float HunchPerKg { get; set; } = 0.008f;
    [Export] public float MaxHunch { get; set; } = 0.22f;
    /// <summary>Arms held in toward the straps per kg, up to MaxArmsIn.</summary>
    [Export] public float ArmsInPerKg { get; set; } = 0.006f;
    [Export] public float MaxArmsIn { get; set; } = 0.18f;

    [ExportGroup("Momentum")]
    /// <summary>Lean per m/s² of forward acceleration (back when slowing down).</summary>
    [Export] public float AccelPitch { get; set; } = 0.045f;
    [Export] public float MaxAccelPitch { get; set; } = 0.22f;
    /// <summary>Bank into a turn per (rad/s × m/s).</summary>
    [Export] public float BankPerTurnSpeed { get; set; } = 0.06f;
    [Export] public float MaxBank { get; set; } = 0.2f;
    /// <summary>How far ahead (s of turning) the upper body leads a turn, up to MaxLead.</summary>
    [Export] public float LeadSeconds { get; set; } = 0.18f;
    [Export] public float MaxLead { get; set; } = 0.45f;
    /// <summary>Extra lead the head takes on top of the chest.</summary>
    [Export] public float HeadLead { get; set; } = 0.6f;
    /// <summary>Spring of the lean and bank: frequency (Hz) and damping (1 = no overshoot).</summary>
    [Export] public float LeanFrequency { get; set; } = 1.8f;
    [Export] public float LeanDamping { get; set; } = 0.45f;
    [Export] public float TwistFrequency { get; set; } = 2.6f;
    [Export] public float TwistDamping { get; set; } = 0.8f;
    /// <summary>Smoothing of the measured velocity before it's differentiated (1/s).</summary>
    [Export] public float VelocitySharpness { get; set; } = 10.0f;

    [ExportGroup("Breathing")]
    [Export] public float BreathDepth { get; set; } = 0.012f;
    [Export] public float BreathDepthPerKg { get; set; } = 0.0006f;
    /// <summary>Breaths per second at rest, and added at full walking speed.</summary>
    [Export] public float BreathRate { get; set; } = 0.25f;
    [Export] public float BreathRateMoving { get; set; } = 0.35f;
    [Export] public float BreathFullSpeed { get; set; } = 3.0f;

    /// <summary>Current forward lean (+ forward), bank (+ right) and upper-body twist (+ left), radians.</summary>
    public float Pitch => _pitch + _staticPitch;
    public float Roll => _roll;
    public float Twist => _twist;
    public float Hunch { get; private set; }

    private float _pitch;
    private float _pitchVelocity;
    private float _roll;
    private float _rollVelocity;
    private float _twist;
    private float _twistVelocity;
    private float _staticPitch;
    private float _breathPhase;
    private Vector3 _smoothVelocity;
    private Vector3 _lastVelocity;
    private float _lastYaw;
    private bool _started;
    private float _grounded = 1.0f;
    private int[] _spine = System.Array.Empty<int>();
    private int _neck = -1;
    private int _head = -1;
    private int[] _shoulders = System.Array.Empty<int>();
    private int[] _arms = System.Array.Empty<int>();

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        float dt = (float)delta;
        if (skeleton == null || Body == null || dt <= 0.0f)
        {
            return;
        }
        FindBones(skeleton);
        Measure(dt);

        Basis toSkeleton = skeleton.GlobalTransform.Basis.Orthonormalized().Inverse();
        Vector3 forward = (toSkeleton * -Body.GlobalBasis.Z).Normalized();
        Vector3 right = (toSkeleton * Body.GlobalBasis.X).Normalized();
        Vector3 up = (toSkeleton * Vector3.Up).Normalized();

        float breath = Mathf.Sin(_breathPhase * Mathf.Tau) * (BreathDepth + BreathDepthPerKg * Load());
        float weight = _grounded;
        for (int i = 0; i < _spine.Length && i < SpineWeights.Length; i++)
        {
            if (_spine[i] < 0)
            {
                continue;
            }
            float share = SpineWeights[i] * weight;
            float pitch = Pitch * share;
            if (i == _spine.Length - 1)
            {
                pitch -= breath;
            }
            Quaternion turn = new Quaternion(up, _twist * share) * new Quaternion(forward, _roll * share) * new Quaternion(right, -pitch);
            Rotate(skeleton, _spine[i], turn);
        }
        // The neck and head take back most of the lean, and look further into a turn.
        float keep = -Pitch * HeadKeepsLevel * weight;
        if (_neck >= 0)
        {
            Rotate(skeleton, _neck, new Quaternion(right, -keep * 0.5f));
        }
        if (_head >= 0)
        {
            Rotate(skeleton, _head, new Quaternion(up, _twist * HeadLead * weight) * new Quaternion(right, -keep * 0.5f) * new Quaternion(forward, -_roll * 0.5f * weight));
        }
        // Shoulders up under the straps, arms held in a little.
        for (int i = 0; i < _shoulders.Length; i++)
        {
            Lift(skeleton, _shoulders[i], ChildOf(i, _arms), up, Hunch * weight);
        }
        for (int i = 0; i < _arms.Length; i++)
        {
            Lift(skeleton, _arms[i], -1, -up, Mathf.Min(Load() * ArmsInPerKg, MaxArmsIn) * weight);
        }
    }

    /// <summary>Velocity, acceleration and turn rate from the body; the springs and the breath advance.</summary>
    private void Measure(float dt)
    {
        Vector3 velocity = Body!.Velocity;
        velocity.Y = 0.0f;
        float yaw = Body.GlobalRotation.Y;
        if (!_started)
        {
            _smoothVelocity = velocity;
            _lastVelocity = velocity;
            _lastYaw = yaw;
            _started = true;
        }
        _smoothVelocity = _smoothVelocity.Lerp(velocity, 1.0f - Mathf.Exp(-VelocitySharpness * dt));
        Vector3 acceleration = (_smoothVelocity - _lastVelocity) / dt;
        _lastVelocity = _smoothVelocity;
        float yawRate = Mathf.AngleDifference(_lastYaw, yaw) / dt;
        _lastYaw = yaw;
        float speed = _smoothVelocity.Length();
        Vector3 forward = -Body.GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();

        float groundedTarget = 0.0f;
        if (Body.IsOnFloor())
        {
            groundedTarget = 1.0f;
        }
        _grounded = Mathf.Lerp(_grounded, groundedTarget, 1.0f - Mathf.Exp(-8.0f * dt));

        float load = Load();
        float loadPitch = Mathf.Min(load * LoadPitchPerKg, MaxLoadPitch);
        float gradePitch = Mathf.Clamp(-Body.MoveGrade * GradePitch, -MaxGradePitch, MaxGradePitch);
        _staticPitch = Mathf.Lerp(_staticPitch, loadPitch + gradePitch, 1.0f - Mathf.Exp(-4.0f * dt));
        float accelPitch = Mathf.Clamp(acceleration.Dot(forward) * AccelPitch, -MaxAccelPitch, MaxAccelPitch);
        float bank = Mathf.Clamp(-yawRate * speed * BankPerTurnSpeed, -MaxBank, MaxBank);
        float lead = Mathf.Clamp(yawRate * LeadSeconds, -MaxLead, MaxLead);
        Spring(ref _pitch, ref _pitchVelocity, accelPitch, LeanFrequency, LeanDamping, dt);
        Spring(ref _roll, ref _rollVelocity, bank, LeanFrequency, LeanDamping, dt);
        Spring(ref _twist, ref _twistVelocity, lead, TwistFrequency, TwistDamping, dt);
        Hunch = Mathf.Lerp(Hunch, Mathf.Min(load * HunchPerKg, MaxHunch), 1.0f - Mathf.Exp(-4.0f * dt));
        float rate = BreathRate + BreathRateMoving * Mathf.Clamp(speed / BreathFullSpeed, 0.0f, 1.0f);
        _breathPhase = Mathf.PosMod(_breathPhase + rate * dt, 1.0f);
    }

    private float Load()
    {
        if (LoadMassOverride >= 0.0f)
        {
            return LoadMassOverride;
        }
        return GameEvents.CurrentLoad.Mass;
    }

    /// <summary>A damped spring toward <paramref name="target"/> (semi-implicit, stable at game frame rates).</summary>
    private static void Spring(ref float value, ref float velocity, float target, float frequency, float damping, float dt)
    {
        float stiffness = Mathf.Pow(Mathf.Tau * frequency, 2.0f);
        float friction = 2.0f * damping * Mathf.Sqrt(stiffness);
        velocity += (stiffness * (target - value) - friction * velocity) * dt;
        value += velocity * dt;
    }

    private static void Rotate(Skeleton3D skeleton, int bone, Quaternion turn)
    {
        Transform3D pose = skeleton.GetBoneGlobalPose(bone);
        skeleton.SetBoneGlobalPose(bone, new Transform3D(new Basis(turn) * pose.Basis, pose.Origin));
    }

    /// <summary>Turns a bone so its far end moves toward <paramref name="toward"/> by <paramref name="angle"/>.</summary>
    private static void Lift(Skeleton3D skeleton, int bone, int child, Vector3 toward, float angle)
    {
        if (bone < 0 || Mathf.IsZeroApprox(angle))
        {
            return;
        }
        Vector3 along;
        if (child >= 0)
        {
            along = skeleton.GetBoneGlobalPose(child).Origin - skeleton.GetBoneGlobalPose(bone).Origin;
        }
        else
        {
            along = skeleton.GetBoneGlobalPose(bone).Basis.Y;
        }
        Vector3 axis = along.Cross(toward);
        if (axis.LengthSquared() < 1e-8f)
        {
            return;
        }
        Rotate(skeleton, bone, new Quaternion(axis.Normalized(), angle));
    }

    private int ChildOf(int i, int[] list)
    {
        if (i < list.Length)
        {
            return list[i];
        }
        return -1;
    }

    private void FindBones(Skeleton3D skeleton)
    {
        if (_spine.Length == SpineBones.Length && _shoulders.Length == ShoulderBones.Length && _arms.Length == UpperArmBones.Length)
        {
            return;
        }
        _spine = Indices(skeleton, SpineBones);
        _shoulders = Indices(skeleton, ShoulderBones);
        _arms = Indices(skeleton, UpperArmBones);
        _neck = skeleton.FindBone(NeckBone);
        _head = skeleton.FindBone(HeadBone);
    }

    private static int[] Indices(Skeleton3D skeleton, string[] names)
    {
        int[] found = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            found[i] = skeleton.FindBone(names[i]);
        }
        return found;
    }
}
