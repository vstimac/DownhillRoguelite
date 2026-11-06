using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Kinematic carrier controller. Slope-aware walking with hold-to-brake, downhill momentum that
/// makes stopping harder, speed-dependent turning, step-up onto ledges and sliding on surfaces
/// steeper than the walkable limit. Publishes a LocomotionSnapshot every physics frame.
/// The node's origin is at the feet.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionBack = "player_move_back";
    private static readonly StringName ActionLeft = "player_move_left";
    private static readonly StringName ActionRight = "player_move_right";
    private static readonly StringName ActionBrake = "player_brake";

    // Angle tolerance so a surface exactly at the walkable limit still counts as walkable.
    private const float WalkableAngleTolerance = 0.01f;

    [Export] public LocomotionSettings Settings { get; set; } = new();

    /// <summary>When false, movement input is ignored, e.g. while a debug camera is active.</summary>
    [Export] public bool InputEnabled { get; set; } = true;

    public LocomotionState State { get; private set; } = LocomotionState.Idle;

    /// <summary>Raised after a step-up moves the body instantly, with the world-space jump, so visuals can smooth it.</summary>
    public event System.Action<Vector3>? SteppedUp;

    private float _gravity;
    private Vector3 _previousVelocity;

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        FloorMaxAngle = Mathf.DegToRad(Settings.MaxWalkableAngle);
        FloorSnapLength = Settings.FloorSnapLength;
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        FloorBlockOnWall = true;
    }

    /// <summary>Moves the carrier instantly and clears its momentum.</summary>
    public void Teleport(Vector3 position, float yawRadians)
    {
        GlobalPosition = position;
        Rotation = new Vector3(0.0f, yawRadians, 0.0f);
        Velocity = Vector3.Zero;
        _previousVelocity = Vector3.Zero;
        State = LocomotionState.Idle;
        ResetPhysicsInterpolation();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 input = ReadInputDirection();
        bool braking = InputEnabled && Input.IsActionPressed(ActionBrake);

        if (IsOnFloor())
        {
            Vector3 groundNormal = GetFloorNormal();
            Velocity = ComputeGroundVelocity(input, braking, groundNormal, dt);
            TryStepUp(dt);
        }
        else
        {
            Velocity = ComputeAirVelocity(input, dt);
        }

        MoveAndSlide();

        UpdateState(braking);
        UpdateFacing(input, dt);
        Publish(input, dt);
    }

    private Vector3 ReadInputDirection()
    {
        if (!InputEnabled)
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
        Vector3 horizontal = Flatten(Velocity, false);
        float speed = horizontal.Length();
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
            float alignment = Mathf.Max(0.0f, direction.Dot(desired));
            float target = TargetSpeed(braking, grade) * inputAmount * Mathf.Lerp(1.0f, alignment, Settings.TurnSlowdown);

            float rate = Settings.Acceleration;
            if (speed > target)
            {
                rate = DecelerationRate(braking, grade);
            }
            speed = Mathf.MoveToward(speed, target, rate * dt);
        }
        else
        {
            float grade = direction.Dot(fallLine) * sinSlope;
            speed = Mathf.MoveToward(speed, 0.0f, DecelerationRate(braking, grade) * dt);
        }

        // Horizontal only: floor snap and constant floor speed keep the body on the surface.
        return direction * speed;
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

    private float TargetSpeed(bool braking, float grade)
    {
        float speed = Settings.StrideSpeed;
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

        return Mathf.Min(speed, Settings.MaxSpeed);
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
        if (IsOnFloor())
        {
            float speed = Flatten(Velocity, false).Length();
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
        Vector3 look = Flatten(Velocity, false);
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
            State));
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
        return normal.AngleTo(Vector3.Up) <= Mathf.DegToRad(Settings.MaxWalkableAngle) + WalkableAngleTolerance;
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
