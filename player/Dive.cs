using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The dive, an ability (Left Ctrl), and the landing roll (Space while falling).
///
/// Ctrl: a short push-off, a long low arc forward, and a roll out on landing, played with the
/// mannequin's own clips (Jump_Start, Jump, Roll) through the animator's action slot, so every
/// carrier model shows it. The dive moves the body itself; a tree or rock in the way stops the
/// forward travel. If he went off an edge (the point: yeeting off a cliff) the dive hands him to
/// normal falling with his momentum, and the roll cue below can save him.
///
/// Falling fast enough to get hurt, a cue (GameEvents.CurrentVault: its distance is the height above
/// the ground) tightens as the ground comes up; Space in the window primes a roll
/// (GameEvents.RollPrimed): the controller then reports the landing as a soft one (no knockdown, no
/// damage) and the carrier rolls out. A mistimed Space spends the chance.
/// RagdollDive (off by default) throws him as a ragdoll instead.
/// </summary>
public partial class Dive : Node3D
{
    private static readonly StringName ActionDive = "player_dive";
    private static readonly StringName ActionJump = "player_jump";

    [Export] public PlayerController? Body { get; set; }
    /// <summary>Plays the dive's clips on the driving skeleton (its action slot).</summary>
    [Export] public CharacterAnimator? Animator { get; set; }
    [Export] public float StaminaCost { get; set; } = 15.0f;
    /// <summary>A Ctrl press is remembered this long (s), so pressing a moment before landing or while a step bumps the feet off the ground still dives.</summary>
    [Export] public float PressBuffer { get; set; } = 0.25f;
    /// <summary>Still counts as on the ground this long after the feet left it (s): uneven slopes lift them for a frame or two all the time.</summary>
    [Export] public float GroundGrace { get; set; } = 0.2f;
    [Export] public string WindedHint { get; set; } = "Too winded to dive";

    [ExportGroup("Timing (s) and speeds (m/s)")]
    [Export] public float PushOffSeconds { get; set; } = 0.22f;
    [Export] public float PushOffSpeed { get; set; } = 2.0f;
    [Export] public float FlightSeconds { get; set; } = 0.75f;
    [Export] public float FlightSpeed { get; set; } = 5.0f;
    [Export] public float RollSeconds { get; set; } = 1.0f;
    /// <summary>The roll's speed at its start and its end (it slows down as he comes up).</summary>
    [Export] public float RollSpeed { get; set; } = 3.6f;
    [Export] public float RollEndSpeed { get; set; } = 1.2f;

    [ExportGroup("Clips")]
    [Export] public StringName PushOffClip { get; set; } = "Jump_Start";
    /// <summary>Where in the clip the push-off starts (s), and how fast it plays.</summary>
    [Export] public float PushOffFrom { get; set; } = 0.35f;
    [Export] public float PushOffClipSpeed { get; set; } = 2.0f;
    [Export] public StringName FlightClip { get; set; } = "Jump";
    [Export] public float FlightFrom { get; set; } = 0.55f;
    [Export] public float FlightClipSpeed { get; set; } = 1.0f;
    [Export] public StringName RollClip { get; set; } = "Roll";
    [Export] public float RollFrom { get; set; } = 0.0f;
    /// <summary>The roll clip is stretched to RollSeconds unless this is above zero.</summary>
    [Export] public float RollClipSpeed { get; set; } = 0.0f;

    [ExportGroup("Ground")]
    /// <summary>Ground this far below at the landing moment means he went off an edge, in m.</summary>
    [Export] public float EdgeDrop { get; set; } = 0.7f;
    /// <summary>The ground ahead dropping away steeper than this counts as an edge, not a slope (degrees).</summary>
    [Export] public float MaxSlopeDegrees { get; set; } = 40.0f;
    /// <summary>Something this steep in the way (its surface normal's height under this) stops the travel.</summary>
    [Export] public float WallNormalY { get; set; } = 0.5f;
    /// <summary>Heights of the probes ahead (m above his feet) and how far past the step they look (m).</summary>
    [Export] public Vector2 ProbeHeights { get; set; } = new(0.5f, 1.3f);
    [Export] public float ProbeReach { get; set; } = 0.4f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Landing roll")]
    /// <summary>Falling faster than this shows the roll cue, in m/s.</summary>
    [Export] public float RollCueSpeed { get; set; } = 5.0f;
    /// <summary>Heights above the ground where Space primes the roll, and the sweet spot, in m.</summary>
    [Export] public Vector2 RollWindow { get; set; } = new(0.15f, 1.8f);
    [Export] public float RollSweet { get; set; } = 0.7f;

    [ExportGroup("Ragdoll dive")]
    /// <summary>Ctrl throws the carrier as a ragdoll instead of the animated dive.</summary>
    [Export] public bool RagdollDive { get; set; }
    /// <summary>The ragdoll dive's push forward and up (added to how he was moving), in m/s.</summary>
    [Export] public float RagdollForwardSpeed { get; set; } = 5.0f;
    [Export] public float RagdollUpSpeed { get; set; } = 3.2f;
    /// <summary>Extra forward speed for the upper body, so he goes head first, in m/s.</summary>
    [Export] public float RagdollLeanPush { get; set; } = 1.5f;

    public enum Phase { None, PushOff, Flight, Roll }

    public bool Diving { get; private set; }
    public Phase Stage { get; private set; }
    public int Dives { get; private set; }
    public int Rolls { get; private set; }
    /// <summary>Dives whose forward travel something in the way stopped.</summary>
    public int Blocked { get; private set; }

    private Vector3 _forward = Vector3.Forward;
    private float _pressed = -1.0f;
    private float _offGround;
    private Vector3 _direction;
    private float _takeoffY;
    private float _landY;
    private float _time;
    private bool _stopped;
    private bool _spentChance;
    private float _gravity = 9.8f;

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.Rolled += OnRolled;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.Rolled -= OnRolled;
        GameEvents.PublishVaultCue(VaultCue.None);
        if (Diving)
        {
            GameEvents.PublishVaulting(false);
        }
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        Vector3 forward = snapshot.Forward;
        forward.Y = 0.0f;
        if (!forward.IsZeroApprox())
        {
            _forward = forward.Normalized();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Body == null)
        {
            return;
        }
        if (Diving)
        {
            Step((float)delta);
            return;
        }
        float dt = (float)delta;
        bool busy = GameEvents.IsDowned || GameEvents.IsPacking || GameEvents.IsScreenOpen || GameEvents.IsConversing || GameEvents.IsMapOpen;
        if (Body.IsOnFloor())
        {
            _offGround = 0.0f;
        }
        else
        {
            _offGround += dt;
        }
        if (Input.IsActionJustPressed(ActionDive))
        {
            _pressed = PressBuffer;
        }
        if (_pressed >= 0.0f)
        {
            _pressed -= dt;
        }
        if (!busy && _pressed >= 0.0f && _offGround <= GroundGrace)
        {
            _pressed = -1.0f;
            if (!TryDive() && !Diving && Body.Stamina != null && Body.Stamina.Current < StaminaCost)
            {
                GameEvents.PublishHint(WindedHint);
            }
            return;
        }
        UpdateRollCue(busy);
    }

    /// <summary>Starts the dive if there's the stamina for it; false otherwise.</summary>
    public bool TryDive()
    {
        if (Body == null || Diving || (Body.Stamina != null && !Body.Stamina.TrySpend(StaminaCost)))
        {
            return false;
        }
        Dives++;
        if (RagdollDive)
        {
            // Physics takes him: a flat throw forward and up from how he was already moving.
            Vector3 moving = new(Body.Velocity.X, 0.0f, Body.Velocity.Z);
            Vector3 launch = moving + _forward * RagdollForwardSpeed + Vector3.Up * RagdollUpSpeed;
            GameEvents.RequestRagdollLaunch(launch, _forward * RagdollLeanPush, "dive");
            return true;
        }
        _direction = _forward;
        _takeoffY = Body.GlobalPosition.Y;
        // Where the dive comes down: the ground ahead, unless that's a cliff (then the arc keeps its height).
        float reach = PushOffSeconds * PushOffSpeed + FlightSeconds * FlightSpeed;
        float landGround = GroundBelow(Body.GlobalPosition + _direction * reach).Y;
        float slopeAllowance = reach * Mathf.Tan(Mathf.DegToRad(MaxSlopeDegrees));
        _landY = landGround;
        if (landGround < _takeoffY - slopeAllowance)
        {
            _landY = _takeoffY;
        }
        Begin(Phase.PushOff);
        return true;
    }

    private void Begin(Phase phase)
    {
        Diving = true;
        _stopped = false;
        GameEvents.PublishVaultCue(VaultCue.None);
        GameEvents.PublishVaulting(true);
        Enter(phase);
    }

    private void Enter(Phase phase)
    {
        Stage = phase;
        _time = 0.0f;
        if (Animator == null)
        {
            return;
        }
        if (phase == Phase.PushOff)
        {
            Animator.PlayAction(PushOffClip, PushOffFrom, PushOffClipSpeed, false);
        }
        else if (phase == Phase.Flight)
        {
            Animator.PlayAction(FlightClip, FlightFrom, FlightClipSpeed, false);
        }
        else if (phase == Phase.Roll)
        {
            float speed = RollClipSpeed;
            if (speed <= 0.0f)
            {
                speed = ClipLength(RollClip) / Mathf.Max(RollSeconds, 0.05f);
            }
            Animator.PlayAction(RollClip, RollFrom, speed, false);
        }
    }

    private float ClipLength(StringName clip)
    {
        AnimationPlayer? player = Animator?.Animations;
        if (player == null || !player.HasAnimation(clip))
        {
            return 1.0f;
        }
        return (float)player.GetAnimation(clip).Length;
    }

    private void Step(float delta)
    {
        if (Body == null)
        {
            return;
        }
        _time += delta;
        float speed = PushOffSpeed;
        if (Stage == Phase.Flight)
        {
            speed = FlightSpeed;
        }
        else if (Stage == Phase.Roll)
        {
            speed = Mathf.Lerp(RollSpeed, RollEndSpeed, Mathf.Clamp(_time / Mathf.Max(RollSeconds, 0.05f), 0.0f, 1.0f));
        }
        Vector3 from = Body.GlobalPosition;
        Vector3 step = _direction * speed * delta;
        if (_stopped || Blocks(from, step))
        {
            if (!_stopped)
            {
                Blocked++;
            }
            _stopped = true;
            step = Vector3.Zero;
        }
        Vector3 next = from + step;
        Vector3 ground = GroundBelow(next);

        if (Stage == Phase.PushOff)
        {
            Body.GlobalPosition = new Vector3(next.X, ground.Y, next.Z);
            if (_time >= PushOffSeconds)
            {
                _takeoffY = Body.GlobalPosition.Y;
                Enter(Phase.Flight);
            }
            return;
        }
        if (Stage == Phase.Flight)
        {
            // In the air: a jump's arc, back down to the landing height at the end.
            float flight = Mathf.Max(FlightSeconds, 0.05f);
            float since = Mathf.Min(_time, flight);
            float rise = 0.5f * _gravity * flight;
            float y = Mathf.Lerp(_takeoffY, _landY, since / flight) + rise * since - 0.5f * _gravity * since * since;
            Body.GlobalPosition = new Vector3(next.X, Mathf.Max(y, ground.Y), next.Z);
            if (_time < flight)
            {
                return;
            }
            if (ground.Y < _landY - EdgeDrop)
            {
                // Off the edge: from here he falls like anyone, keeping the dive's speed.
                Vector3 velocity = _direction * FlightSpeed + Vector3.Down * (0.5f * _gravity * flight);
                if (_stopped)
                {
                    velocity = Vector3.Down * (0.5f * _gravity * flight);
                }
                Finish(velocity);
                return;
            }
            Body.GlobalPosition = new Vector3(next.X, ground.Y, next.Z);
            Enter(Phase.Roll);
            return;
        }
        // The roll, along the ground.
        Body.GlobalPosition = new Vector3(next.X, ground.Y, next.Z);
        if (_time >= RollSeconds)
        {
            Finish(_direction * RollEndSpeed);
        }
    }

    /// <summary>A tree, rock or wall in the way of this step (at knee and chest height).</summary>
    private bool Blocks(Vector3 from, Vector3 step)
    {
        if (Body == null || step.LengthSquared() < 0.000001f)
        {
            return false;
        }
        Vector3 ahead = step + step.Normalized() * ProbeReach;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        foreach (float height in new[] { ProbeHeights.X, ProbeHeights.Y })
        {
            Vector3 start = from + Vector3.Up * height;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(start, start + ahead, GroundMask,
                new Godot.Collections.Array<Rid> { Body.GetRid() }));
            if (hit.Count > 0 && ((Vector3)hit["normal"]).Y < WallNormalY)
            {
                return true;
            }
        }
        return false;
    }

    private Vector3 GroundBelow(Vector3 point)
    {
        if (Body == null)
        {
            return point;
        }
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 2.0f, point + Vector3.Down * 60.0f, GroundMask, new Godot.Collections.Array<Rid> { Body.GetRid() }));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point + Vector3.Down * 60.0f;
    }

    private void Finish(Vector3 velocity)
    {
        Animator?.StopAction();
        Diving = false;
        Stage = Phase.None;
        GameEvents.PublishVaulting(false);
        if (Body != null)
        {
            Body.Velocity = velocity;
        }
    }

    /// <summary>Falling fast enough to hurt: the cue for a roll, and Space to prime it.</summary>
    private void UpdateRollCue(bool busy)
    {
        if (Body == null || busy || Body.IsOnFloor() || Body.Velocity.Y > -RollCueSpeed || GameEvents.RollPrimed || _spentChance)
        {
            if (Body != null && Body.IsOnFloor())
            {
                _spentChance = false;
            }
            GameEvents.PublishVaultCue(VaultCue.None);
            return;
        }
        float height = Body.GlobalPosition.Y - GroundBelow(Body.GlobalPosition).Y;
        VaultCue cue = new(true, height, RollWindow.X, RollWindow.Y, RollSweet);
        GameEvents.PublishVaultCue(cue);
        if (Input.IsActionJustPressed(ActionJump))
        {
            TryRoll(cue);
        }
    }

    /// <summary>Space while falling: in the window it primes a roll; outside it the chance is gone.</summary>
    public void TryRoll(VaultCue cue)
    {
        GameEvents.PublishVaultCue(VaultCue.None);
        if (cue.InWindow)
        {
            GameEvents.PublishRollPrimed(true);
        }
        else
        {
            _spentChance = true;
            GameEvents.PublishHint("Too early: brace for a hard landing");
        }
    }

    /// <summary>A rolled landing: a roll along the way he was going.</summary>
    private void OnRolled()
    {
        Rolls++;
        if (Body == null || Diving)
        {
            return;
        }
        Vector3 along = Body.Velocity;
        along.Y = 0.0f;
        _direction = _forward;
        if (along.Length() > 0.5f)
        {
            _direction = along.Normalized();
        }
        Begin(Phase.Roll);
    }
}
