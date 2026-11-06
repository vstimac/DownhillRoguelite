using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Drives the character model's animation from the controller's speed and state. Builds an
/// AnimationTree in code: idle, walk, jog and sprint blended by ground speed, each clip's playback
/// rate scaled so its feet move at the body's speed, plus a partial crouch-walk mixed in while
/// braking or sliding for careful steps. Native clip speeds were measured from the planted toe.
/// </summary>
public partial class CharacterAnimator : Node
{
    [Export] public PlayerController? Body { get; set; }

    /// <summary>The model's AnimationPlayer. The tree is created next to it so track paths resolve.</summary>
    [Export] public AnimationPlayer? Animations { get; set; }

    /// <summary>The model root, offset to hide the instant jump of a step-up.</summary>
    [Export] public Node3D? Visual { get; set; }

    /// <summary>How quickly the model catches up with the body after a step-up.</summary>
    [Export] public float StepSmoothSharpness { get; set; } = 18.0f;

    [ExportGroup("Clips")]
    [Export] public StringName IdleClip { get; set; } = "Idle";
    [Export] public StringName WalkClip { get; set; } = "Walk";
    [Export] public StringName JogClip { get; set; } = "Jog_Fwd";
    [Export] public StringName SprintClip { get; set; } = "Sprint";
    [Export] public StringName BrakeIdleClip { get; set; } = "Crouch_Idle";
    [Export] public StringName BrakeWalkClip { get; set; } = "Crouch_Fwd";
    /// <summary>Played once when balance is lost. Placeholder until a proper fall and get-up exist.</summary>
    [Export] public StringName FallClip { get; set; } = "Death01";
    [Export] public float FallFadeIn { get; set; } = 0.15f;
    [Export] public float FallFadeOut { get; set; } = 0.4f;

    [ExportGroup("Native clip speeds (m/s)")]
    [Export] public float WalkClipSpeed { get; set; } = 1.05f;
    [Export] public float JogClipSpeed { get; set; } = 5.9f;
    [Export] public float SprintClipSpeed { get; set; } = 9.0f;
    [Export] public float BrakeWalkClipSpeed { get; set; } = 0.75f;

    [ExportGroup("Blend bands (m/s)")]
    /// <summary>Speed range over which idle blends into walk (x = start, y = end).</summary>
    [Export] public Vector2 IdleToWalk { get; set; } = new(0.05f, 0.4f);
    [Export] public Vector2 WalkToJog { get; set; } = new(1.8f, 2.6f);
    [Export] public Vector2 JogToSprint { get; set; } = new(5.6f, 6.4f);

    [ExportGroup("Playback")]
    /// <summary>Clips never play slower or faster than this, so extreme speeds don't look absurd.</summary>
    [Export] public float MinPlaybackRate { get; set; } = 0.55f;
    [Export] public float MaxPlaybackRate { get; set; } = 1.6f;
    /// <summary>How much of the crouch-walk is mixed in at full brake (0 = none, 1 = full crouch).</summary>
    [Export] public float BrakeCrouchAmount { get; set; } = 0.35f;
    [Export] public float BrakeBlendSharpness { get; set; } = 8.0f;
    [Export] public float SpeedSharpness { get; set; } = 12.0f;

    private const string IdleNode = "idle";
    private const string WalkNode = "walk";
    private const string JogNode = "jog";
    private const string SprintNode = "sprint";
    private const string BrakeIdleNode = "brake_idle";
    private const string BrakeWalkNode = "brake_walk";
    private const string ToWalkNode = "to_walk";
    private const string ToJogNode = "to_jog";
    private const string ToSprintNode = "to_sprint";
    private const string BrakeMoveNode = "brake_move";
    private const string BrakeNode = "brake";
    private const string FallNode = "fall";
    private const string FallShotNode = "fall_shot";
    private const string RateSuffix = "_rate";

    private AnimationTree? _tree;
    private float _speed;
    private float _brakeWeight;
    private float _brakeToWalkCycleRatio = 1.0f;
    private Vector3 _stepOffset;
    private bool _wasFallen;

    public override void _Ready()
    {
        if (Body != null)
        {
            Body.SteppedUp += OnSteppedUp;
        }

        if (Animations == null)
        {
            GD.PushError("CharacterAnimator: Animations is not assigned.");
            return;
        }

        if (Animations.HasAnimation(WalkClip) && Animations.HasAnimation(BrakeWalkClip))
        {
            _brakeToWalkCycleRatio = (float)(Animations.GetAnimation(BrakeWalkClip).Length / Animations.GetAnimation(WalkClip).Length);
        }

        _tree = new AnimationTree { Name = "AnimationTree", TreeRoot = BuildTree() };
        Animations.GetParent().AddChild(_tree);
        _tree.AnimPlayer = _tree.GetPathTo(Animations);
        _tree.Active = true;
    }

    public override void _ExitTree()
    {
        if (Body != null)
        {
            Body.SteppedUp -= OnSteppedUp;
        }
    }

    private void OnSteppedUp(Vector3 jump)
    {
        _stepOffset -= jump;
    }

    public override void _Process(double delta)
    {
        if (_tree == null || Body == null)
        {
            return;
        }

        float dt = (float)delta;
        _stepOffset = _stepOffset.Lerp(Vector3.Zero, 1.0f - Mathf.Exp(-StepSmoothSharpness * dt));
        if (Visual != null)
        {
            Visual.Position = Body.GlobalBasis.Inverse() * _stepOffset;
        }
        Vector3 velocity = Body.Velocity;
        float targetSpeed = new Vector2(velocity.X, velocity.Z).Length();
        _speed = Mathf.Lerp(_speed, targetSpeed, 1.0f - Mathf.Exp(-SpeedSharpness * dt));

        bool fallen = Body.State == LocomotionState.Fallen;
        if (fallen && !_wasFallen)
        {
            _tree.Set($"parameters/{FallShotNode}/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
        }
        else if (!fallen && _wasFallen)
        {
            _tree.Set($"parameters/{FallShotNode}/request", (int)AnimationNodeOneShot.OneShotRequest.FadeOut);
        }
        _wasFallen = fallen;

        float targetBrake = 0.0f;
        if (Body.State == LocomotionState.Braking || Body.State == LocomotionState.Sliding)
        {
            targetBrake = 1.0f;
        }
        _brakeWeight = Mathf.Lerp(_brakeWeight, targetBrake, 1.0f - Mathf.Exp(-BrakeBlendSharpness * dt));

        SetBlend(ToWalkNode, Band(_speed, IdleToWalk));
        SetBlend(ToJogNode, Band(_speed, WalkToJog));
        SetBlend(ToSprintNode, Band(_speed, JogToSprint));
        SetBlend(BrakeMoveNode, Band(_speed, IdleToWalk));
        SetBlend(BrakeNode, _brakeWeight * BrakeCrouchAmount);

        float walkRate = SetRate(WalkNode, WalkClipSpeed);
        SetRate(JogNode, JogClipSpeed);
        SetRate(SprintNode, SprintClipSpeed);

        // The crouch-walk is mixed on top of the walk, so it must put its feet down at the same
        // moments: lock its cycle duration to the walk's instead of matching its own speed.
        _tree.Set($"parameters/{BrakeWalkNode}{RateSuffix}/scale", walkRate * _brakeToWalkCycleRatio);
    }

    private AnimationNodeBlendTree BuildTree()
    {
        AnimationNodeBlendTree tree = new();

        tree.AddNode(IdleNode, new AnimationNodeAnimation { Animation = IdleClip });
        tree.AddNode(BrakeIdleNode, new AnimationNodeAnimation { Animation = BrakeIdleClip });
        AddScaledClip(tree, WalkNode, WalkClip);
        AddScaledClip(tree, JogNode, JogClip);
        AddScaledClip(tree, SprintNode, SprintClip);
        AddScaledClip(tree, BrakeWalkNode, BrakeWalkClip);

        AddBlend(tree, ToWalkNode, IdleNode, WalkNode + RateSuffix);
        AddBlend(tree, ToJogNode, ToWalkNode, JogNode + RateSuffix);
        AddBlend(tree, ToSprintNode, ToJogNode, SprintNode + RateSuffix);
        AddBlend(tree, BrakeMoveNode, BrakeIdleNode, BrakeWalkNode + RateSuffix);
        AddBlend(tree, BrakeNode, ToSprintNode, BrakeMoveNode);

        tree.AddNode(FallNode, new AnimationNodeAnimation { Animation = FallClip });
        tree.AddNode(FallShotNode, new AnimationNodeOneShot { FadeInTime = FallFadeIn, FadeOutTime = FallFadeOut });
        tree.ConnectNode(FallShotNode, 0, BrakeNode);
        tree.ConnectNode(FallShotNode, 1, FallNode);

        tree.ConnectNode("output", 0, FallShotNode);
        return tree;
    }

    private static void AddScaledClip(AnimationNodeBlendTree tree, string name, StringName clip)
    {
        tree.AddNode(name, new AnimationNodeAnimation { Animation = clip });
        tree.AddNode(name + RateSuffix, new AnimationNodeTimeScale());
        tree.ConnectNode(name + RateSuffix, 0, name);
    }

    private static void AddBlend(AnimationNodeBlendTree tree, string name, string from, string to)
    {
        // Sync keeps both inputs advancing at zero weight, so blended cycles stay in phase.
        tree.AddNode(name, new AnimationNodeBlend2 { Sync = true });
        tree.ConnectNode(name, 0, from);
        tree.ConnectNode(name, 1, to);
    }

    private void SetBlend(string node, float amount)
    {
        _tree?.Set($"parameters/{node}/blend_amount", amount);
    }

    private float SetRate(string clipNode, float clipSpeed)
    {
        float rate = Mathf.Clamp(_speed / clipSpeed, MinPlaybackRate, MaxPlaybackRate);
        _tree?.Set($"parameters/{clipNode}{RateSuffix}/scale", rate);
        return rate;
    }

    /// <summary>0 below band.X, 1 above band.Y, smooth in between.</summary>
    private static float Band(float value, Vector2 band)
    {
        return Mathf.SmoothStep(band.X, band.Y, value);
    }
}
