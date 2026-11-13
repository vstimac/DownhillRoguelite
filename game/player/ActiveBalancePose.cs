using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Grounded recovery layered over clips, before FootIK: spring-driven hips move against the
/// measured COM error, knees soften and free arms open. Never moves the controller or changes
/// the balance model. The physical ragdoll owns the pose as soon as the carrier goes down.
/// </summary>
public partial class ActiveBalancePose : SkeletonModifier3D
{
    [Export] public PlayerController? Body { get; set; }
    [Export] public bool Enabled { get; set; } = true;
    [ExportGroup("Recovery")]
    [Export] public float StartImbalance { get; set; } = 0.4f;
    [Export] public float FullImbalance { get; set; } = 1.4f;
    [Export] public float HipCorrection { get; set; } = 0.4f;
    [Export] public float MaxHipShift { get; set; } = 0.075f;
    [Export] public float MaxCrouch { get; set; } = 0.12f;
    [Export] public float Frequency { get; set; } = 3.0f;
    [Export] public float Damping { get; set; } = 0.85f;
    [Export] public float BlendSeconds { get; set; } = 0.12f;
    [ExportGroup("Free arms (radians)")]
    [Export] public float ArmSpread { get; set; } = 0.65f;
    [Export] public float ArmForward { get; set; } = 0.3f;
    [Export] public float ElbowBend { get; set; } = 0.35f;
    [ExportGroup("Bones")]
    [Export] public string HipsBone { get; set; } = "DEF-hips";
    [Export] public string LeftArmBone { get; set; } = "DEF-upper_arm.L";
    [Export] public string RightArmBone { get; set; } = "DEF-upper_arm.R";
    [Export] public string LeftElbowBone { get; set; } = "DEF-forearm.L";
    [Export] public string RightElbowBone { get; set; } = "DEF-forearm.R";

    public Vector3 HipShift { get; private set; }
    public float Effort { get; private set; }
    public float Weight { get; private set; }
    public float LeftArmWeight { get; private set; }
    public float RightArmWeight { get; private set; }
    private BalanceSnapshot _balance;
    private bool _hasBalance;
    private Vector3 _shift;
    private Vector3 _speed;
    private float _effortSpeed;
    private int _hips = -1;
    private int _left = -1;
    private int _right = -1;
    private int _leftElbow = -1;
    private int _rightElbow = -1;
    private CarrierTorch? _torch;

    public override void _Ready()
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton != null)
        {
            _hips = skeleton.FindBone(HipsBone);
            _left = skeleton.FindBone(LeftArmBone);
            _right = skeleton.FindBone(RightArmBone);
            _leftElbow = skeleton.FindBone(LeftElbowBone);
            _rightElbow = skeleton.FindBone(RightElbowBone);
        }
        _torch = Body?.GetNodeOrNull<CarrierTorch>("Torch");
        GameEvents.BalanceUpdated += OnBalance;
        GameEvents.DownedChanged += OnDowned;
        if (Body != null) { Body.Teleported += Reset; }
    }

    public override void _ExitTree()
    {
        GameEvents.BalanceUpdated -= OnBalance;
        GameEvents.DownedChanged -= OnDowned;
        if (Body != null) { Body.Teleported -= Reset; }
    }

    private void OnBalance(BalanceSnapshot balance) { _balance = balance; _hasBalance = true; }
    private void OnDowned(bool downed) { Reset(); }

    private void Reset()
    {
        _hasBalance = false;
        _shift = Vector3.Zero;
        _speed = Vector3.Zero;
        _effortSpeed = 0;
        HipShift = Vector3.Zero;
        Effort = 0;
        Weight = 0;
        LeftArmWeight = 0;
        RightArmWeight = 0;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null || Body == null || delta <= 0) { return; }
        float dt = (float)delta;
        if (GameEvents.IsDowned || _balance.State == BalanceState.Fallen)
        {
            Reset();
            return;
        }
        float targetWeight = 0;
        bool grounded = Body.IsOnFloor() && (Body.State == LocomotionState.Idle
            || Body.State == LocomotionState.Striding || Body.State == LocomotionState.Braking);
        if (Enabled && _hasBalance && grounded && !GameEvents.IsVaulting && !GameEvents.IsPacking)
        {
            targetWeight = 1;
        }
        Weight = Mathf.MoveToward(Weight, targetWeight, dt / Mathf.Max(BlendSeconds, 0.001f));
        float amount = (_balance.ImbalanceMagnitude - StartImbalance) / Mathf.Max(FullImbalance - StartImbalance, 0.001f);
        float targetEffort = Mathf.SmoothStep(0, 1, Mathf.Clamp(amount, 0, 1));
        if (_balance.State == BalanceState.Stumbling) { targetEffort = 1; }
        float effort = Effort;
        DampedSpring.Step(ref effort, ref _effortSpeed, targetEffort * targetWeight, Frequency, Damping, dt);
        Effort = Mathf.Clamp(effort, 0, 1);

        Vector3 error = _balance.CenterOfMassGround - _balance.SupportCenter;
        error.Y = 0;
        Vector3 target = (-error * Mathf.Max(0, HipCorrection) * Effort).LimitLength(Mathf.Max(0, MaxHipShift));
        target.Y = -Mathf.Max(0, MaxCrouch) * Effort;
        target *= targetWeight;
        DampedSpring.Step(ref _shift.X, ref _speed.X, target.X, Frequency, Damping, dt);
        DampedSpring.Step(ref _shift.Y, ref _speed.Y, target.Y, Frequency, Damping, dt);
        DampedSpring.Step(ref _shift.Z, ref _speed.Z, target.Z, Frequency, Damping, dt);
        Vector3 horizontal = new Vector3(_shift.X, 0, _shift.Z).LimitLength(Mathf.Max(0, MaxHipShift));
        HipShift = new Vector3(horizontal.X, Mathf.Clamp(_shift.Y, -Mathf.Max(0, MaxCrouch), 0), horizontal.Z) * Weight;

        Transform3D world = skeleton.GetGlobalTransformInterpolated();
        if (_hips >= 0 && !HipShift.IsZeroApprox())
        {
            Transform3D hips = skeleton.GetBoneGlobalPose(_hips);
            hips.Origin += world.Basis.Inverse() * HipShift;
            skeleton.SetBoneGlobalPose(_hips, hips);
        }
        float free = Effort * Weight;
        if (GameEvents.SwordDrawn) { free = 0; }
        float leftTarget = free;
        if (_torch != null && _torch.Lit) { leftTarget = 0; }
        // Ownership changes are immediate: never alter a guarded blade or a lit torch's arm.
        LeftArmWeight = leftTarget;
        RightArmWeight = free;
        Basis toSkeleton = world.Basis.Orthonormalized().Inverse();
        Vector3 forward = (toSkeleton * -Body.GlobalBasis.Z).Normalized();
        Vector3 right = (toSkeleton * Body.GlobalBasis.X).Normalized();
        Arm(skeleton, _left, _leftElbow, forward, right, -1, LeftArmWeight);
        Arm(skeleton, _right, _rightElbow, forward, right, 1, RightArmWeight);
    }

    private void Arm(Skeleton3D skeleton, int arm, int elbow, Vector3 forward, Vector3 right, float side, float weight)
    {
        if (weight <= 0) { return; }
        Rotate(skeleton, arm, new Quaternion(forward, -side * ArmSpread * weight) * new Quaternion(right, ArmForward * weight));
        Rotate(skeleton, elbow, new Quaternion(right, ElbowBend * weight));
    }

    private static void Rotate(Skeleton3D skeleton, int bone, Quaternion rotation)
    {
        if (bone < 0) { return; }
        Transform3D pose = skeleton.GetBoneGlobalPose(bone);
        skeleton.SetBoneGlobalPose(bone, new Transform3D(new Basis(rotation) * pose.Basis, pose.Origin));
    }
}
