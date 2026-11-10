using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Drives the character model's animation from the controller's speed and state. Builds an
/// AnimationTree in code: idle, phase-synchronized walk/jog/sprint with a stride-matched common
/// cadence, a partial crouch for braking, and short authored takeoff/landing transitions. The
/// controller retains all movement authority. Native clip speeds were measured from the planted toe.
/// On top of everything an action slot plays one clip over the whole body (PlayAction), as the dive
/// and the landing roll do; it fades in and out.
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
    /// <summary>Looping pose while airborne (jumps, drops). Falls themselves are ragdolls (PlayerRagdoll).</summary>
    [Export] public StringName AirClip { get; set; } = "Jump";
    [Export] public float AirBlendSharpness { get; set; } = 10.0f;
    [Export] public StringName TakeoffClip { get; set; } = "Jump_Start";
    [Export] public StringName LandingClip { get; set; } = "Jump_Land";
    [Export] public float TakeoffTime { get; set; } = 0.28f;
    [Export] public float LandingTime { get; set; } = 0.45f;
    [Export] public float TransitionFadeTime { get; set; } = 0.09f;
    /// <summary>Running landings retain their stride instead of replacing it with a full stationary crouch.</summary>
    [Export] public float MovingLandingWeight { get; set; } = 0.25f;
    /// <summary>How long an action clip (PlayAction) takes to fade in or out, in s.</summary>
    [Export] public float ActionFadeTime { get; set; } = 0.12f;

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
    /// <summary>Limits the blended gait's average playback rate; low-weight clips share its cadence.</summary>
    [Export] public float MinPlaybackRate { get; set; } = 0.0f;
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
    private const string AirNode = "air";
    private const string AirBlendNode = "air_blend";
    private const string RateSuffix = "_rate";
    private const string TakeoffNode = "takeoff";
    private const string LandingNode = "landing";
    private const string ActionNode = "action";

    // These finite paths belong to the fixed graph below. Keep their native names for the
    // application's lifetime instead of creating finalizable StringName wrappers every frame.
    private static readonly StringName AirBlendParameter = new("parameters/" + AirBlendNode + "/blend_amount");
    private static readonly StringName ToWalkParameter = new("parameters/" + ToWalkNode + "/blend_amount");
    private static readonly StringName ToJogParameter = new("parameters/" + ToJogNode + "/blend_amount");
    private static readonly StringName ToSprintParameter = new("parameters/" + ToSprintNode + "/blend_amount");
    private static readonly StringName BrakeMoveParameter = new("parameters/" + BrakeMoveNode + "/blend_amount");
    private static readonly StringName BrakeParameter = new("parameters/" + BrakeNode + "/blend_amount");
    private static readonly StringName TakeoffBlendParameter = new("parameters/" + TakeoffNode + "_blend/blend_amount");
    private static readonly StringName LandingBlendParameter = new("parameters/" + LandingNode + "_blend/blend_amount");
    private static readonly StringName WalkRateParameter = new("parameters/" + WalkNode + RateSuffix + "/scale");
    private static readonly StringName JogRateParameter = new("parameters/" + JogNode + RateSuffix + "/scale");
    private static readonly StringName SprintRateParameter = new("parameters/" + SprintNode + RateSuffix + "/scale");
    private static readonly StringName BrakeWalkRateParameter = new("parameters/" + BrakeWalkNode + RateSuffix + "/scale");
    private static readonly StringName TakeoffSeekParameter = new("parameters/" + TakeoffNode + "_seek/seek_request");
    private static readonly StringName LandingSeekParameter = new("parameters/" + LandingNode + "_seek/seek_request");

    private AnimationTree? _tree;
    public AnimationTree? Tree => _tree;
    public float SmoothedSpeed => _speed;
    public float Cadence { get; private set; }
    public float TakeoffWeight { get; private set; }
    public float LandingWeight { get; private set; }
    private float _speed;
    private float _brakeWeight;
    private Vector3 _stepOffset;
    private Vector3 _visualHome;
    private float _airWeight;
    private bool _wasAirborne;
    private bool _landingPending;
    private float _takeoffLeft;
    private float _landingLeft;
    private float _walkLength;
    private float _jogLength;
    private float _sprintLength;
    private float _brakeLength;
    private AnimationNodeAnimation? _action;
    private float _actionTime;
    private float _actionSpeed = 1.0f;
    private bool _actionLoops;
    private bool _actionOn;
    private float _actionWeight;

    /// <summary>The clip in the action slot, its place in it (s) and how much of it shows (0–1).</summary>
    public StringName ActionClip { get; private set; } = "";
    public float ActionTime => _actionTime;
    public float ActionWeight => _actionWeight;

    public override void _Ready()
    {
        // Supply this frame's blend parameters before the tree evaluates and CarrierModel copies its offset.
        ProcessPriority = -20;
        if (Visual != null) { _visualHome = Visual.Position; }
        if (Body != null)
        {
            Body.SteppedUp += OnSteppedUp;
            Body.Teleported += ResetMotion;
        }
        GameEvents.Landed += OnLanded;

        if (Animations == null)
        {
            GD.PushError("CharacterAnimator: Animations is not assigned.");
            return;
        }

        _walkLength = Length(WalkClip);
        _jogLength = Length(JogClip);
        _sprintLength = Length(SprintClip);
        _brakeLength = Length(BrakeWalkClip);
        _tree = new AnimationTree { Name = "AnimationTree", TreeRoot = BuildTree(), ProcessPriority = -10 };
        Animations.GetParent().AddChild(_tree);
        _tree.AnimPlayer = _tree.GetPathTo(Animations);
        _tree.Active = true;
    }

    public override void _ExitTree()
    {
        if (Body != null)
        {
            Body.SteppedUp -= OnSteppedUp;
            Body.Teleported -= ResetMotion;
        }
        GameEvents.Landed -= OnLanded;
    }

    private void OnLanded(float speed) { _landingPending = true; }

    private void ResetMotion()
    {
        _speed = 0;
        _stepOffset = Vector3.Zero;
        _brakeWeight = 0;
        _airWeight = 0;
        _wasAirborne = false;
        _landingPending = false;
        _takeoffLeft = 0;
        _landingLeft = 0;
        TakeoffWeight = 0;
        LandingWeight = 0;
        if (Visual != null) { Visual.Position = _visualHome; }
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
        if (!float.IsFinite(dt) || dt <= 0) { return; }
        _stepOffset = _stepOffset.Lerp(Vector3.Zero, 1.0f - Mathf.Exp(-StepSmoothSharpness * dt));
        if (Visual != null)
        {
            Visual.Position = _visualHome + Body.GlobalBasis.Inverse() * _stepOffset;
            // The packing camera stands where the carrier is; hide the model so it doesn't block the view.
            Visual.Visible = !GameEvents.IsPacking;
        }
        Vector3 velocity = Body.Velocity;
        float targetSpeed = new Vector2(velocity.X, velocity.Z).Length();
        _speed = Mathf.Lerp(_speed, targetSpeed, 1.0f - Mathf.Exp(-SpeedSharpness * dt));

        float airTarget = 0.0f;
        if (Body.State == LocomotionState.Airborne)
        {
            airTarget = 1.0f;
        }
        _airWeight = Mathf.Lerp(_airWeight, airTarget, 1.0f - Mathf.Exp(-AirBlendSharpness * dt));
        SetBlend(AirBlendParameter, _airWeight);

        float targetBrake = 0.0f;
        if (Body.State == LocomotionState.Braking || Body.State == LocomotionState.Sliding)
        {
            targetBrake = 1.0f;
        }
        _brakeWeight = Mathf.Lerp(_brakeWeight, targetBrake, 1.0f - Mathf.Exp(-BrakeBlendSharpness * dt));

        SetBlend(ToWalkParameter, Band(_speed, IdleToWalk));
        SetBlend(ToJogParameter, Band(_speed, WalkToJog));
        SetBlend(ToSprintParameter, Band(_speed, JogToSprint));
        SetBlend(BrakeMoveParameter, Band(_speed, IdleToWalk));
        SetBlend(BrakeParameter, _brakeWeight * BrakeCrouchAmount);

        UpdateCadence(targetSpeed);
        UpdateAirTransitions(dt);
        UpdateAction(dt);
    }

    /// <summary>
    /// Plays a clip over the whole body from <paramref name="from"/> s at <paramref name="speed"/>,
    /// fading in (or switching straight over if one is already showing); false if there's no such clip.
    /// </summary>
    public bool PlayAction(StringName clip, float from, float speed, bool loop)
    {
        if (_action == null || Animations?.HasAnimation(clip) != true)
        {
            return false;
        }
        _action.Animation = clip;
        ActionClip = clip;
        _actionTime = from;
        _actionSpeed = speed;
        _actionLoops = loop;
        _actionOn = true;
        return true;
    }

    /// <summary>Fades the action clip out; the normal animation takes over again.</summary>
    public void StopAction()
    {
        _actionOn = false;
    }

    private void UpdateAction(float dt)
    {
        if (_action == null)
        {
            return;
        }
        float target = 0.0f;
        if (_actionOn)
        {
            target = 1.0f;
            float length = Length(ActionClip);
            _actionTime += dt * _actionSpeed;
            if (_actionLoops)
            {
                _actionTime = Mathf.PosMod(_actionTime, length);
            }
            else
            {
                _actionTime = Mathf.Min(_actionTime, length);
            }
        }
        if (ActionFadeTime > 0.0f)
        {
            _actionWeight = Mathf.MoveToward(_actionWeight, target, dt / ActionFadeTime);
        }
        else
        {
            _actionWeight = target;
        }
        _tree!.Set($"parameters/{ActionNode}_seek/seek_request", _actionTime);
        SetBlend(ActionNode + "_blend", Mathf.SmoothStep(0.0f, 1.0f, _actionWeight));
    }

    private float Length(StringName clip)
    {
        if (Animations?.HasAnimation(clip) == true) { return Mathf.Max((float)Animations.GetAnimation(clip).Length, 0.001f); }
        return 1;
    }

    private void UpdateCadence(float groundSpeed)
    {
        float jog = Band(_speed, WalkToJog);
        float sprint = Band(_speed, JogToSprint);
        float brake = _brakeWeight * BrakeCrouchAmount;
        float length = Mathf.Lerp(Mathf.Lerp(_walkLength, _jogLength, jog), _sprintLength, sprint);
        length = Mathf.Lerp(length, _brakeLength, brake);
        float stride = Mathf.Lerp(Mathf.Lerp(WalkClipSpeed * _walkLength, JogClipSpeed * _jogLength, jog), SprintClipSpeed * _sprintLength, sprint);
        stride = Mathf.Lerp(stride, BrakeWalkClipSpeed * _brakeLength, brake);
        Cadence = 0;
        if (groundSpeed > IdleToWalk.X)
        {
            Cadence = Mathf.Clamp(groundSpeed / Mathf.Max(stride, 0.001f), MinPlaybackRate / length, MaxPlaybackRate / length);
        }
        SetCadence(WalkRateParameter);
        SetCadence(JogRateParameter);
        SetCadence(SprintRateParameter);
        SetCadence(BrakeWalkRateParameter);
    }

    private void SetCadence(StringName parameter) { _tree!.Set(parameter, Cadence); }

    private void UpdateAirTransitions(float dt)
    {
        bool airborne = Body!.State == LocomotionState.Airborne;
        if (GameEvents.IsDowned || GameEvents.IsVaulting || Body.Health?.Dead == true)
        {
            _takeoffLeft = 0;
            _landingLeft = 0;
            _landingPending = false;
        }
        else
        {
            if (airborne && !_wasAirborne && Body.Velocity.Y > 0)
            { _takeoffLeft = TakeoffTime; _landingLeft = 0; }
            if (_landingPending && !airborne)
            { _landingLeft = LandingTime; _takeoffLeft = 0; }
        }
        _landingPending = false;
        _wasAirborne = airborne;
        TakeoffWeight = SampleTransition(TakeoffSeekParameter, TakeoffClip, TakeoffTime, ref _takeoffLeft, dt);
        LandingWeight = SampleTransition(LandingSeekParameter, LandingClip, LandingTime, ref _landingLeft, dt);
        LandingWeight *= Mathf.Lerp(1, MovingLandingWeight, Band(_speed, WalkToJog));
        SetBlend(TakeoffBlendParameter, TakeoffWeight);
        SetBlend(LandingBlendParameter, LandingWeight);
    }

    private float SampleTransition(StringName parameter, StringName clip, float duration, ref float left, float dt)
    {
        if (left <= 0 || duration <= 0 || Animations?.HasAnimation(clip) != true) { return 0; }
        left = Mathf.Max(0, left - dt);
        float elapsed = duration - left;
        _tree!.Set(parameter, Mathf.Clamp(elapsed / duration * Length(clip), 0, Length(clip)));
        return Mathf.SmoothStep(0, TransitionFadeTime, elapsed) * Mathf.SmoothStep(0, TransitionFadeTime, left);
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

        tree.AddNode(AirNode, new AnimationNodeAnimation { Animation = AirClip });
        AddBlend(tree, AirBlendNode, BrakeNode, AirNode);

        AddTransition(tree, TakeoffNode, TakeoffClip, AirBlendNode);
        AddTransition(tree, LandingNode, LandingClip, TakeoffNode + "_blend");
        AddTransition(tree, ActionNode, IdleClip, LandingNode + "_blend");
        _action = tree.GetNode(ActionNode) as AnimationNodeAnimation;
        tree.ConnectNode("output", 0, ActionNode + "_blend");
        return tree;
    }

    private void AddTransition(AnimationNodeBlendTree tree, string name, StringName clip, string from)
    {
        if (Animations?.HasAnimation(clip) != true) { clip = IdleClip; }
        tree.AddNode(name, new AnimationNodeAnimation { Animation = clip });
        tree.AddNode(name + "_seek", new AnimationNodeTimeSeek());
        tree.ConnectNode(name + "_seek", 0, name);
        AddBlend(tree, name + "_blend", from, name + "_seek");
    }

    private static void AddScaledClip(AnimationNodeBlendTree tree, string name, StringName clip)
    {
        // Equal normalized timelines plus equal rates keep all gait clips on the same footfall cycle.
        tree.AddNode(name, new AnimationNodeAnimation { Animation = clip, UseCustomTimeline = true,
            TimelineLength = 1, StretchTimeScale = true, LoopMode = Animation.LoopModeEnum.Linear });
        tree.AddNode(name + RateSuffix, new AnimationNodeTimeScale());
        tree.ConnectNode(name + RateSuffix, 0, name);
    }

    private static void AddBlend(AnimationNodeBlendTree tree, string name, string from, string to)
    {
        // Advance even at zero weight; normalized gait timelines share the same cadence.
        tree.AddNode(name, new AnimationNodeBlend2 { Sync = true });
        tree.ConnectNode(name, 0, from);
        tree.ConnectNode(name, 1, to);
    }

    private void SetBlend(StringName parameter, float amount)
    {
        _tree?.Set(parameter, amount);
    }

    /// <summary>0 below band.X, 1 above band.Y, smooth in between.</summary>
    private static float Band(float value, Vector2 band)
    {
        return Mathf.SmoothStep(band.X, band.Y, value);
    }
}
