using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// A beast of the night (BeastDirector spawns and drives it through Tick). It stalks the carrier at
/// a distance with its eyes glowing, now and then crouches and growls (WindUp: the warning), then
/// charges in a nearly straight line, so a sidestep or a dive gets out of the way. A hit throws the
/// carrier into the air (GameEvents.RequestLaunch; the timed roll can take the landing), or, for the
/// she-wolf, tears food out of the pack. Afterwards it backs off and stalks again. Near fire, in a
/// shelter or at dawn it leaves.
/// </summary>
public partial class NightBeast : CharacterBody3D
{
    public enum Mode
    {
        Stalk,
        WindUp,
        Charge,
        Retreat,
        Leave,
    }

    public BeastSpecies? Species { get; private set; }
    public Mode State { get; private set; } = Mode.Stalk;
    /// <summary>Seconds left of being thrown off by a loud noise (the megaphone): it backs away and doesn't attack.</summary>
    public float ConfusedFor { get; private set; }
    public float Radius { get; private set; }
    /// <summary>Seconds until it next attacks (counts down while stalking).</summary>
    public float AttackTimer { get; set; }
    /// <summary>Set by the director before each tick: a food item the carrier has, or empty.</summary>
    public string FoodInPack { get; set; } = "";
    /// <summary>What its last hit did ("threw", or the stolen item's id), cleared by the director.</summary>
    public string LastHit { get; set; } = "";
    public int Hits { get; private set; }
    /// <summary>Seconds since it started to leave (the director lets it vanish after a while, even if stuck).</summary>
    public float LeavingFor { get; private set; }
    public float CarrierRadius { get; set; } = 0.35f;
    public float HitMargin { get; set; } = 0.35f;
    /// <summary>Seconds it has been held up (a tree, a rock, a wall) without getting free; the director moves it after a while.</summary>
    public float StuckFor { get; private set; }
    /// <summary>Held up this long (moving under StuckSpeedShare of the speed it wants), it tries a detour.</summary>
    public float StuckTime { get; set; } = 0.6f;
    public float StuckSpeedShare { get; set; } = 0.3f;
    /// <summary>A detour turns this many degrees off the way it wants to go, for DetourTime seconds, alternating sides.</summary>
    public float DetourAngle { get; set; } = 70.0f;
    public float DetourTime { get; set; } = 1.0f;

    private RandomNumberGenerator _random = new();
    private AnimationPlayer? _animation;
    private string _clip = "";
    private float _timer;
    private float _orbit;
    private Vector3 _chargeDirection;
    private float _gravity = 9.8f;
    private Node3D? _eyes;
    private Vector3 _eyesBase = Vector3.One;
    private float _held;
    private float _detour;
    private float _detourSide = 1.0f;

    public void Setup(BeastSpecies species, ulong seed, uint layer, uint mask)
    {
        Species = species;
        _random.Seed = seed;
        Name = species.DisplayName;
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        CollisionLayer = layer;
        CollisionMask = mask;
        FloorSnapLength = 0.6f;
        FloorMaxAngle = Mathf.DegToRad(species.MaxSlope);
        Vector3 body = species.BodySize;
        Radius = Mathf.Max(0.05f, Mathf.Min(body.X, body.Z) * 0.5f);
        float height = Mathf.Max(body.Y, Radius * 2.0f);
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Radius, Height = height },
            Position = Vector3.Up * height * 0.5f,
        });
        if (species.Model != null)
        {
            Node3D model = species.Model.Instantiate<Node3D>();
            model.Scale = Vector3.One * species.ModelScale;
            model.RotationDegrees = species.ModelRotationDegrees;
            AddChild(model);
            StandardMaterial3D shade = new()
            {
                AlbedoColor = species.Shade,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            foreach (Node node in model.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is MeshInstance3D mesh)
                {
                    mesh.MaterialOverlay = shade;
                }
            }
            foreach (Node node in model.FindChildren("*", "AnimationPlayer", true, false))
            {
                _animation = node as AnimationPlayer;
                break;
            }
        }
        _eyes = new Node3D { Name = "Eyes", Position = species.EyeOffset };
        StandardMaterial3D glow = new()
        {
            AlbedoColor = species.EyeColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = species.EyeColor,
            EmissionEnergyMultiplier = 3.0f,
        };
        foreach (float side in new[] { -0.5f, 0.5f })
        {
            _eyes.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = species.EyeSize, Height = species.EyeSize * 2.0f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = glow,
                Position = Vector3.Right * species.EyeSpacing * side,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        AddChild(_eyes);
        _eyesBase = _eyes.Scale;
        _orbit = _random.RandfRange(0.0f, Mathf.Tau);
        AttackTimer = _random.RandfRange(species.AttackInterval.X, species.AttackInterval.Y);
        Play(species.WalkClip, 1.0f);
    }

    /// <summary>
    /// Advances the beast. <paramref name="mayAttack"/>: the director lets one beast attack at a time
    /// (and none while the carrier is down); <paramref name="safe"/>: fire, shelter or daylight.
    /// </summary>
    public void Tick(float dt, Vector3 carrier, bool mayAttack, bool safe, float hunger, PhysicsDirectSpaceState3D space)
    {
        if (Species == null)
        {
            return;
        }
        Vector3 toCarrier = carrier - GlobalPosition;
        toCarrier.Y = 0.0f;
        float distance = toCarrier.Length();
        Vector3 facing = Vector3.Zero;
        if (distance > 0.01f)
        {
            facing = toCarrier / distance;
        }
        if (safe && State != Mode.Leave)
        {
            State = Mode.Leave;
            LeavingFor = 0.0f;
            Play(Species.RunClip, 1.0f);
        }

        Vector3 walk = Vector3.Zero;
        if (ConfusedFor > 0.0f)
        {
            ConfusedFor = Mathf.Max(0.0f, ConfusedFor - dt);
        }
        switch (State)
        {
            case Mode.Stalk:
                if (ConfusedFor > 0.0f)
                {
                    // Thrown off: shies away from the noise and forgets the attack for now.
                    walk = -facing * Species.StalkSpeed;
                    Turn(-facing, dt);
                    break;
                }
                walk = Stalk(dt, carrier, distance, facing);
                AttackTimer -= dt * hunger;
                if (AttackTimer <= 0.0f && mayAttack)
                {
                    State = Mode.WindUp;
                    _timer = Species.WindUp;
                    Play(Species.IdleClip, 2.5f);
                }
                break;
            case Mode.WindUp:
                Turn(facing, dt * 2.0f);
                _timer -= dt;
                if (_eyes != null)
                {
                    _eyes.Scale = _eyesBase * (1.0f + 0.8f * (1.0f - _timer / Mathf.Max(Species.WindUp, 0.01f)));
                }
                if (_timer <= 0.0f)
                {
                    State = Mode.Charge;
                    _chargeDirection = facing;
                    _timer = distance / Mathf.Max(Species.ChargeSpeed, 0.1f) + 0.8f;
                    Play(Species.RunClip, 1.6f);
                }
                break;
            case Mode.Charge:
                walk = Charge(dt, facing, distance);
                if (State == Mode.Charge && _held > StuckTime * 0.5f)
                {
                    // Ran into something: the charge is over.
                    State = Mode.Retreat;
                    Play(Species.RunClip, 1.0f);
                }
                break;
            case Mode.Retreat:
                walk = -facing * Species.CatchUpSpeed * 0.7f;
                Turn(-facing, dt);
                if (distance >= Species.StalkRadius.Y)
                {
                    BackToStalking();
                }
                break;
            case Mode.Leave:
                LeavingFor += dt;
                walk = -facing * Species.CatchUpSpeed;
                Turn(-facing, dt);
                break;
        }
        if (_detour > 0.0f && State != Mode.Charge)
        {
            _detour -= dt;
            walk = walk.Rotated(Vector3.Up, Mathf.DegToRad(DetourAngle) * _detourSide);
        }
        Step(walk, dt);
        Unstick(walk, dt);
    }

    /// <summary>Notices when it is held up and tries a detour to one side, then the other.</summary>
    private void Unstick(Vector3 walk, float dt)
    {
        float wanted = new Vector2(walk.X, walk.Z).Length();
        Vector3 real = GetRealVelocity();
        float got = new Vector2(real.X, real.Z).Length();
        if (wanted > 1.0f && got < wanted * StuckSpeedShare)
        {
            _held += dt;
            StuckFor += dt;
        }
        else
        {
            _held = 0.0f;
            if (_detour <= 0.0f)
            {
                StuckFor = 0.0f;
            }
        }
        if (_held > StuckTime && State != Mode.Charge)
        {
            _held = 0.0f;
            _detour = DetourTime;
            _detourSide = -_detourSide;
        }
    }

    /// <summary>
    /// A loud noise close by (the megaphone): for these seconds it backs away and doesn't attack; a
    /// crouch or a charge already under way breaks off. A leaving beast just keeps leaving.
    /// </summary>
    public void Confuse(float seconds)
    {
        if (State == Mode.Leave || Species == null)
        {
            return;
        }
        ConfusedFor = Mathf.Max(ConfusedFor, seconds);
        if (State == Mode.WindUp || State == Mode.Charge)
        {
            State = Mode.Retreat;
            if (_eyes != null)
            {
                _eyes.Scale = _eyesBase;
            }
            Play(Species.RunClip, 1.0f);
        }
    }

    /// <summary>Puts it somewhere else (the director's way out when it stays stuck), stalking again.</summary>
    public void Relocate(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        StuckFor = 0.0f;
        _held = 0.0f;
        _detour = 0.0f;
        if (State != Mode.Leave)
        {
            BackToStalking();
        }
    }

    /// <summary>Circles the carrier inside the stalking ring, catching up at a run when left behind.</summary>
    private Vector3 Stalk(float dt, Vector3 carrier, float distance, Vector3 facing)
    {
        BeastSpecies species = Species!;
        _orbit += dt * species.StalkSpeed / Mathf.Max(species.StalkRadius.Y, 1.0f) * 0.6f;
        float ring = (species.StalkRadius.X + species.StalkRadius.Y) * 0.5f;
        Vector3 spot = carrier + new Vector3(Mathf.Sin(_orbit), 0.0f, Mathf.Cos(_orbit)) * ring;
        Vector3 toSpot = spot - GlobalPosition;
        toSpot.Y = 0.0f;
        if (toSpot.Length() >= 0.5f && distance < species.StalkRadius.X)
        {
            // Inside the ring (or its way to the spot crosses near the carrier): bend outward, so it walks round, not through.
            float inside = (species.StalkRadius.X - distance) / Mathf.Max(species.StalkRadius.X, 0.1f);
            // The sideways part keeps it moving when the spot lies straight across the carrier.
            toSpot = toSpot.Normalized() - facing * (inside * 3.0f) + facing.Cross(Vector3.Up) * (inside * 2.0f);
        }
        if (toSpot.Length() < 0.5f)
        {
            Turn(facing, dt);
            Play(species.IdleClip, 1.0f);
            return Vector3.Zero;
        }
        float speed = species.StalkSpeed;
        string clip = species.WalkClip;
        if (distance > species.StalkRadius.Y + 8.0f)
        {
            speed = species.CatchUpSpeed;
            clip = species.RunClip;
        }
        Vector3 direction = toSpot.Normalized();
        Turn(direction, dt);
        Play(clip, 1.0f);
        return direction * speed;
    }

    /// <summary>Runs along the charge, bending only a little toward the carrier; a hit throws them.</summary>
    private Vector3 Charge(float dt, Vector3 facing, float distance)
    {
        BeastSpecies species = Species!;
        float bend = Mathf.DegToRad(species.ChargeTurnSpeed) * dt;
        float angle = _chargeDirection.SignedAngleTo(facing, Vector3.Up);
        if (_chargeDirection.Dot(facing) > 0.0f)
        {
            _chargeDirection = _chargeDirection.Rotated(Vector3.Up, Mathf.Clamp(angle, -bend, bend)).Normalized();
        }
        Turn(_chargeDirection, dt * 3.0f);
        _timer -= dt;
        if (distance < Radius + CarrierRadius + HitMargin)
        {
            Hit();
            return Vector3.Zero;
        }
        if (_timer <= 0.0f)
        {
            // Missed: overshoots, then backs off to stalk again.
            State = Mode.Retreat;
            Play(species.RunClip, 1.0f);
        }
        return _chargeDirection * species.ChargeSpeed;
    }

    private void Hit()
    {
        BeastSpecies species = Species!;
        Hits++;
        if (species.StealsFood && FoodInPack.Length > 0)
        {
            LastHit = FoodInPack;
            GameEvents.RequestDrop(FoodInPack);
            GameEvents.RequestLaunch(_chargeDirection * species.ThrowSpeed * 0.5f, species.ThrowHeight * 0.4f, species.DisplayName);
        }
        else
        {
            LastHit = "threw";
            GameEvents.RequestLaunch(_chargeDirection * species.ThrowSpeed, species.ThrowHeight, species.DisplayName);
        }
        State = Mode.Retreat;
        Play(species.RunClip, 1.0f);
    }

    private void BackToStalking()
    {
        State = Mode.Stalk;
        AttackTimer = _random.RandfRange(Species!.AttackInterval.X, Species.AttackInterval.Y);
        if (_eyes != null)
        {
            _eyes.Scale = _eyesBase;
        }
    }

    private void Step(Vector3 walk, float dt)
    {
        float vertical = Velocity.Y;
        if (IsOnFloor())
        {
            vertical = -0.5f;
        }
        else
        {
            vertical -= _gravity * dt;
        }
        Velocity = new Vector3(walk.X, vertical, walk.Z);
        MoveAndSlide();
    }

    /// <summary>Turns toward a horizontal direction (the beast faces -Z).</summary>
    private void Turn(Vector3 direction, float dt)
    {
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }
        float target = Mathf.Atan2(-direction.X, -direction.Z);
        float step = Mathf.DegToRad(Species!.TurnSpeed) * dt;
        float yaw = Rotation.Y;
        float difference = Mathf.Wrap(target - yaw, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0.0f, yaw + Mathf.Clamp(difference, -step, step), 0.0f);
    }

    private void Play(string clip, float speed)
    {
        if (_animation == null || clip.Length == 0 || !_animation.HasAnimation(clip))
        {
            return;
        }
        _animation.SpeedScale = speed;
        if (_clip == clip)
        {
            return;
        }
        _clip = clip;
        _animation.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
        _animation.Play(clip, 0.2f);
    }
}
