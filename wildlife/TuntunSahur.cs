using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Tuntun Sahur: a wooden log with a bat (TuntunSpawner brings him, Alt+T). He runs at the carrier
/// knocking "tung" with every step, and once close leans back and brings the bat down. The bat is a
/// capsule (handle to tip) that moves with him; when it overlaps the carrier's capsule during the
/// swing, the blow is the bat tip's speed: a hard one knocks the carrier down as a ragdoll
/// (GameEvents.RequestKnockdown; the animation lets go and physics takes over), a soft one only
/// shoves him (RequestLaunch). He has no skeleton: the run and the swing lean the whole log.
/// While the carrier is down he hops in place; then he chases again, until his time is up.
/// </summary>
public partial class TuntunSahur : CharacterBody3D
{
    private enum Phase { Chase, WindUp, Strike, Recover, Gloat }

    [Export(PropertyHint.File, "*.glb")] public string ModelPath { get; set; } = "res://art/creatures/tuntun/tuntun.glb";
    [Export] public string Cause { get; set; } = "Tuntun Sahur";

    [ExportGroup("Running")]
    [Export] public float RunSpeed { get; set; } = 5.5f;
    /// <summary>How fast he turns toward the carrier, in degrees per second.</summary>
    [Export] public float TurnSpeed { get; set; } = 540.0f;
    /// <summary>Distance per step (one knock, one bob), in m.</summary>
    [Export] public float StepLength { get; set; } = 0.9f;
    [Export] public float BobHeight { get; set; } = 0.08f;
    [Export] public float WaddleDegrees { get; set; } = 7.0f;
    /// <summary>Seconds before he leaves on his own.</summary>
    [Export] public float LifeSeconds { get; set; } = 45.0f;
    /// <summary>He leaves when the carrier gets this far away, in m.</summary>
    [Export] public float GiveUpDistance { get; set; } = 120.0f;

    [ExportGroup("Swing")]
    /// <summary>He swings when the carrier's centre is this close, horizontally, in m.</summary>
    [Export] public float StrikeRange { get; set; } = 1.05f;
    [Export] public float WindUpSeconds { get; set; } = 0.45f;
    /// <summary>How far he leans back before the blow, in degrees.</summary>
    [Export] public float WindUpLean { get; set; } = 55.0f;
    /// <summary>How far forward the blow carries him, in degrees.</summary>
    [Export] public float StrikeLean { get; set; } = 30.0f;
    /// <summary>The blow's duration, picked anew each swing (shorter = faster bat = harder hit), in s.</summary>
    [Export] public Vector2 StrikeSeconds { get; set; } = new(0.18f, 0.6f);
    [Export] public float RecoverSeconds { get; set; } = 0.5f;
    /// <summary>Seconds he hops over the downed carrier after he gets up, before chasing again.</summary>
    [Export] public float GloatSeconds { get; set; } = 1.0f;
    /// <summary>Hops per second and hop height (m) while he gloats.</summary>
    [Export] public float HopRate { get; set; } = 1.25f;
    [Export] public float HopHeight { get; set; } = 0.25f;

    [ExportGroup("Bat")]
    /// <summary>The bat's handle and tip in the model's space (front is -Z), in m.</summary>
    [Export] public Vector3 BatHandle { get; set; } = new(0.28f, 0.55f, -0.38f);
    [Export] public Vector3 BatTip { get; set; } = new(0.24f, 0.42f, -0.86f);
    [Export] public float BatRadius { get; set; } = 0.09f;
    /// <summary>The carrier's capsule (as in Player.tscn), from his feet, in m.</summary>
    [Export] public float CarrierRadius { get; set; } = 0.3f;
    [Export] public float CarrierHeight { get; set; } = 1.8f;
    /// <summary>Share of the bat tip's speed the carrier takes.</summary>
    [Export] public float HitTransfer { get; set; } = 0.6f;
    /// <summary>Upward share of the blow (a club knocks you off your feet, not into the ground).</summary>
    [Export] public float LiftShare { get; set; } = 0.35f;
    /// <summary>Blows at least this fast (m/s) make the carrier a ragdoll; slower ones only shove him.</summary>
    [Export] public float RagdollSpeed { get; set; } = 4.5f;
    [Export] public float StaggerHeight { get; set; } = 0.3f;

    [ExportGroup("Body")]
    [Export] public float BodyRadius { get; set; } = 0.3f;
    [Export] public float BodyHeight { get; set; } = 1.9f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Voice")]
    [Export] public string[] KnockSounds { get; set; } =
    {
        "res://audio/creatures/tuntun_knock_0.wav", "res://audio/creatures/tuntun_knock_1.wav", "res://audio/creatures/tuntun_knock_2.wav",
    };
    [Export] public string[] CallSounds { get; set; } = { "res://audio/creatures/tuntun_call_0.wav" };
    [Export] public string[] BonkSounds { get; set; } = { "res://audio/creatures/tuntun_bonk_0.wav", "res://audio/creatures/tuntun_bonk_1.wav" };
    [Export] public float VolumeDb { get; set; } = 4.0f;

    /// <summary>The last blow's speed (m/s) and whether it knocked the carrier down, for tests.</summary>
    public float LastHitSpeed { get; private set; }
    public bool LastHitKnockedDown { get; private set; }
    public int Hits { get; private set; }

    private readonly RandomNumberGenerator _random = new();
    private Node3D? _pivot;
    private AudioStreamPlayer3D? _voice;
    private Vector3 _carrier;
    private bool _carrierKnown;
    private Phase _phase = Phase.Chase;
    private float _phaseTime;
    private float _strikeSeconds;
    private bool _hitThisSwing;
    private Vector3 _previousTip;
    private float _stepDistance;
    private float _age;
    private float _gravity;

    public override void _Ready()
    {
        _random.Randomize();
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        CollisionLayer = 0;
        CollisionMask = GroundMask;
        FloorSnapLength = 0.5f;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = Vector3.Up * BodyHeight * 0.5f,
        });
        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);
        if (ResourceLoader.Load(ModelPath) is PackedScene scene && scene.Instantiate() is Node3D model)
        {
            _pivot.AddChild(model);
        }
        if (DisplayServer.GetName() != "headless")
        {
            _voice = new AudioStreamPlayer3D { Name = "Voice", VolumeDb = VolumeDb, UnitSize = 8.0f, Position = Vector3.Up * 1.2f };
            AddChild(_voice);
        }
        _previousTip = TipWorld();
        GameEvents.LocomotionUpdated += OnLocomotion;
        Say(CallSounds);
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    /// <summary>Tells him where the carrier is (the carrier's feet), as the locomotion events do.</summary>
    public void Track(Vector3 carrierFeet)
    {
        _carrier = carrierFeet;
        _carrierKnown = true;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        Track(snapshot.Position);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _phaseTime += dt;
        Vector3 toCarrier = _carrier - GlobalPosition;
        toCarrier.Y = 0.0f;
        float distance = toCarrier.Length();
        if (_age > LifeSeconds || (_carrierKnown && distance > GiveUpDistance))
        {
            QueueFree();
            return;
        }

        Vector3 velocity = Velocity;
        velocity.X = 0.0f;
        velocity.Z = 0.0f;
        if (!IsOnFloor())
        {
            velocity.Y -= _gravity * dt;
        }
        if (_carrierKnown && distance > 0.01f && _phase != Phase.Strike)
        {
            Face(toCarrier / distance, dt);
        }

        float lean = 0.0f;
        switch (_phase)
        {
            case Phase.Chase:
                if (GameEvents.IsDowned)
                {
                    Enter(Phase.Gloat);
                }
                else if (_carrierKnown && distance <= StrikeRange)
                {
                    Enter(Phase.WindUp);
                }
                else if (_carrierKnown)
                {
                    Vector3 run = -GlobalBasis.Z * RunSpeed;
                    velocity.X = run.X;
                    velocity.Z = run.Z;
                    Step(RunSpeed * dt);
                }
                break;
            case Phase.WindUp:
                lean = WindUpLean * Ease(_phaseTime / Mathf.Max(WindUpSeconds, 0.01f));
                if (_phaseTime >= WindUpSeconds)
                {
                    _strikeSeconds = _random.RandfRange(StrikeSeconds.X, StrikeSeconds.Y);
                    _hitThisSwing = false;
                    _previousTip = TipWorld();
                    Enter(Phase.Strike);
                }
                break;
            case Phase.Strike:
                float t = Mathf.Clamp(_phaseTime / Mathf.Max(_strikeSeconds, 0.01f), 0.0f, 1.0f);
                // Accelerating: the bat is fastest as it comes down.
                lean = Mathf.Lerp(WindUpLean, -StrikeLean, t * t);
                break;
            case Phase.Recover:
                lean = Mathf.Lerp(-StrikeLean, 0.0f, Ease(_phaseTime / Mathf.Max(RecoverSeconds, 0.01f)));
                if (_phaseTime >= RecoverSeconds)
                {
                    Enter(Phase.Chase);
                }
                break;
            case Phase.Gloat:
                if (!GameEvents.IsDowned)
                {
                    if (_phaseTime >= GloatSeconds)
                    {
                        Enter(Phase.Chase);
                    }
                }
                else
                {
                    _phaseTime = 0.0f;
                }
                break;
        }

        Pose(lean, dt);
        Velocity = velocity;
        MoveAndSlide();

        if (_phase == Phase.Strike)
        {
            Vector3 tip = TipWorld();
            if (!_hitThisSwing && BatTouchesCarrier())
            {
                Hit((tip - _previousTip) / Mathf.Max(dt, 0.0001f));
            }
            _previousTip = tip;
            if (_phaseTime >= _strikeSeconds)
            {
                Enter(Phase.Recover);
            }
        }
    }

    private void Enter(Phase phase)
    {
        _phase = phase;
        _phaseTime = 0.0f;
    }

    private void Face(Vector3 direction, float dt)
    {
        float target = Mathf.Atan2(-direction.X, -direction.Z);
        float step = Mathf.DegToRad(TurnSpeed) * dt;
        float difference = Mathf.Wrap(target - Rotation.Y, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0.0f, Rotation.Y + Mathf.Clamp(difference, -step, step), 0.0f);
    }

    /// <summary>A step's worth of running knocks once.</summary>
    private void Step(float distance)
    {
        _stepDistance += distance;
        if (_stepDistance >= StepLength)
        {
            _stepDistance -= StepLength;
            Say(KnockSounds);
        }
    }

    /// <summary>Leans the log (the swing, about his feet), bobs and waddles while running, hops while gloating.</summary>
    private void Pose(float leanDegrees, float dt)
    {
        if (_pivot == null)
        {
            return;
        }
        float phase = _stepDistance / Mathf.Max(StepLength, 0.01f) * Mathf.Pi;
        float bob = 0.0f;
        float waddle = 0.0f;
        if (_phase == Phase.Chase)
        {
            bob = Mathf.Abs(Mathf.Sin(phase)) * BobHeight;
            waddle = Mathf.Sin(phase) * WaddleDegrees;
        }
        else if (_phase == Phase.Gloat)
        {
            bob = Mathf.Abs(Mathf.Sin(_age * Mathf.Pi * 2.0f * HopRate)) * HopHeight;
        }
        _pivot.Position = Vector3.Up * bob;
        _pivot.Rotation = new Vector3(Mathf.DegToRad(leanDegrees), 0.0f, Mathf.DegToRad(waddle));
    }

    private Vector3 TipWorld()
    {
        if (_pivot == null)
        {
            return GlobalPosition;
        }
        return _pivot.GlobalTransform * BatTip;
    }

    /// <summary>Whether the bat's capsule overlaps the carrier's.</summary>
    public bool BatTouchesCarrier()
    {
        if (_pivot == null || !_carrierKnown)
        {
            return false;
        }
        Vector3 handle = _pivot.GlobalTransform * BatHandle;
        Vector3 tip = _pivot.GlobalTransform * BatTip;
        Vector3 bottom = _carrier + Vector3.Up * CarrierRadius;
        Vector3 top = _carrier + Vector3.Up * Mathf.Max(CarrierHeight - CarrierRadius, CarrierRadius);
        Vector3[] closest = Geometry3D.GetClosestPointsBetweenSegments(handle, tip, bottom, top);
        return closest[0].DistanceTo(closest[1]) <= BatRadius + CarrierRadius;
    }

    /// <summary>The blow: the bat tip's speed decides between a ragdoll knockdown and a shove.</summary>
    private void Hit(Vector3 tipVelocity)
    {
        _hitThisSwing = true;
        Hits++;
        float speed = tipVelocity.Length() * HitTransfer;
        Vector3 away = _carrier - GlobalPosition;
        away.Y = 0.0f;
        if (away.LengthSquared() < 0.0001f)
        {
            away = -GlobalBasis.Z;
        }
        away = away.Normalized();
        LastHitSpeed = speed;
        LastHitKnockedDown = speed >= RagdollSpeed;
        Say(BonkSounds);
        if (LastHitKnockedDown)
        {
            GameEvents.RequestKnockdown(away * speed + Vector3.Up * speed * LiftShare, Cause);
        }
        else
        {
            GameEvents.RequestLaunch(away * speed, StaggerHeight, Cause);
        }
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

    private static float Ease(float t)
    {
        float clamped = Mathf.Clamp(t, 0.0f, 1.0f);
        return clamped * clamped * (3.0f - 2.0f * clamped);
    }
}
