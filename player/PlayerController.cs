using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Kinematic carrier controller. Slope-aware walking with hold-to-brake, downhill momentum that
/// makes stopping harder, speed-dependent turning, step-up onto ledges and sliding on surfaces
/// steeper than the walkable limit. Surface grip (core SurfaceType) scales traction, lowers the
/// walkable limit and adds a downhill creep. Publishes a LocomotionSnapshot every physics frame.
/// The node's origin is at the feet.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionBack = "player_move_back";
    private static readonly StringName ActionLeft = "player_move_left";
    private static readonly StringName ActionRight = "player_move_right";
    private static readonly StringName ActionBrake = "player_brake";
    private static readonly StringName ActionSprint = "player_sprint";
    private static readonly StringName ActionJump = "player_jump";

    // Angle tolerance so a surface exactly at the walkable limit still counts as walkable.
    private const float WalkableAngleTolerance = 0.01f;
    private const string DefaultSurfaceName = "Default";
    // Input below this length doesn't count as moving for sprinting.
    private const float SprintInputThreshold = 0.1f;

    [Export] public LocomotionSettings Settings { get; set; } = new();
    [Export] public CarrierStamina? Stamina { get; set; }
    [Export] public CarrierHealth? Health { get; set; }

    /// <summary>When false, movement input is ignored, e.g. while a debug camera is active.</summary>
    [Export] public bool InputEnabled { get; set; } = true;
    /// <summary>A rolled landing (Dive) counts as this share of its impact speed.</summary>
    [Export] public float RollSoftening { get; set; } = 0.15f;
    /// <summary>Keys move the carrier: input is on and the player isn't typing to the companion.</summary>
    private bool Controls => InputEnabled && !GameEvents.IsConversing;

    public LocomotionState State { get; private set; } = LocomotionState.Idle;
    public bool Sprinting { get; private set; }

    /// <summary>
    /// Steering for tests and automation: a world-space direction used instead of player input
    /// while set (length = input strength). Not used in normal play.
    /// </summary>
    public Vector3? DirectionOverride { get; set; }

    /// <summary>Raised after a step-up moves the body instantly, with the world-space jump, so visuals can smooth it.</summary>
    public event System.Action<Vector3>? SteppedUp;

    /// <summary>Raised after Teleport, so anything holding world positions (ragdoll, planted feet) can reset.</summary>
    public event System.Action? Teleported;

    /// <summary>Grip of the surface underfoot, 0 to 1.</summary>
    public float Grip { get; private set; }

    private float _gravity;
    // Grade of the ground along this frame's walk (+ downhill), for stamina drain.
    private float _moveGrade;
    private Vector3 _previousVelocity;
    private string _surfaceName = DefaultSurfaceName;
    // Surface creep and stumble lurch, kept apart from the carrier's own velocity so they don't accumulate.
    private Vector3 _externalVelocity;

    // Latest balance, from GameEvents: a stumble slows and lurches, bracing stops, a fall disables input.
    private BalanceState _balanceState = BalanceState.Stable;
    private bool _bracing;
    private Vector3 _lurchDirection;

    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        Grip = Settings.DefaultGrip;
        FloorMaxAngle = Mathf.DegToRad(WalkableAngle());
        FloorSnapLength = Settings.FloorSnapLength;
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        FloorBlockOnWall = true;
        GameEvents.BalanceUpdated += OnBalanceUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.BalanceUpdated -= OnBalanceUpdated;
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        _balanceState = snapshot.State;
        _bracing = snapshot.Bracing;
        _lurchDirection = Flatten(snapshot.Right * snapshot.Imbalance.X + snapshot.Forward * snapshot.Imbalance.Y);
    }

    /// <summary>Moves the carrier instantly and clears its momentum.</summary>
    public void Teleport(Vector3 position, float yawRadians)
    {
        GlobalPosition = position;
        Rotation = new Vector3(0.0f, yawRadians, 0.0f);
        Velocity = Vector3.Zero;
        _previousVelocity = Vector3.Zero;
        _externalVelocity = Vector3.Zero;
        State = LocomotionState.Idle;
        ResetPhysicsInterpolation();
        Teleported?.Invoke();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (GameEvents.IsVaulting)
        {
            // The vault moves the body along its animation; nothing to do here until it lands.
            Velocity = Vector3.Zero;
            return;
        }
        if (GameEvents.IsDowned)
        {
            // The ragdoll carries the body around; stand still and report being down.
            Velocity = Vector3.Zero;
            _externalVelocity = Vector3.Zero;
            Sprinting = false;
            State = LocomotionState.Fallen;
            Publish(Vector3.Zero, dt);
            return;
        }

        Vector3 input = ReadInputDirection();
        bool braking = Controls && Input.IsActionPressed(ActionBrake);
        // Out of breath, the carrier still walks, just slower (no sprinting or jumping: CanSprint, TrySpend).
        bool exhausted = Stamina != null && Stamina.Exhausted;
        bool held = _balanceState == BalanceState.Fallen || _bracing || GameEvents.IsPacking;
        if (held)
        {
            input = Vector3.Zero;
        }
        else if (exhausted && Stamina != null)
        {
            input *= Stamina.ExhaustedWalkFactor;
        }
        _moveGrade = 0.0f;
        FloorMaxAngle = Mathf.DegToRad(WalkableAngle());

        bool grounded = IsOnFloor();
        Sprinting = grounded && Controls && !held && !braking && !GameEvents.IsMapOpen
            && input.Length() > SprintInputThreshold
            && Input.IsActionPressed(ActionSprint)
            && Stamina != null && Stamina.CanSprint;

        if (grounded)
        {
            Vector3 groundNormal = GetFloorNormal();
            Velocity = ComputeGroundVelocity(input, braking, groundNormal, dt);
            TryStepUp(dt);
            if (!held)
            {
                TryJump();
            }
        }
        else
        {
            _externalVelocity = Vector3.Zero;
            Velocity = ComputeAirVelocity(input, dt);
        }

        // Walking under your own power costs stamina; standing still lets it recover.
        if (grounded && Stamina != null && input.Length() > SprintInputThreshold && IntendedVelocity().Length() >= Settings.IdleSpeed)
        {
            Stamina.DrainMovement(dt, GameEvents.CurrentLoad.Mass, _moveGrade, braking, Sprinting);
        }

        // Only a real flight counts as a landing: sliding off a steep face onto flat ground doesn't.
        bool wasAirborne = State == LocomotionState.Airborne;
        Vector3 preMoveVelocity = Velocity;
        MoveAndSlide();
        if (wasAirborne && !grounded && IsOnFloor())
        {
            // Speed into the surface, not raw vertical speed, so landing on a slope going with it is softer.
            float impactSpeed = -preMoveVelocity.Dot(GetFloorNormal());
            if (GameEvents.RollPrimed)
            {
                // A timed roll (Dive): the landing is taken by the roll, not the legs.
                GameEvents.PublishRollPrimed(false);
                impactSpeed *= RollSoftening;
                GameEvents.PublishVaulted(true);
                GameEvents.PublishRolled();
            }
            if (impactSpeed >= Settings.MinReportedLanding)
            {
                GameEvents.PublishLanded(impactSpeed);
            }
        }

        UpdateSurface();
        UpdateState(braking);
        UpdateFacing(input, dt);
        Publish(input, dt);
    }

    private Vector3 ReadInputDirection()
    {
        if (DirectionOverride.HasValue)
        {
            return Flatten(DirectionOverride.Value, false).LimitLength(1.0f);
        }
        if (!Controls)
        {
            return Vector3.Zero;
        }

        Vector2 raw = Input.GetVector(ActionLeft, ActionRight, ActionForward, ActionBack);
        if (raw.IsZeroApprox())
        {
            return Vector3.Zero;
        }

        Basis view = Basis.Identity;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            view = camera.GlobalBasis;
        }

        Vector3 forward = Flatten(-view.Z);
        Vector3 right = Flatten(view.X);
        if (forward.IsZeroApprox() || right.IsZeroApprox())
        {
            forward = Vector3.Forward;
            right = Vector3.Right;
        }

        Vector3 direction = right * raw.X + forward * -raw.Y;
        return direction.LimitLength(1.0f);
    }

    private Vector3 ComputeGroundVelocity(Vector3 input, bool braking, Vector3 groundNormal, float dt)
    {
        Vector3 horizontal = IntendedVelocity();
        float speed = horizontal.Length();
        float traction = Mathf.Lerp(Settings.MinTraction, 1.0f, Grip);
        Vector3 direction = Vector3.Zero;
        if (speed > Mathf.Epsilon)
        {
            direction = horizontal / speed;
        }

        float sinSlope = Mathf.Sin(groundNormal.AngleTo(Vector3.Up));
        Vector3 fallLine = HorizontalFallLine(groundNormal);
        float inputAmount = input.Length();

        if (inputAmount > Mathf.Epsilon)
        {
            Vector3 desired = input / inputAmount;
            direction = TurnToward(direction, desired, speed, dt);

            float grade = direction.Dot(fallLine) * sinSlope;
            _moveGrade = grade;
            float alignment = Mathf.Max(0.0f, direction.Dot(desired));
            float target = TargetSpeed(braking, grade) * inputAmount * Mathf.Lerp(1.0f, alignment, Settings.TurnSlowdown);
            if (_balanceState == BalanceState.Stumbling)
            {
                target *= Settings.StumbleSpeedFactor;
            }

            float rate = Settings.Acceleration;
            if (speed > target)
            {
                rate = DecelerationRate(braking, grade);
            }
            speed = Mathf.MoveToward(speed, target, rate * traction * dt);
        }
        else
        {
            // No input: plant the feet and stop within a step (slippery ground a little longer).
            float grade = direction.Dot(fallLine) * sinSlope;
            float rate = Mathf.Max(DecelerationRate(braking, grade), Settings.StopDeceleration);
            if (_balanceState == BalanceState.Fallen)
            {
                rate = Settings.FallDeceleration;
            }
            else if (_bracing)
            {
                rate = Mathf.Max(Settings.BraceDeceleration, Settings.StopDeceleration);
            }
            float stopTraction = Mathf.Lerp(Settings.MinStopTraction, 1.0f, Grip);
            speed = Mathf.MoveToward(speed, 0.0f, rate * stopTraction * dt);
            if (speed < Settings.IdleSpeed)
            {
                speed = 0.0f;
            }
        }

        // Loose ground drags the feet downhill only while walking on it; standing still, they stay put.
        _externalVelocity = Vector3.Zero;
        if (inputAmount > Mathf.Epsilon)
        {
            _externalVelocity = fallLine * Settings.SurfaceCreepSpeed * (1.0f - Grip) * sinSlope;
        }
        if (_balanceState == BalanceState.Stumbling)
        {
            _externalVelocity += _lurchDirection * Settings.StumbleLurchSpeed;
        }

        // Horizontal only: floor snap and constant floor speed keep the body on the surface.
        return direction * speed + _externalVelocity;
    }

    /// <summary>Horizontal velocity the carrier is producing, without surface creep or stumble lurch.</summary>
    private Vector3 IntendedVelocity()
    {
        return Flatten(Velocity, false) - _externalVelocity;
    }

    /// <summary>Walkable angle for the current grip: low grip slides on gentler slopes.</summary>
    private float WalkableAngle()
    {
        return Mathf.Lerp(Settings.LowGripWalkableAngle, Settings.MaxWalkableAngle, Grip);
    }

    /// <summary>Reads the SurfaceType of whatever the body stands or slides on; keeps the last one in the air.</summary>
    private void UpdateSurface()
    {
        if (!FindGround(out GodotObject? ground, out Vector3 point))
        {
            return;
        }

        Grip = Settings.DefaultGrip;
        _surfaceName = DefaultSurfaceName;
        SurfaceType? surface = SurfaceLookup.Find(ground, point);
        if (surface != null)
        {
            Grip = surface.Grip;
            _surfaceName = surface.DisplayName;
        }
        // Rain and snow make every surface slicker; boons can improve footing.
        Grip *= GameEvents.CurrentWeather.GripFactor;
        if (_run != null)
        {
            Grip = Mathf.Min(1.0f, Grip * _run.Modifiers.Grip * (1.0f + GameEvents.CurrentGear.Grip));
        }
    }

    /// <summary>What the body stands or slides on and where it touches; false in the air (keeps the last surface).</summary>
    private bool FindGround(out GodotObject? collider, out Vector3 point)
    {
        // Floor snapping often moves the body without a slide collision, so probe the floor directly.
        if (IsOnFloor())
        {
            KinematicCollision3D probe = new();
            if (TestMove(GlobalTransform, Vector3.Down * Settings.FloorSnapLength, probe))
            {
                collider = probe.GetCollider();
                point = probe.GetPosition();
                return true;
            }
            collider = null;
            point = Vector3.Zero;
            return false;
        }

        float maxSlide = Mathf.DegToRad(Settings.MaxSlideAngle);
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D contact = GetSlideCollision(i);
            if (contact.GetNormal().AngleTo(Vector3.Up) <= maxSlide)
            {
                collider = contact.GetCollider();
                point = contact.GetPosition();
                return true;
            }
        }
        collider = null;
        point = Vector3.Zero;
        return false;
    }

    private Vector3 TurnToward(Vector3 current, Vector3 desired, float speed, float dt)
    {
        if (current.IsZeroApprox() || speed < Settings.TurnInPlaceSpeed)
        {
            return desired;
        }

        float speedFraction = Mathf.Clamp(speed / Settings.MaxSpeed, 0.0f, 1.0f);
        float turnRate = Mathf.Lerp(Settings.TurnRateAtRest, Settings.TurnRateAtMaxSpeed, speedFraction);
        float maxTurn = Mathf.DegToRad(turnRate) * dt;
        float angle = current.SignedAngleTo(desired, Vector3.Up);
        return current.Rotated(Vector3.Up, Mathf.Clamp(angle, -maxTurn, maxTurn));
    }

    /// <summary>Jumps if the button was just pressed and there is stamina for it; a heavier load jumps lower.</summary>
    private void TryJump()
    {
        if (!Controls || !Input.IsActionJustPressed(ActionJump))
        {
            return;
        }

        if (Stamina != null && !Stamina.TrySpend(Stamina.JumpCost))
        {
            return;
        }

        float loadFraction = Mathf.Clamp(GameEvents.CurrentLoad.Mass / Settings.JumpLoadForMinimum, 0.0f, 1.0f);
        float speed = Settings.JumpSpeed * Mathf.Lerp(1.0f, Settings.MinJumpFactor, loadFraction);
        Velocity = new Vector3(Velocity.X, speed, Velocity.Z);
    }

    /// <summary>Speed multiplier from injuries: 1 above InjuredHealthFraction, down to InjuredSpeedFactor at zero.</summary>
    private float InjuryFactor()
    {
        if (Health == null || Health.Max <= 0.0f)
        {
            return 1.0f;
        }
        float fraction = Health.Current / Health.Max;
        if (fraction >= Settings.InjuredHealthFraction)
        {
            return 1.0f;
        }
        return Mathf.Lerp(Settings.InjuredSpeedFactor, 1.0f, fraction / Settings.InjuredHealthFraction);
    }

    /// <summary>
    /// Normal walking speed (no sprint or brake) on ground of <paramref name="grade"/> (+ downhill),
    /// including injury and deep snow: what route estimates assume.
    /// </summary>
    public float WalkingSpeed(float grade)
    {
        float speed = Settings.StrideSpeed;
        if (grade > 0.0f)
        {
            speed *= 1.0f + Settings.DownhillSpeedBonus * grade;
        }
        else
        {
            speed *= Mathf.Max(Settings.MinUphillSpeedFactor, 1.0f + Settings.UphillSpeedPenalty * grade);
        }
        return Mathf.Min(speed * InjuryFactor() * WeatherSpeedFactor(), Settings.MaxSpeed);
    }

    /// <summary>Snow underfoot slows every stride; a boon can lengthen it.</summary>
    public float WeatherSpeedFactor()
    {
        float factor = Mathf.Max(0.0f, 1.0f - Settings.SnowSpeedPenalty * GameEvents.CurrentWeather.Snow);
        if (_run != null)
        {
            factor *= _run.Modifiers.WalkSpeed;
        }
        return factor;
    }

    private float TargetSpeed(bool braking, float grade)
    {
        float speed = Settings.StrideSpeed;
        if (Sprinting)
        {
            speed = Settings.SprintSpeed;
        }
        if (braking)
        {
            speed = Settings.BrakeSpeed;
        }

        if (grade > 0.0f)
        {
            if (!braking)
            {
                speed *= 1.0f + Settings.DownhillSpeedBonus * grade;
            }
        }
        else
        {
            speed *= Mathf.Max(Settings.MinUphillSpeedFactor, 1.0f + Settings.UphillSpeedPenalty * grade);
        }

        // Holding the map up slows the carrier now, but not the route estimates (WalkingSpeed).
        float map = 1.0f;
        if (GameEvents.IsMapOpen)
        {
            map = Settings.MapSpeedFactor;
        }
        return Mathf.Min(speed * InjuryFactor() * WeatherSpeedFactor() * map, Settings.MaxSpeed);
    }

    private float DecelerationRate(bool braking, float grade)
    {
        float rate = Settings.Deceleration;
        if (braking)
        {
            rate = Settings.BrakeDeceleration;
        }

        if (grade > 0.0f)
        {
            rate *= Mathf.Max(Settings.MinDecelerationFactor, 1.0f - Settings.DownhillDecelerationLoss * grade);
        }

        return rate;
    }

    private Vector3 ComputeAirVelocity(Vector3 input, float dt)
    {
        Vector3 horizontal = Flatten(Velocity, false);
        Vector3 desired = input * Settings.StrideSpeed;
        horizontal = horizontal.MoveToward(desired, Settings.Acceleration * Settings.AirControl * dt);

        float vertical = Velocity.Y - _gravity * Settings.GravityScale * dt;
        vertical = Mathf.Max(vertical, -Settings.MaxFallSpeed);

        return new Vector3(horizontal.X, vertical, horizontal.Z);
    }

    /// <summary>
    /// If the body is about to hit a ledge no taller than StepHeight, lifts it onto the ledge.
    /// Walkable slopes are left to MoveAndSlide.
    /// </summary>
    private void TryStepUp(float dt)
    {
        Vector3 horizontal = Flatten(Velocity, false);
        float speed = horizontal.Length();
        if (speed < Settings.MinStepSpeed)
        {
            return;
        }

        Transform3D start = GlobalTransform;
        Vector3 motion = horizontal * dt;
        KinematicCollision3D blocker = new();
        if (!TestMove(start, motion, blocker) || IsWalkable(blocker.GetNormal()))
        {
            return;
        }

        Vector3 up = Vector3.Up * Settings.StepHeight;
        if (TestMove(start, up))
        {
            return;
        }

        Transform3D raised = new(start.Basis, start.Origin + up);
        Vector3 forward = horizontal / speed * Mathf.Max(motion.Length(), Settings.StepForwardProbe);
        if (TestMove(raised, forward))
        {
            return;
        }

        Transform3D advanced = new(raised.Basis, raised.Origin + forward);
        KinematicCollision3D ground = new();
        if (!TestMove(advanced, -up, ground) || !IsWalkable(ground.GetNormal()))
        {
            return;
        }

        Vector3 landing = advanced.Origin + ground.GetTravel();
        SteppedUp?.Invoke(landing - start.Origin);
        GlobalPosition = landing;
    }

    private void UpdateState(bool braking)
    {
        if (_balanceState == BalanceState.Fallen)
        {
            State = LocomotionState.Fallen;
        }
        else if (IsOnFloor())
        {
            float speed = IntendedVelocity().Length();
            if (speed < Settings.IdleSpeed)
            {
                State = LocomotionState.Idle;
            }
            else if (braking)
            {
                State = LocomotionState.Braking;
            }
            else
            {
                State = LocomotionState.Striding;
            }
        }
        else if (FindSteepContact(out _))
        {
            State = LocomotionState.Sliding;
        }
        else
        {
            State = LocomotionState.Airborne;
        }
    }

    private void UpdateFacing(Vector3 input, float dt)
    {
        Vector3 look = IntendedVelocity();
        if (look.Length() < Settings.FacingMinSpeed)
        {
            look = input;
        }
        if (look.IsZeroApprox())
        {
            return;
        }

        float targetYaw = Mathf.Atan2(-look.X, -look.Z);
        float weight = 1.0f - Mathf.Exp(-Settings.FacingSharpness * dt);
        Rotation = new Vector3(0.0f, Mathf.LerpAngle(Rotation.Y, targetYaw, weight), 0.0f);
    }

    private void Publish(Vector3 input, float dt)
    {
        Vector3 velocity = GetRealVelocity();
        Vector3 acceleration = (velocity - _previousVelocity) / dt;
        _previousVelocity = velocity;

        Vector3 normal = Vector3.Up;
        if (IsOnFloor())
        {
            normal = GetFloorNormal();
        }
        else if (FindSteepContact(out Vector3 steep))
        {
            normal = steep;
        }

        GameEvents.PublishLocomotion(new LocomotionSnapshot(
            GlobalPosition,
            velocity,
            acceleration,
            normal,
            Mathf.RadToDeg(normal.AngleTo(Vector3.Up)),
            HorizontalFallLine(normal),
            input,
            State,
            _surfaceName,
            Grip,
            Flatten(-GlobalBasis.Z)));
    }

    /// <summary>Finds a too-steep-to-walk but not wall-like surface touched during the last move.</summary>
    private bool FindSteepContact(out Vector3 normal)
    {
        float maxSlide = Mathf.DegToRad(Settings.MaxSlideAngle);
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            Vector3 candidate = GetSlideCollision(i).GetNormal();
            if (!IsWalkable(candidate) && candidate.AngleTo(Vector3.Up) <= maxSlide)
            {
                normal = candidate;
                return true;
            }
        }

        normal = Vector3.Up;
        return false;
    }

    private bool IsWalkable(Vector3 normal)
    {
        return normal.AngleTo(Vector3.Up) <= Mathf.DegToRad(WalkableAngle()) + WalkableAngleTolerance;
    }

    private static Vector3 HorizontalFallLine(Vector3 normal)
    {
        return Flatten(Vector3.Down.Slide(normal));
    }

    /// <summary>Drops the vertical component; normalizes the result unless asked not to.</summary>
    private static Vector3 Flatten(Vector3 vector, bool normalize = true)
    {
        Vector3 flat = new(vector.X, 0.0f, vector.Z);
        if (!normalize)
        {
            return flat;
        }
        if (flat.IsZeroApprox())
        {
            return Vector3.Zero;
        }
        return flat.Normalized();
    }
}
