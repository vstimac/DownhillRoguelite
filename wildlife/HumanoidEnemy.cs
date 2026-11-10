using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// A humanoid enemy that fights the carrier hand to hand. Its model (any Tripo-rigged humanoid, see
/// EnemySpecies) follows a hidden mannequin that plays the Universal Animation Library's clips
/// (RigFollower), so it idles, walks, runs and sprints at the pace it moves, swings its blows, raises its
/// guard, rolls aside, reels when hit and falls dead.
///
/// It wanders until it sees the carrier, then closes in and attacks. Its blows hurt the carrier
/// (GameEvents.RequestDamage) and shove them, the heaviest knocking them down, unless the carrier's
/// shield takes them (GameEvents.TryBlock), which throws it back. When it sees the carrier swing or kick
/// at it (GameEvents.MeleeSwung) it may guard, taking almost nothing from the front, or roll aside, and
/// then hits back. The carrier's sword finds it as an IMeleeTarget.
/// </summary>
public partial class HumanoidEnemy : CharacterBody3D, IMeleeTarget
{
    public enum Phase { Wander, Chase, Attack, Guard, Dodge, Stagger, Gloat, Dead }

    [Export] public EnemySpecies? Species { get; set; }
    /// <summary>The animated mannequin the model follows (its clips drive every enemy).</summary>
    [Export(PropertyHint.File, "*.glb")] public string DriverPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    /// <summary>Height of the mannequin (m): its clips' strides are for this size.</summary>
    [Export] public float DriverHeight { get; set; } = 1.83f;
    /// <summary>Ground speed of the mannequin's walk, jog and sprint clips at normal speed (m/s).</summary>
    [Export] public float WalkClipSpeed { get; set; } = 1.35f;
    [Export] public float JogClipSpeed { get; set; } = 3.3f;
    [Export] public float SprintClipSpeed { get; set; } = 5.6f;
    /// <summary>Clips for standing, guarding, rolling, reeling and dying.</summary>
    [Export] public string IdleClip { get; set; } = "Idle";
    [Export] public string WalkClip { get; set; } = "Walk";
    [Export] public string JogClip { get; set; } = "Jog_Fwd";
    [Export] public string SprintClip { get; set; } = "Sprint";
    [Export] public string GuardClip { get; set; } = "Punch_Enter";
    [Export] public string DodgeClip { get; set; } = "Roll";
    [Export] public string[] HurtClips { get; set; } = { "Hit_Chest", "Hit_Head" };
    [Export] public string DeathClip { get; set; } = "Death01";
    [Export] public float ClipBlend { get; set; } = 0.18f;
    /// <summary>Playback speed limits for the walk and run clips.</summary>
    [Export] public Vector2 GaitSpeedRange { get; set; } = new(0.6f, 1.6f);
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    /// <summary>The carrier's radius when its blows reach for them, in m.</summary>
    [Export] public float CarrierRadius { get; set; } = 0.3f;
    /// <summary>It stops this share of its reach short of the carrier to swing.</summary>
    [Export] public float StandOffShare { get; set; } = 0.75f;
    /// <summary>How far from the carrier a swing makes it react (beyond its own reach), in m.</summary>
    [Export] public float ReactMargin { get; set; } = 1.2f;
    /// <summary>Pause between wander legs, in s.</summary>
    [Export] public Vector2 WanderPause { get; set; } = new(1.5f, 4.0f);
    /// <summary>After dying it sinks this deep over SinkSeconds before it's removed.</summary>
    [Export] public float SinkDepth { get; set; } = 1.2f;
    [Export] public float SinkSeconds { get; set; } = 2.0f;
    /// <summary>Height of the health bar above its head (m).</summary>
    [Export] public float HealthBarGap { get; set; } = 0.35f;

    /// <summary>Health multiplier (deeper circles make them tougher); set before it enters the tree.</summary>
    public float Toughness { get; set; } = 1.0f;

    public Phase State { get; private set; } = Phase.Wander;
    public float Health { get; private set; }
    public float MaxHealth { get; private set; }
    public EnemyHealthBar? Bar { get; private set; }
    public RigFollower? Follower { get; private set; }
    public AnimationPlayer? Animations { get; private set; }
    public string Clip { get; private set; } = "";
    /// <summary>For tests: blows that reached the carrier, ones their shield stopped, blows it guarded, rolls, wounds.</summary>
    public int HitsLanded { get; private set; }
    public int HitsBlocked { get; private set; }
    public int Guarded { get; private set; }
    public int Dodges { get; private set; }
    public int Wounds { get; private set; }
    public bool Aware => _aware;
    public string Cause => Species?.DisplayName ?? "an enemy";

    private readonly RandomNumberGenerator _random = new();
    private Node3D? _visual;
    private Skeleton3D? _driver;
    private AudioStreamPlayer3D? _voice;
    private Vector3 _carrier;
    private bool _carrierKnown;
    private bool _aware;
    private float _phaseTime;
    private float _cooldown;
    private int _attack;
    private bool _struck;
    private float _attackLength = 1.0f;
    private float _phaseLength;
    private Vector3 _shove;
    private Vector3 _dodgeDirection;
    private Vector3 _home;
    private Vector3 _wanderTarget;
    private float _wanderWait;
    private float _gravity;
    private float _scale = 1.0f;
    private bool _counter;
    /// <summary>Time left in the opening after a parry: it neither swings nor guards nor dodges.</summary>
    private float _opening;

    /// <summary>A new enemy of this kind (add it to the tree, then place it).</summary>
    public static HumanoidEnemy Create(EnemySpecies species)
    {
        return new HumanoidEnemy { Name = "Enemy_" + species.Id, Species = species };
    }

    public override void _Ready()
    {
        _random.Randomize();
        if (Species == null)
        {
            GD.PushWarning("HumanoidEnemy: no species.");
            QueueFree();
            return;
        }
        Health = Species.Health * Toughness;
        MaxHealth = Health;
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        CollisionLayer = 0;
        CollisionMask = GroundMask;
        FloorSnapLength = 0.5f;
        FloorMaxAngle = Mathf.DegToRad(60.0f);
        AddChild(new CollisionShape3D
        {
            Name = "Body",
            Shape = new CapsuleShape3D { Radius = Species.BodyRadius, Height = Mathf.Max(Species.Height, Species.BodyRadius * 2.0f) },
            Position = Vector3.Up * Species.Height * 0.5f,
        });
        BuildVisual();
        if (Species.HealthBar)
        {
            Bar = new EnemyHealthBar { Name = "HealthBar", Position = Vector3.Up * (Species.Height + HealthBarGap) };
            AddChild(Bar);
        }
        if (DisplayServer.GetName() != "headless")
        {
            _voice = new AudioStreamPlayer3D { Name = "Voice", VolumeDb = Species.VolumeDb, PitchScale = Species.VoicePitch, UnitSize = 8.0f, Position = Vector3.Up * Species.Height * 0.8f };
            AddChild(_voice);
        }
        _home = GlobalPosition;
        _wanderTarget = _home;
        _wanderWait = _random.RandfRange(WanderPause.X, WanderPause.Y);
        AddToGroup(IMeleeTarget.Group);
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.MeleeSwung += OnMeleeSwung;
        Play(IdleClip, 0.0f, 1.0f);
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.MeleeSwung -= OnMeleeSwung;
    }

    /// <summary>The mannequin driver (hidden) and the model following it, turned to face the way it walks.</summary>
    private void BuildVisual()
    {
        _visual = new Node3D { Name = "Visual", RotationDegrees = new Vector3(0.0f, 180.0f + Species!.ModelYaw, 0.0f) };
        AddChild(_visual);
        if (ResourceLoader.Load(DriverPath) is PackedScene driverScene && driverScene.Instantiate() is Node3D driver)
        {
            driver.Name = "Driver";
            // The driver's size only matters for its clips' strides; it is hidden.
            driver.Scale = Vector3.One * (Species.Height / Mathf.Max(DriverHeight, 0.1f));
            _visual.AddChild(driver);
            foreach (Node node in driver.FindChildren("*", "Skeleton3D", true, false))
            {
                _driver = node as Skeleton3D;
                break;
            }
            foreach (Node node in driver.FindChildren("*", "AnimationPlayer", true, false))
            {
                Animations = node as AnimationPlayer;
                break;
            }
        }
        if (ResourceLoader.Load(Species.ModelPath) is not PackedScene modelScene || modelScene.Instantiate() is not Node3D model)
        {
            GD.PushWarning($"HumanoidEnemy: can't load {Species.ModelPath}.");
            return;
        }
        model.Name = "Model";
        _scale = Species.Height / Mathf.Max(Species.ModelHeight, 0.01f);
        model.Scale = Vector3.One * _scale;
        _visual.AddChild(model);
        Skeleton3D? skeleton = null;
        foreach (Node node in model.FindChildren("*", "Skeleton3D", true, false))
        {
            skeleton = node as Skeleton3D;
            break;
        }
        if (skeleton == null || _driver == null)
        {
            GD.PushWarning($"HumanoidEnemy: {Species.ModelPath} or the driver has no skeleton.");
            return;
        }
        Follower = new RigFollower { Name = "RigFollower", Driver = _driver };
        Follower.UseRigOf(skeleton);
        skeleton.AddChild(Follower);
        HitSkeleton = skeleton;
    }

    // --- Where the carrier is, and what they do ---

    /// <summary>Woken (an item used near it): it knows where the carrier is and comes for them at once.</summary>
    public void Alert(Vector3 carrierFeet)
    {
        _aware = true;
        Track(carrierFeet);
        if (State == Phase.Wander)
        {
            Enter(Phase.Chase);
        }
        Say(Species?.CallSounds ?? System.Array.Empty<string>());
    }

    /// <summary>Tells it where the carrier is (their feet), as the locomotion events do.</summary>
    public void Track(Vector3 carrierFeet)
    {
        _carrier = carrierFeet;
        _carrierKnown = true;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        Track(snapshot.Position);
    }

    /// <summary>The carrier swings or kicks: if it's in front of them and close, it may guard or roll aside.</summary>
    private void OnMeleeSwung(Vector3 feet, Vector3 facing, string kind)
    {
        if (Species == null || State == Phase.Dead || State == Phase.Stagger || State == Phase.Dodge || State == Phase.Guard || _opening > 0.0f)
        {
            return;
        }
        if (State == Phase.Attack && _phaseTime >= Species.Window(_attack).X * _attackLength)
        {
            return;
        }
        Vector3 toMe = GlobalPosition - feet;
        toMe.Y = 0.0f;
        float distance = toMe.Length();
        if (distance > Species.Reach + ReactMargin || distance < 0.01f)
        {
            return;
        }
        if (facing.Dot(toMe / distance) < Mathf.Cos(Mathf.DegToRad(80.0f)))
        {
            return;
        }
        _aware = true;
        Track(feet);
        float roll = _random.Randf();
        if (roll < Species.GuardChance)
        {
            StartGuard();
        }
        else if (roll < Species.GuardChance + Species.DodgeChance)
        {
            StartDodge(toMe / distance);
        }
    }

    // --- Each step ---

    public override void _PhysicsProcess(double delta)
    {
        if (Species == null)
        {
            return;
        }
        float dt = (float)delta;
        _phaseTime += dt;
        _cooldown = Mathf.Max(0.0f, _cooldown - dt);
        _opening = Mathf.Max(0.0f, _opening - dt);
        Vector3 toCarrier = _carrier - GlobalPosition;
        toCarrier.Y = 0.0f;
        float distance = toCarrier.Length();
        Vector3 direction = Vector3.Zero;
        if (distance > 0.001f)
        {
            direction = toCarrier / distance;
        }
        if (_carrierKnown && !_aware && distance <= Species.SightRange && State != Phase.Dead)
        {
            _aware = true;
            Say(Species.CallSounds);
        }
        if (_aware && distance > Species.GiveUpRange && State != Phase.Dead)
        {
            _aware = false;
            _home = GlobalPosition;
            Enter(Phase.Wander);
        }

        Vector3 wanted = Vector3.Zero;
        switch (State)
        {
            case Phase.Wander:
                wanted = Wander(dt);
                if (_aware)
                {
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Chase:
                wanted = Chase(direction, distance, dt);
                break;
            case Phase.Attack:
                Face(direction, dt, 0.35f);
                UpdateAttack(direction, distance);
                break;
            case Phase.Guard:
                Face(direction, dt, 1.0f);
                if (_phaseTime >= Species.GuardSeconds)
                {
                    // Out of the guard straight into a blow.
                    _cooldown = 0.0f;
                    _counter = true;
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Dodge:
                wanted = _dodgeDirection * Species.DodgeSpeed * (1.0f - Mathf.Clamp(_phaseTime / Mathf.Max(_phaseLength, 0.01f), 0.0f, 1.0f));
                if (_phaseTime >= _phaseLength)
                {
                    _cooldown = 0.0f;
                    _counter = true;
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Stagger:
                if (_phaseTime >= _phaseLength)
                {
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Gloat:
                if (!GameEvents.IsDowned)
                {
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Dead:
                Sink(dt);
                return;
        }
        Move(wanted, dt);
        if (State == Phase.Wander || State == Phase.Chase || State == Phase.Gloat)
        {
            Gait();
        }
    }

    private Vector3 Wander(float dt)
    {
        Vector3 to = _wanderTarget - GlobalPosition;
        to.Y = 0.0f;
        if (to.Length() < 0.5f)
        {
            _wanderWait -= dt;
            if (_wanderWait <= 0.0f)
            {
                float angle = _random.RandfRange(0.0f, Mathf.Tau);
                float radius = _random.RandfRange(0.3f, 1.0f) * Species!.WanderRadius;
                _wanderTarget = _home + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * radius;
                _wanderWait = _random.RandfRange(WanderPause.X, WanderPause.Y);
            }
            return Vector3.Zero;
        }
        Vector3 way = to.Normalized();
        Face(way, dt, 0.5f);
        return way * Species!.WalkSpeed;
    }

    private Vector3 Chase(Vector3 direction, float distance, float dt)
    {
        if (GameEvents.IsDowned)
        {
            Enter(Phase.Gloat);
            return Vector3.Zero;
        }
        Face(direction, dt, 1.0f);
        float standOff = Species!.Reach * StandOffShare;
        bool facing = (-GlobalBasis.Z).Dot(direction) > Mathf.Cos(Mathf.DegToRad(Species.ReachAngle));
        if (distance <= Species.Reach && facing && _cooldown <= 0.0f)
        {
            StartAttack();
            return Vector3.Zero;
        }
        if (distance <= standOff)
        {
            return Vector3.Zero;
        }
        float speed = Species.RunSpeed;
        if (distance > Species.SprintBeyond)
        {
            speed = Species.SprintSpeed;
        }
        else if (distance < standOff + 1.5f)
        {
            speed = Species.WalkSpeed;
        }
        return direction * speed;
    }

    private void Move(Vector3 wanted, float dt)
    {
        Vector3 velocity = Velocity;
        Vector3 flat = new(velocity.X - _shove.X, 0.0f, velocity.Z - _shove.Z);
        flat = flat.MoveToward(wanted, Species!.Acceleration * dt);
        _shove = _shove.MoveToward(Vector3.Zero, (_shove.Length() * Species.ShoveDrag + 0.5f) * dt);
        velocity.X = flat.X + _shove.X;
        velocity.Z = flat.Z + _shove.Z;
        if (IsOnFloor())
        {
            velocity.Y = Mathf.Min(velocity.Y, 0.0f);
        }
        else
        {
            velocity.Y -= _gravity * dt;
        }
        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>Idle, walk, jog or sprint by how fast it really moves, the clip sped up or slowed to match.</summary>
    private void Gait()
    {
        Vector3 real = GetRealVelocity();
        float speed = new Vector2(real.X, real.Z).Length();
        float stride = Species!.Height / Mathf.Max(DriverHeight, 0.1f);
        string clip = IdleClip;
        float clipSpeed = 1.0f;
        if (speed > 0.25f)
        {
            clip = WalkClip;
            clipSpeed = WalkClipSpeed * stride;
            if (speed > (WalkClipSpeed + JogClipSpeed) * 0.5f * stride)
            {
                clip = JogClip;
                clipSpeed = JogClipSpeed * stride;
            }
            if (speed > (JogClipSpeed + SprintClipSpeed) * 0.5f * stride)
            {
                clip = SprintClip;
                clipSpeed = SprintClipSpeed * stride;
            }
        }
        float rate = 1.0f;
        if (clip != IdleClip)
        {
            rate = Mathf.Clamp(speed / Mathf.Max(clipSpeed, 0.01f), GaitSpeedRange.X, GaitSpeedRange.Y);
        }
        Play(clip, ClipBlend, rate);
    }

    // --- Blows ---

    private void StartAttack()
    {
        if (Species!.Attacks.Length == 0)
        {
            return;
        }
        _attack = _random.RandiRange(0, Species.Attacks.Length - 1);
        if (_counter)
        {
            // A counter is the quick first blow.
            _attack = 0;
            _counter = false;
        }
        float speed = Mathf.Max(Species.Speed(_attack), 0.05f);
        Play(Species.Attacks[_attack], ClipBlend * 0.5f, speed, true);
        _attackLength = ClipLength(Species.Attacks[_attack]) / speed;
        _struck = false;
        Enter(Phase.Attack);
        Say(Species.SwingSounds);
    }

    private void UpdateAttack(Vector3 direction, float distance)
    {
        Vector2 window = Species!.Window(_attack);
        if (!_struck && _phaseTime >= window.X * _attackLength && _phaseTime <= window.Y * _attackLength)
        {
            bool inReach = distance <= Species.Reach + CarrierRadius;
            bool inFront = (-GlobalBasis.Z).Dot(direction) >= Mathf.Cos(Mathf.DegToRad(Species.ReachAngle));
            if (_carrierKnown && inReach && inFront && !GameEvents.IsDowned)
            {
                _struck = true;
                Land(direction);
            }
        }
        if (_phaseTime >= _attackLength)
        {
            _cooldown = Species.AttackCooldown * _random.RandfRange(0.7f, 1.3f);
            Enter(Phase.Chase);
        }
    }

    /// <summary>The blow lands: the carrier's shield takes it (it reels back), or it hurts and shoves them.</summary>
    private void Land(Vector3 away)
    {
        if (away.LengthSquared() < 0.0001f)
        {
            away = -GlobalBasis.Z;
        }
        if (GameEvents.TryBlock(away, Cause))
        {
            HitsBlocked++;
            Say(Species!.GuardSounds);
            _shove = -away * Species.BlockedRecoil;
            // Parried: it reels, and the next swing waits, so the carrier can strike back safely.
            Reel(Species.ParriedStaggerSeconds);
            _cooldown = Species.ParriedStaggerSeconds + Species.ParriedOpening;
            _opening = _cooldown;
            _counter = false;
            return;
        }
        HitsLanded++;
        float damage = Species!.Damage(_attack);
        float push = Species.Push(_attack);
        Say(Species.HitSounds);
        GameEvents.RequestDamage(damage, Cause);
        GameEvents.RequestCameraShake(0.05f + push * 0.02f);
        if (push >= Species.KnockdownPush)
        {
            GameEvents.RequestKnockdown(away * push + Vector3.Up * push * Species.LiftShare, Cause);
        }
        else
        {
            GameEvents.RequestLaunch(away * push, Species.StaggerHeight, Cause);
        }
    }

    // --- Defence ---

    private void StartGuard()
    {
        Play(GuardClip, ClipBlend * 0.5f, 1.6f, true);
        Enter(Phase.Guard);
    }

    /// <summary>Rolls to the side of the carrier's blow (left or right at random).</summary>
    private void StartDodge(Vector3 fromCarrier)
    {
        Vector3 side = fromCarrier.Cross(Vector3.Up).Normalized();
        if (_random.Randf() < 0.5f)
        {
            side = -side;
        }
        // A little away from the carrier too.
        _dodgeDirection = (side + fromCarrier * 0.5f).Normalized();
        Rotation = new Vector3(0.0f, Mathf.Atan2(-_dodgeDirection.X, -_dodgeDirection.Z), 0.0f);
        Play(DodgeClip, ClipBlend * 0.5f, 1.2f, true);
        _phaseLength = ClipLength(DodgeClip) / 1.2f;
        Dodges++;
        Enter(Phase.Dodge);
    }

    // --- IMeleeTarget: the carrier's sword ---

    public Vector3 HitBase => GlobalPosition;
    public float HitHeight => Species?.Height ?? 2.0f;
    public float HitRadius => Species?.BodyRadius ?? 0.4f;
    public bool CanBeHit => State != Phase.Dead && State != Phase.Dodge && Health > 0.0f;
    /// <summary>The shown model's skeleton: blows land on its body (BoneHitboxes).</summary>
    public Skeleton3D? HitSkeleton { get; private set; }

    /// <summary>
    /// A sword blow or kick. Into its raised guard from the front it takes only a sliver; otherwise it
    /// loses the health, is shoved and reels (a blow mid-swing breaks the swing off), and at 0 it dies.
    /// </summary>
    public void TakeHit(float damage, Vector3 push, string source)
    {
        if (!CanBeHit || Species == null)
        {
            return;
        }
        _aware = true;
        Vector3 from = -new Vector3(push.X, 0.0f, push.Z);
        bool fromFront = from.LengthSquared() > 0.0001f && (-GlobalBasis.Z).Dot(from.Normalized()) >= Mathf.Cos(Mathf.DegToRad(Species.GuardAngle));
        if (State == Phase.Guard && fromFront)
        {
            Guarded++;
            Health = Mathf.Max(0.0f, Health - damage * Species.GuardedDamage);
            ShowHealth();
            _shove = new Vector3(push.X, 0.0f, push.Z) * 0.4f;
            Say(Species.GuardSounds);
            // The guard holds a moment longer.
            _phaseTime = Mathf.Min(_phaseTime, Species.GuardSeconds * 0.5f);
            if (Health <= 0.0f)
            {
                Die();
            }
            return;
        }
        Wounds++;
        Health = Mathf.Max(0.0f, Health - damage);
        ShowHealth();
        _shove = new Vector3(push.X, 0.0f, push.Z);
        Say(Species.HurtSounds);
        if (Health <= 0.0f)
        {
            Die();
            return;
        }
        float seconds = Species.StaggerSeconds;
        if (damage >= Species.HeavyHit)
        {
            seconds = Species.HeavyStaggerSeconds;
        }
        Reel(seconds);
    }

    private void Reel(float seconds)
    {
        if (HurtClips.Length > 0)
        {
            string clip = HurtClips[_random.RandiRange(0, HurtClips.Length - 1)];
            float length = ClipLength(clip);
            Play(clip, ClipBlend * 0.4f, Mathf.Max(length / Mathf.Max(seconds, 0.1f), 0.5f), true);
        }
        _phaseLength = seconds;
        Enter(Phase.Stagger);
    }

    private void ShowHealth()
    {
        Bar?.Set(Health / Mathf.Max(MaxHealth, 0.01f));
    }

    private void Die()
    {
        if (Bar != null)
        {
            Bar.Visible = false;
        }
        Enter(Phase.Dead);
        Play(DeathClip, ClipBlend * 0.5f, 1.0f, true);
        Velocity = Vector3.Zero;
        RemoveFromGroup(IMeleeTarget.Group);
    }

    /// <summary>Lies a while, then sinks into the ground and is gone.</summary>
    private void Sink(float dt)
    {
        if (_phaseTime < Species!.CorpseSeconds)
        {
            if (!IsOnFloor())
            {
                Velocity = new Vector3(0.0f, Velocity.Y - _gravity * dt, 0.0f);
                MoveAndSlide();
            }
            return;
        }
        if (_visual != null)
        {
            _visual.Position += Vector3.Down * SinkDepth / Mathf.Max(SinkSeconds, 0.1f) * dt;
        }
        if (_phaseTime >= Species.CorpseSeconds + SinkSeconds)
        {
            QueueFree();
        }
    }

    // --- Helpers ---

    private void Enter(Phase phase)
    {
        State = phase;
        _phaseTime = 0.0f;
    }

    /// <summary>Turns toward <paramref name="direction"/> at the species' turn speed (times <paramref name="share"/>).</summary>
    private void Face(Vector3 direction, float dt, float share)
    {
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }
        float target = Mathf.Atan2(-direction.X, -direction.Z);
        float step = Mathf.DegToRad(Species!.TurnSpeed * share) * dt;
        float difference = Mathf.Wrap(target - Rotation.Y, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0.0f, Rotation.Y + Mathf.Clamp(difference, -step, step), 0.0f);
    }

    /// <summary>Plays a clip (a one-off <paramref name="restart"/>s even if it is the one playing).</summary>
    private void Play(string clip, float blend, float speed, bool restart = false)
    {
        if (Animations == null || !Animations.HasAnimation(clip))
        {
            return;
        }
        if (restart && Clip == clip)
        {
            Animations.Stop(true);
        }
        else if (Clip == clip && Animations.IsPlaying())
        {
            Animations.SpeedScale = speed;
            return;
        }
        Clip = clip;
        Animations.SpeedScale = speed;
        Animations.Play(clip, blend);
    }

    private float ClipLength(string clip)
    {
        if (Animations == null || !Animations.HasAnimation(clip))
        {
            return 1.0f;
        }
        return (float)Animations.GetAnimation(clip).Length;
    }

    private void Say(string[] sounds)
    {
        if (_voice == null || sounds.Length == 0)
        {
            return;
        }
        if (ResourceLoader.Load(sounds[_random.RandiRange(0, sounds.Length - 1)]) is AudioStream stream)
        {
            _voice.Stream = stream;
            _voice.Play();
        }
    }
}
