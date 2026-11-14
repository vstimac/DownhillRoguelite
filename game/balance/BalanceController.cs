using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Balance;

/// <summary>
/// Balance of the carrier and its load, modelled as an inverted pendulum about the feet on two axes
/// (roll toward the right, pitch toward forward). Gravity topples the combined centre of mass,
/// sideways load offset adds a constant push, the carrier's acceleration and the slope underfoot
/// add disturbances that grow with load height, and footfalls jolt it. The carrier's muscles pull
/// the tilt toward a target: forward/back load offset is leaned into automatically, sideways
/// mostly isn't, so the player counter-leans (lean buttons) or braces (both buttons). Wind
/// (GameEvents.CurrentWeather) pushes body and pack; a bigger, higher load catches more.
/// Imbalance is the centre of mass over the support area, as a fraction of its half size, and
/// drives stable → wobbling → stumbling → fallen.
/// Reads LocomotionSnapshot, FootPlacementSnapshot and LoadSnapshot events; publishes BalanceSnapshot.
/// </summary>
public partial class BalanceController : Node
{
    private static readonly StringName ActionLeanLeft = "balance_lean_left";
    private static readonly StringName ActionLeanRight = "balance_lean_right";

    // Runs after the player controller in each physics tick, so it sees this tick's movement.
    private const int PhysicsPriority = 10;

    [Export] public BalanceSettings Settings { get; set; } = new();

    /// <summary>When false, lean and brace input are ignored.</summary>
    [Export] public bool InputEnabled { get; set; } = true;

    public BalanceState State { get; private set; } = BalanceState.Stable;
    public BalanceSnapshot Snapshot { get; private set; }

    private float _gravity;
    private Vector2 _tilt;
    private Vector2 _tiltVelocity;
    private Vector2 _pendingImpulse;
    private Vector2 _fallDirection;
    private Vector2 _stumbleDirection;
    private Vector2 _supportHalf;
    private float _lean;
    private float _stateTime;

    private LocomotionSnapshot _locomotion;
    private bool _hasLocomotion;
    private Vector3 _lastPosition;
    private LoadSnapshot _load = LoadSnapshot.Empty;
    private FootPlacementSnapshot _feet;
    private bool _hasFeet;
    private bool _leftWasPlanted;
    private bool _rightWasPlanted;

    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        ProcessPhysicsPriority = PhysicsPriority;
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        _load = GameEvents.CurrentLoad;
        _supportHalf = new Vector2(Settings.MinSupportHalfWidth, Settings.MinSupportHalfLength);

        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.FootPlacementUpdated += OnFootPlacementUpdated;
        GameEvents.LoadChanged += OnLoadChanged;
        GameEvents.DownedChanged += OnDownedChanged;
        GameEvents.Landed += OnLanded;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.FootPlacementUpdated -= OnFootPlacementUpdated;
        GameEvents.LoadChanged -= OnLoadChanged;
        GameEvents.DownedChanged -= OnDownedChanged;
        GameEvents.Landed -= OnLanded;
    }

    /// <summary>A ragdoll fall (from anything) holds balance at Fallen until the carrier is up again.</summary>
    private void OnDownedChanged(bool downed)
    {
        if (downed)
        {
            if (State != BalanceState.Fallen)
            {
                _fallDirection = FallDirection(_tilt);
                EnterState(BalanceState.Fallen);
            }
        }
        else
        {
            ResetBalance();
        }
    }

    /// <summary>Landing pitches the load forward, harder for a heavier, higher load.</summary>
    private void OnLanded(float downwardSpeed)
    {
        if (downwardSpeed <= 0.0f || _load.Mass <= 0.0f || State == BalanceState.Fallen)
        {
            return;
        }
        float impulse = Settings.LandingJolt * downwardSpeed * _load.Mass * _load.CenterOfMass.Y;
        ApplyImpulse(new Vector2(0.0f, impulse / Inertia().Y));
    }

    /// <summary>Adds an instant change of tilt velocity, in rad/s (roll, pitch). Used for knocks and tests.</summary>
    public void ApplyImpulse(Vector2 tiltVelocityChange)
    {
        if (tiltVelocityChange.IsZeroApprox())
        {
            return;
        }
        _sincePush = 0.0f;
        _pendingImpulse += tiltVelocityChange;
    }

    /// <summary>Upright and stable, no motion.</summary>
    public void ResetBalance()
    {
        _tilt = Vector2.Zero;
        _tiltVelocity = Vector2.Zero;
        _pendingImpulse = Vector2.Zero;
        _lean = 0.0f;
        _sincePush = float.PositiveInfinity;
        EnterState(BalanceState.Stable);
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        if (_hasLocomotion && snapshot.Position.DistanceTo(_lastPosition) > Settings.TeleportDistance)
        {
            ResetBalance();
        }
        _locomotion = snapshot;
        _lastPosition = snapshot.Position;
        _hasLocomotion = true;
    }

    private void OnLoadChanged(LoadSnapshot snapshot)
    {
        _load = snapshot;
    }

    private void OnFootPlacementUpdated(FootPlacementSnapshot snapshot)
    {
        if (snapshot.Left.Planted && !_leftWasPlanted)
        {
            QueueFootfall(-1.0f);
        }
        if (snapshot.Right.Planted && !_rightWasPlanted)
        {
            QueueFootfall(1.0f);
        }
        _leftWasPlanted = snapshot.Left.Planted;
        _rightWasPlanted = snapshot.Right.Planted;
        _feet = snapshot;
        _hasFeet = true;
    }

    /// <summary>A foot landing jolts the load sideways toward that foot, and forward on descents.</summary>
    private void QueueFootfall(float side)
    {
        if (!_hasLocomotion || _load.Mass <= 0.0f || State == BalanceState.Fallen)
        {
            return;
        }

        Vector3 forward = CarrierForward();
        float speed = new Vector2(_locomotion.Velocity.X, _locomotion.Velocity.Z).Length();
        float looseness = 1.0f + Settings.LowGripJoltMultiplier * (1.0f - _locomotion.Grip);
        float impulse = Settings.StepJolt * speed * _load.Mass * _load.CenterOfMass.Y * looseness;
        float descent = Mathf.Max(0.0f, _locomotion.GroundNormal.Dot(forward));

        Vector2 inertia = Inertia();
        _pendingImpulse += new Vector2(
            side * impulse / inertia.X,
            impulse * descent * Settings.StepPitchJoltFraction / inertia.Y);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasLocomotion)
        {
            return;
        }

        float dt = (float)delta;
        Vector3 forward = CarrierForward();
        Vector3 right = forward.Cross(Vector3.Up);
        UpdateSupport(forward, right, dt);

        float bodyMass = Settings.BodyMass;
        float mass = bodyMass + _load.Mass;
        float height = (bodyMass * Settings.BodyComHeight + _load.Mass * _load.CenterOfMass.Y) / mass;
        // Load offset of the combined centre of mass from the feet, in carrier axes (right, forward).
        Vector2 offset = new Vector2(_load.CenterOfMass.X, _load.CenterOfMass.Z) * (_load.Mass / mass);
        Vector2 inertia = Inertia();

        float leanTarget = 0.0f;
        bool bracing = false;
        if (InputEnabled && State != BalanceState.Fallen && !GameEvents.IsPacking && !GameEvents.IsMapOpen && !GameEvents.IsScreenOpen && !GameEvents.SwordDrawn)
        {
            bool left = Input.IsActionPressed(ActionLeanLeft);
            bool rightPressed = Input.IsActionPressed(ActionLeanRight);
            bracing = left && rightPressed;
            if (!bracing)
            {
                leanTarget = Input.GetAxis(ActionLeanLeft, ActionLeanRight);
            }
        }
        _lean = Mathf.MoveToward(_lean, leanTarget, Settings.LeanResponse * dt);

        Vector2 gravityTorque = Vector2.Zero;
        Vector2 inertialTorque = Vector2.Zero;
        Vector2 slopeTorque = Vector2.Zero;
        Vector2 windTorque = Vector2.Zero;
        Vector2 correctionTorque = Vector2.Zero;

        if (State == BalanceState.Fallen)
        {
            UpdateFall(dt);
        }
        else
        {
            float stiffness = Settings.Strength;
            if (_run != null)
            {
                // A boon can steady the carrier.
                stiffness *= _run.Modifiers.Balance;
            }
            float dampingScale = 1.0f;
            if (bracing)
            {
                stiffness *= Settings.BraceStiffnessMultiplier;
                dampingScale = Settings.BraceDampingMultiplier;
            }
            if (State == BalanceState.Stumbling)
            {
                stiffness *= Settings.StumbleStiffnessFactor;
            }
            // Standing still, feet planted: steadier, and stopping under one's own power doesn't tip the load.
            bool standing = _locomotion.State == LocomotionState.Idle;
            if (standing)
            {
                stiffness *= Settings.IdleStiffnessMultiplier;
            }

            // Only the load destabilizes: the carrier's own weight is folded into Strength. Using
            // load mass × load height (not a combined centre of mass, which 70 kg of body would
            // dilute) is what makes a high pack measurably harder than a low one.
            float loadMass = _load.Mass;
            float loadHeight = _load.CenterOfMass.Y;
            Vector2 loadOffset = new(_load.CenterOfMass.X, _load.CenterOfMass.Z);
            float loadWeight = loadMass * _gravity;

            // The part of the load offset the carrier handles by itself: it leans that part back over
            // the feet and holds that posture against gravity. What's left over pushes the tilt off
            // target, amplified by how little stiffness exceeds the load's m·g·h: the player's job.
            Vector2 compensated = new(offset.X * Settings.AutoRollCompensation, offset.Y * Settings.AutoPitchCompensation);
            Vector2 compensatedLoad = new(loadOffset.X * Settings.AutoRollCompensation, loadOffset.Y * Settings.AutoPitchCompensation);
            Vector2 target = new(
                -Mathf.Asin(Mathf.Clamp(compensated.X / height, -1.0f, 1.0f)) + _lean * Mathf.DegToRad(Settings.MaxLeanAngle),
                -Mathf.Asin(Mathf.Clamp(compensated.Y / height, -1.0f, 1.0f)));

            gravityTorque = new Vector2(
                loadWeight * (loadHeight * Mathf.Sin(_tilt.X) + loadOffset.X),
                loadWeight * (loadHeight * Mathf.Sin(_tilt.Y) + loadOffset.Y));
            // The pack swinging on its straps (BackpackHarness): a heavy pack swung aside drags the
            // carrier that way, so a full pack's wobble is something to balance against.
            gravityTorque += PackSwayTorque(forward, right);
            Vector2 holdTarget = new(
                loadWeight * (loadHeight * Mathf.Sin(target.X) + compensatedLoad.X),
                loadWeight * (loadHeight * Mathf.Sin(target.Y) + compensatedLoad.Y));

            Vector3 acceleration = _locomotion.Acceleration.LimitLength(Settings.MaxAcceleration);
            Vector2 carrierAcceleration = new(acceleration.Dot(right), acceleration.Dot(forward));
            inertialTorque = -loadMass * loadHeight * Settings.InertialFactor * carrierAcceleration;
            if (standing)
            {
                inertialTorque *= Settings.IdleInertiaFactor;
            }

            Vector3 normal = _locomotion.GroundNormal;
            slopeTorque = loadWeight * loadHeight * new Vector2(
                normal.Dot(right) * Settings.SlopeRollFactor,
                normal.Dot(forward) * Settings.SlopePitchFactor);

            windTorque = WindTorque(forward, right, loadMass, loadHeight);
            if (!windTorque.IsZeroApprox())
            {
                // Wind is an external disturbance: standing still must not grant immunity to it.
                _sincePush = 0.0f;
            }

            float effectiveStiffness = Mathf.Max(stiffness - loadWeight * loadHeight, Settings.MinEffectiveStiffness);
            Vector2 damping = new(
                2.0f * Settings.DampingRatio * Mathf.Sqrt(effectiveStiffness * inertia.X),
                2.0f * Settings.DampingRatio * Mathf.Sqrt(effectiveStiffness * inertia.Y));
            correctionTorque = -stiffness * (_tilt - target) - damping * dampingScale * _tiltVelocity - holdTarget;

            if (State == BalanceState.Stumbling)
            {
                // The body keeps tipping along the stumble unless the player fights it: lean against
                // a sideways stumble, or brace against any stumble.
                gravityTorque += _stumbleDirection * Settings.StumblePushFactor * loadWeight * loadHeight;
                float effort = Mathf.Clamp(-_lean * Mathf.Sign(_stumbleDirection.X), 0.0f, 1.0f) * Mathf.Abs(_stumbleDirection.X);
                if (bracing)
                {
                    effort = 1.0f;
                }
                correctionTorque -= _stumbleDirection * Settings.RecoveryTorque * effort;
            }

            Vector2 torque = gravityTorque + inertialTorque + slopeTorque + windTorque + correctionTorque;
            _tiltVelocity += new Vector2(torque.X / inertia.X, torque.Y / inertia.Y) * dt + _pendingImpulse;
            _pendingImpulse = Vector2.Zero;

            float maxTilt = Mathf.DegToRad(Settings.MaxTiltAngle);
            _tilt += _tiltVelocity * dt;
            _tilt = new Vector2(Mathf.Clamp(_tilt.X, -maxTilt, maxTilt), Mathf.Clamp(_tilt.Y, -maxTilt, maxTilt));
        }

        Vector2 comOffset = new(height * Mathf.Sin(_tilt.X) + offset.X, height * Mathf.Sin(_tilt.Y) + offset.Y);
        Vector2 imbalance = new(comOffset.X / _supportHalf.X, comOffset.Y / _supportHalf.Y);
        if (State != BalanceState.Fallen)
        {
            UpdateState(imbalance, dt);
        }

        Vector3 support = _locomotion.Position;
        Snapshot = new BalanceSnapshot(
            State,
            _stateTime,
            _tilt,
            imbalance,
            _lean,
            bracing,
            forward,
            right,
            support,
            _supportHalf,
            support + right * comOffset.X + forward * comOffset.Y,
            height,
            gravityTorque,
            inertialTorque,
            slopeTorque,
            windTorque,
            correctionTorque,
            _load.Name,
            _load.Mass);
        GameEvents.PublishBalance(Snapshot);
    }

    /// <summary>
    /// Standing still, a stumble from the pack alone steadies itself instead of becoming a fall (the
    /// carrier plants the feet); walking, or after a push (a gust, a landing), it can still go down.
    /// The balance tests turn this off to check the raw model.
    /// </summary>
    [Export] public bool SteadyWhenStanding { get; set; } = true;
    /// <summary>A push within this many seconds still lets a standing stumble become a fall.</summary>
    [Export] public float PushMemory { get; set; } = 1.5f;

    private float _sincePush = float.PositiveInfinity;

    private void UpdateState(Vector2 imbalance, float dt)
    {
        _stateTime += dt;
        _sincePush += dt;
        float magnitude = Mathf.Max(Mathf.Abs(imbalance.X), Mathf.Abs(imbalance.Y));

        if (State == BalanceState.Stumbling && SteadyWhenStanding && _locomotion.State == LocomotionState.Idle && _sincePush > PushMemory
            && (magnitude >= Settings.FallThreshold || _stateTime >= Settings.StumbleWindow))
        {
            // Feet planted: catch it.
            _tiltVelocity *= 0.2f;
            EnterState(BalanceState.Wobbling);
            return;
        }

        if (State == BalanceState.Stumbling)
        {
            if (magnitude >= Settings.FallThreshold || _stateTime >= Settings.StumbleWindow)
            {
                _fallDirection = FallDirection(imbalance);
                EnterState(BalanceState.Fallen);
            }
            else if (magnitude <= Settings.RecoverThreshold)
            {
                EnterState(BalanceState.Wobbling);
            }
            return;
        }

        if (magnitude >= Settings.StumbleThreshold)
        {
            _stumbleDirection = FallDirection(imbalance);
            EnterState(BalanceState.Stumbling);
        }
        else if (magnitude >= Settings.WobbleThreshold)
        {
            if (State != BalanceState.Wobbling)
            {
                EnterState(BalanceState.Wobbling);
            }
        }
        else if (State != BalanceState.Stable)
        {
            EnterState(BalanceState.Stable);
        }
    }

    /// <summary>On the ground: the tilt eases toward the fall pose, then the carrier gets up.</summary>
    private void UpdateFall(float dt)
    {
        _stateTime += dt;
        _tiltVelocity = Vector2.Zero;
        _pendingImpulse = Vector2.Zero;
        Vector2 fallen = _fallDirection * Mathf.DegToRad(Settings.FallTiltAngle);
        _tilt = _tilt.MoveToward(fallen, Settings.FallTiltSpeed * dt);

        // A ragdoll fall ends when the ragdoll stands up (DownedChanged), not on a timer.
        if (!GameEvents.IsDowned && _stateTime >= Settings.FallDuration)
        {
            ResetBalance();
        }
    }

    private static Vector2 FallDirection(Vector2 imbalance)
    {
        if (imbalance.IsZeroApprox())
        {
            return new Vector2(0.0f, 1.0f);
        }
        return imbalance.Normalized();
    }

    private void EnterState(BalanceState state)
    {
        State = state;
        _stateTime = 0.0f;
    }

    /// <summary>Drag of the wind on body and load as torque about the feet, in carrier axes (roll, pitch).</summary>
    private Vector2 WindTorque(Vector3 forward, Vector3 right, float loadMass, float loadHeight)
    {
        Vector3 wind = GameEvents.CurrentWeather.Wind;
        float speed = wind.Length();
        if (speed < Mathf.Epsilon)
        {
            return Vector2.Zero;
        }
        // Force per unit area along the wind, then area times lever arm for body and load.
        Vector3 pressure = wind * (Settings.WindDrag * speed);
        // A cape (GameEvents.CurrentGear.WindCatch) adds area at body height.
        float moment = (Settings.WindBodyArea + GameEvents.CurrentGear.WindCatch) * Settings.WindBodyHeight + Settings.WindAreaPerKg * loadMass * loadHeight;
        return new Vector2(pressure.Dot(right), pressure.Dot(forward)) * moment;
    }

    /// <summary>The pull of the pack's swing about the feet, in carrier axes (roll, pitch), in N·m.</summary>
    private Vector2 PackSwayTorque(Vector3 forward, Vector3 right)
    {
        Vector3 sway = GameEvents.PackSway;
        float mass = GameEvents.PackSwayMass;
        if (mass <= 0.0f || sway.IsZeroApprox() || Settings.PackSwayFactor <= 0.0f)
        {
            return Vector2.Zero;
        }
        float weight = mass * _gravity * Settings.PackSwayFactor;
        return new Vector2(weight * sway.Dot(right), weight * sway.Dot(forward));
    }

    /// <summary>Support half size from how far apart the feet stand, smoothed.</summary>
    private void UpdateSupport(Vector3 forward, Vector3 right, float dt)
    {
        Vector2 target = new(Settings.MinSupportHalfWidth, Settings.MinSupportHalfLength);
        if (_hasFeet && _feet.Left.HasGround && _feet.Right.HasGround)
        {
            Vector3 spread = _feet.Left.GroundPoint - _feet.Right.GroundPoint;
            target = new Vector2(
                Mathf.Max(Settings.MinSupportHalfWidth, Mathf.Abs(spread.Dot(right)) * 0.5f + Settings.FootHalfWidth),
                Mathf.Max(Settings.MinSupportHalfLength, Mathf.Abs(spread.Dot(forward)) * 0.5f + Settings.FootHalfLength));
        }
        _supportHalf = _supportHalf.Lerp(target, 1.0f - Mathf.Exp(-Settings.SupportSharpness * dt));
    }

    /// <summary>Inertia about the feet (roll, pitch) of body plus load, in kg·m².</summary>
    private Vector2 Inertia()
    {
        float body = Settings.BodyMass * Settings.BodyComHeight * Settings.BodyComHeight * Settings.BodyInertiaFactor;
        return new Vector2(body + _load.RollInertia, body + _load.PitchInertia);
    }

    private Vector3 CarrierForward()
    {
        Vector3 forward = _locomotion.Forward;
        if (forward.IsZeroApprox())
        {
            return Vector3.Forward;
        }
        return forward;
    }
}
