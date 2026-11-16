using System;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One animal living on the slope. Most of the time it grazes, walks to the next grazing spot along
/// the slope (a herd follows its leader there, each to its own place, keeping its spacing), stands
/// and looks round, or lies down; with water near (WaterNear) it goes to drink. It sees and hears
/// the carrier by its species' ranges: a moving carrier is seen farther than a still one, a sprint,
/// a fall or a heavy load is heard farther than a quiet walk. Noticing it, it gives a short alert
/// beat (frozen, facing the carrier), then acts on its temperament: skittish ones bolt, the herd with
/// them, away and downhill or along the slope; wary ones walk off and look back; curious ones come to
/// look at a carrier standing still, then lose interest; bold ones hold their ground. Fleeing, it
/// stops at a safe distance, looks back and goes back to grazing (uneasy for a while). Every step
/// ahead is checked for ground it can stand on: no steep banks, no drops, no rocks; with nowhere to
/// go it is cornered and a big one shoves. Pushed for long it gives way a little, then turns round
/// and shoves the carrier into the air (GameEvents.RequestLaunch). A fall or a sword swing nearby
/// scatters it (FleeFrom), a hard landing startles it (Startle).
/// A physics body: a capsule sized to the species walks on the ground with gravity, slides on what's
/// too steep and bumps into rocks, other animals and the carrier; its model leans to the slope
/// underfoot so legs and head don't sink in. Flyers float at their height above the ground. The
/// spawner drives it through Tick so all animals together can be timed; in co-op the host thinks and
/// guests see position, clip and model pose (INetMob). The carrier's sword finds it (IMeleeTarget,
/// on its bones): struck, it's knocked aside and bolts.
/// </summary>
public partial class WildlifeAnimal : CharacterBody3D, IMeleeTarget, INetMob
{
    public bool Puppet { get; set; }
    public int NetKind => 2;
    public string NetSpecies => Species?.ResourcePath ?? "";
    public bool NetPuppet => Puppet;
    public float NetHealth => 1;
    public bool NetDown => false;
    public Transform3D NetVisual => _model?.Transform ?? Transform3D.Identity;
    public void NetShowVisual(Transform3D visual) { if (Puppet && _model != null) { _model.Transform = visual; } }
    public string NetClip => _clip;
    public float NetClipPosition => (float)(_animation?.CurrentAnimationPosition ?? 0);
    public float NetClipSpeed => _animation?.GetPlayingSpeed() ?? 1;
    public void NetShow(string clip, float position, float speed, float health, bool down)
    {
        if (!Puppet) { return; }
        CollisionLayer = 0; CollisionMask = 0;
        Play(clip, speed, -1);
        if (_animation != null && _animation.HasAnimation(clip)
            && Mathf.Abs((float)_animation.CurrentAnimationPosition - position) > 0.25f) { _animation.Seek(position, true); }
    }
    public enum Mode
    {
        /// <summary>Standing, looking round.</summary>
        Idle,
        /// <summary>Walking to the next grazing spot (or to water).</summary>
        Walk,
        /// <summary>Running from the carrier, a fall or a blow.</summary>
        Flee,
        /// <summary>Pushed too long (or cornered): turning on the carrier, about to shove.</summary>
        Shove,
        /// <summary>Head down, eating.</summary>
        Graze,
        /// <summary>Lying down.</summary>
        Rest,
        /// <summary>Head up, frozen, facing what it noticed.</summary>
        Alert,
        /// <summary>Wary: walking away from the carrier, looking back now and then.</summary>
        Retreat,
        /// <summary>Curious: coming to look at a carrier standing still, then sniffing.</summary>
        Approach,
        /// <summary>Stopped after fleeing, looking back before grazing again.</summary>
        LookBack,
        /// <summary>Drinking at water.</summary>
        Drink,
    }

    /// <summary>
    /// Water near a point within a distance, or null. The world has no water yet; whatever adds
    /// streams or ponds sets this and grazing animals go to drink there.
    /// </summary>
    public static Func<Vector3, float, Vector3?>? WaterNear { get; set; }

    /// <summary>Seconds the carrier has been pushing this animal (reset once it calms down).</summary>
    public float PushTime { get; private set; }
    /// <summary>How many times this animal has shoved the carrier.</summary>
    public int Shoves { get; private set; }
    /// <summary>Blows it has taken from the carrier's sword or boot.</summary>
    public int Struck { get; private set; }
    /// <summary>Alert beats, bolts, approaches, look-backs and times cornered so far (for checks and the overlay).</summary>
    public int Alerts { get; private set; }
    public int Flees { get; private set; }
    public int Approaches { get; private set; }
    public int LookBacks { get; private set; }
    public int Cornered { get; private set; }
    public long TargetPeer { get; set; }
    /// <summary>A knock dies away at this rate (m/s per s).</summary>
    [Export] public float KnockDecay { get; set; } = 8.0f;
    /// <summary>The capsule's radius, in m.</summary>
    public float Radius { get; private set; }
    /// <summary>Its herd or flock, if it came in one.</summary>
    public WildlifeHerd? Herd { get; set; }
    /// <summary>Its place relative to the herd's leader.</summary>
    public Vector3 HerdSlot { get; set; }
    /// <summary>Calm: grazing, walking, standing, lying or drinking (far calm animals are updated less often).</summary>
    public bool IsCalm => State is Mode.Idle or Mode.Walk or Mode.Graze or Mode.Rest or Mode.Drink;
    /// <summary>The way it faces, flat.</summary>
    public Vector3 Facing => new Vector3(-GlobalBasis.Z.X, 0.0f, -GlobalBasis.Z.Z).Normalized();
    /// <summary>How steep the ground under it is, in degrees (as of its last step).</summary>
    public float GroundSlope => Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(_fallLine.Length(), 0.0f, 1.0f)));
    /// <summary>The way it last fled or walked off (flat; zero before).</summary>
    public Vector3 FleeDirection => _fleeDirection;

    private float _calm;
    private float _shoveCooldown;

    public WildlifeSpecies? Species { get; private set; }
    public Mode State { get; private set; } = Mode.Graze;
    public bool HasGround { get; private set; }
    /// <summary>Surface and slope where the spawner placed it (empty when placed by hand).</summary>
    public string SpawnSurface { get; set; } = "";
    public float SpawnSlope { get; set; }

    private WildlifeTable? _table;
    private RandomNumberGenerator _random = new();
    private AnimationPlayer? _animation;
    private string _clip = "";
    private float _timer;
    private float _stateTime;
    private Vector3 _target;
    private Vector3 _fleeDirection;
    private Vector3 _threat;
    private bool _threatIsCarrier;
    private float _alertCooldown;
    private float _curiousCooldown;
    private float _uneasy;
    private float _probeTimer;
    private float _escapeTimer;
    private float _glanceTimer;
    private bool _sniffing;
    private bool _toWater;
    private int _followSerial;
    private float _lookYaw;
    private Vector3 _drift;
    private Vector3 _fallLine;
    private float _sink;
    private PhysicsRayQueryParameters3D? _ray;
    private PhysicsRayQueryParameters3D? _probe;
    private Node3D? _model;
    private Transform3D _modelBase;
    private Quaternion _tilt = Quaternion.Identity;
    private float _gravity = 9.8f;
    private Vector3 _knock;
    private Skeleton3D? _skeleton;

    /// <summary>What the animal makes of the carrier this tick.</summary>
    private readonly record struct Perception(bool Aware, bool Moving, bool Fast, float Flight);

    // --- IMeleeTarget: the carrier's sword ---

    public Vector3 HitBase => GlobalPosition;
    public float HitHeight => Species?.BodySize.Y ?? 1.0f;
    public float HitRadius => Radius;
    public bool CanBeHit => Species != null;
    public Skeleton3D? HitSkeleton => _skeleton;
    public bool Lockable => false;

    /// <summary>Animals have no health: a wound only leaves a trail of blood (environment/Bleeding).</summary>
    public bool Bleed(float damage)
    {
        return true;
    }

    /// <summary>Struck: knocked the way of the blow, and it bolts away from the carrier (its herd with it).</summary>
    public void TakeHit(float damage, Vector3 push, string source)
    {
        if (Puppet) { GameEvents.PublishNetMobHit(this, damage, push, source); return; }
        Struck++;
        _knock = new Vector3(push.X, 0.0f, push.Z);
        FleeFrom(GlobalPosition - _knock);
    }

    public void Setup(WildlifeSpecies species, WildlifeTable table, ulong seed)
    {
        Species = species;
        _table = table;
        _random.Seed = seed;
        Name = species.DisplayName;
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        CollisionLayer = table.BodyLayer;
        CollisionMask = table.BodyMask;
        FloorMaxAngle = Mathf.DegToRad(table.MaxFloorAngle);
        FloorSnapLength = 0.6f;
        if (species.HoverHeight > 0.0f)
        {
            MotionMode = MotionModeEnum.Floating;
        }
        // An upright capsule as wide as the body: on a slope its round foot stays close to the ground.
        Vector3 body = species.BodySize;
        float radius = Mathf.Max(0.05f, Mathf.Min(body.X, body.Z) * 0.5f);
        float height = Mathf.Max(body.Y, radius * 2.0f);
        Radius = radius;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
            Position = Vector3.Up * height * 0.5f,
        });
        if (species.Model != null)
        {
            _model = species.Model.Instantiate<Node3D>();
            _model.Scale = Vector3.One * species.ModelScale;
            _model.RotationDegrees = species.ModelRotationDegrees;
            AddChild(_model);
            _modelBase = _model.Transform;
            _animation = FindPlayer(_model);
            foreach (Node node in _model.FindChildren("*", "Skeleton3D", true, false))
            {
                _skeleton = node as Skeleton3D;
                break;
            }
        }
        AddToGroup(IMeleeTarget.Group);
        Rotation = new Vector3(0.0f, _random.RandfRange(0.0f, Mathf.Tau), 0.0f);
        // Placed mid-meal: animals in view don't all start or stop together.
        State = Mode.Graze;
        _timer = _random.RandfRange(0.0f, table.WanderInterval.Y);
        _alertCooldown = _random.RandfRange(0.0f, 2.0f);
        Play(ClipOr(species.GrazeClip), 1.0f, _random.Randf());
    }

    /// <summary>Advances the animal by dt given where the carrier is and whether it sprints.</summary>
    public void Tick(float dt, Vector3 carrier, bool sprinting, PhysicsDirectSpaceState3D space)
    {
        Tick(dt, carrier, Vector3.Zero, sprinting, space);
    }

    /// <summary>Advances the animal by dt given where the carrier is, where it's steering (input) and whether it sprints.</summary>
    public void Tick(float dt, Vector3 carrier, Vector3 carrierInput, bool sprinting, PhysicsDirectSpaceState3D space)
    {
        Tick(dt, new CarrierSense(carrier, Vector3.Zero, carrierInput, sprinting, false, 0.0f), space);
    }

    /// <summary>Advances the animal by dt given what it can make of the nearest carrier.</summary>
    public void Tick(float dt, in CarrierSense carrier, PhysicsDirectSpaceState3D space)
    {
        if (Puppet) { return; }
        if (Species == null || _table == null)
        {
            return;
        }
        Vector3 toCarrier = carrier.Position - GlobalPosition;
        toCarrier.Y = 0.0f;
        float distance = toCarrier.Length();
        _shoveCooldown = Mathf.Max(0.0f, _shoveCooldown - dt);
        _alertCooldown -= dt;
        _curiousCooldown -= dt;
        _uneasy -= dt;
        _probeTimer -= dt;
        _escapeTimer -= dt;
        _stateTime += dt;
        if (IsOnFloor())
        {
            // The horizontal part of the floor's normal points downhill (zero on the flat).
            Vector3 normal = GetFloorNormal();
            _fallLine = new Vector3(normal.X, 0.0f, normal.Z);
        }

        if (State == Mode.Shove)
        {
            ShoveStep(dt, toCarrier, distance, space);
            return;
        }
        Vector3 pushedBy = Pushed(toCarrier, distance, carrier.Input, dt);

        Vector3 walk = Vector3.Zero;
        if (State != Mode.Shove)
        {
            Perception sense = Perceive(carrier, distance);
            walk = State switch
            {
                Mode.Alert => AlertStep(dt, carrier, toCarrier, distance, sense, space),
                Mode.Flee => FleeStep(dt, carrier, distance, sense, space),
                Mode.Retreat => RetreatStep(dt, carrier, toCarrier, distance, sense, space),
                Mode.Approach => ApproachStep(dt, carrier, toCarrier, distance, sense, space),
                Mode.LookBack => LookBackStep(dt, carrier, toCarrier, distance, sense, space),
                _ => CalmStep(dt, carrier, distance, sense, space),
            };
        }
        if (!pushedBy.IsZeroApprox())
        {
            // A heavy animal gives way slowly under the push, whatever it was doing.
            walk = pushedBy * _table.PushYieldSpeed;
        }

        Step(walk, dt, space);
        LeanToGround(dt);
    }

    // --- Senses ---

    /// <summary>Whether it sees or hears the carrier, how alarming the carrier is, and how close it may come.</summary>
    private Perception Perceive(in CarrierSense carrier, float distance)
    {
        WildlifeTable table = _table!;
        WildlifeSpecies species = Species!;
        float speed = new Vector2(carrier.Velocity.X, carrier.Velocity.Z).Length();
        bool fast = carrier.Sprinting || carrier.Falling || speed > table.FastSpeed;
        bool moving = fast || speed > table.MovingSpeed;
        float wake = _uneasy > 0.0f ? table.UneasyFactor : 1.0f;
        float noise = fast ? 1.0f : moving ? table.WalkNoise : table.StillNoise;
        noise *= 1.0f + table.LoadNoise * Mathf.Clamp(carrier.LoadMass / Mathf.Max(table.HeavyLoad, 1.0f), 0.0f, 1.0f);
        float sight = species.SightRange * (moving ? 1.0f : table.StillSight) * wake;
        float hearing = species.HearingRange * noise * wake;
        bool aware = distance < sight || distance < hearing;
        float flight = fast ? species.SprintFleeDistance : moving ? species.FleeDistance : species.FleeDistance * table.StillSight;
        return new Perception(aware, moving, fast, flight * wake);
    }

    /// <summary>What its temperament makes it do about the carrier now (Idle: nothing but watch).</summary>
    private Mode Reaction(Perception sense, float distance)
    {
        if (!sense.Aware)
        {
            return Mode.Idle;
        }
        WildlifeSpecies species = Species!;
        switch (species.Temperament)
        {
            case Temperament.Skittish:
                return distance < sense.Flight ? Mode.Flee : Mode.Idle;
            case Temperament.Wary:
                if (distance < sense.Flight)
                {
                    return sense.Fast || !CanWalk ? Mode.Flee : Mode.Retreat;
                }
                return Mode.Idle;
            case Temperament.Curious:
                if (sense.Fast && distance < sense.Flight * 0.6f)
                {
                    return Mode.Flee;
                }
                if (sense.Moving && !sense.Fast && distance < species.FleeDistance && CanWalk)
                {
                    return Mode.Retreat;
                }
                if (!sense.Moving && _curiousCooldown <= 0.0f && CanWalk
                    && distance < species.CuriousRange && distance > species.ApproachStop + 1.5f)
                {
                    return Mode.Approach;
                }
                return Mode.Idle;
            default:
                // Bold: holds its ground (it shoves when pushed).
                return Mode.Idle;
        }
    }

    // --- States ---

    /// <summary>Grazing, walking, standing, lying or drinking; noticing the carrier lifts its head.</summary>
    private Vector3 CalmStep(float dt, in CarrierSense carrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        if (sense.Aware)
        {
            Mode reaction = Reaction(sense, distance);
            if (reaction is Mode.Flee or Mode.Retreat)
            {
                // Close already: a short beat, then it acts.
                EnterAlert(carrier.Position, _table!.MinAlertBeat + 0.2f, true);
                return Vector3.Zero;
            }
            if (reaction == Mode.Approach || _alertCooldown <= 0.0f)
            {
                EnterAlert(carrier.Position, Species!.AlertSeconds, true);
                return Vector3.Zero;
            }
        }

        WildlifeHerd? herd = Herd;
        if (State == Mode.Walk)
        {
            Vector3 toTarget = _target - GlobalPosition;
            toTarget.Y = 0.0f;
            _timer -= dt;
            if (toTarget.Length() < 0.4f || _timer <= 0.0f)
            {
                if (_toWater)
                {
                    StartDrink();
                }
                else
                {
                    NextActivity(space);
                }
                return Vector3.Zero;
            }
            Vector3 direction = toTarget.Normalized();
            if (_probeTimer <= 0.0f)
            {
                _probeTimer = _table!.ProbeInterval * 1.5f;
                if (!Safe(direction, Lookahead(Species!.WalkSpeed), space))
                {
                    // A bank, a drop or a rock ahead: turn the drift round and eat here.
                    FlipDrift();
                    StartGraze();
                    return Vector3.Zero;
                }
            }
            return Move(Steer(direction, 0.6f), Species!.WalkSpeed, dt);
        }

        if (State == Mode.Idle)
        {
            Vector3 look = new(-Mathf.Sin(_lookYaw), 0.0f, -Mathf.Cos(_lookYaw));
            Turn(look, dt * 0.4f);
        }
        _timer -= dt;
        // A follower sets off a moment after its leader does.
        if (herd != null && State != Mode.Rest && herd.Leader != this && herd.MoveSerial != _followSerial)
        {
            _followSerial = herd.MoveSerial;
            _timer = Mathf.Min(_timer, _random.RandfRange(0.3f, 2.0f));
        }
        if (_timer <= 0.0f)
        {
            NextActivity(space);
        }
        return Vector3.Zero;
    }

    /// <summary>Head up, frozen, facing what it noticed; then it acts or goes back to its meal.</summary>
    private Vector3 AlertStep(float dt, in CarrierSense carrier, Vector3 toCarrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        Vector3 look = sense.Aware ? toCarrier : Flat(_threat - GlobalPosition);
        if (look.LengthSquared() > 0.0001f)
        {
            Turn(look.Normalized(), dt * 2.0f);
        }
        Mode reaction = Reaction(sense, distance);
        if (reaction is Mode.Flee or Mode.Retreat && _stateTime >= _table!.MinAlertBeat)
        {
            Act(reaction, carrier.Position);
            return Vector3.Zero;
        }
        _timer -= dt;
        if (_timer > 0.0f)
        {
            return Vector3.Zero;
        }
        if (reaction == Mode.Approach)
        {
            StartApproach();
            return Vector3.Zero;
        }
        _alertCooldown = _random.RandfRange(_table!.AlertCooldown.X, _table.AlertCooldown.Y);
        StartGraze();
        return Vector3.Zero;
    }

    /// <summary>Runs away and downhill or along the slope, around what it can't cross, until safe.</summary>
    private Vector3 FleeStep(float dt, in CarrierSense carrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        WildlifeTable table = _table!;
        WildlifeSpecies species = Species!;
        if (_threatIsCarrier && sense.Aware && distance < sense.Flight * 1.5f)
        {
            // Chased: keeps running from where the carrier is now.
            _threat = carrier.Position;
        }
        float fromThreat = Flat(GlobalPosition - _threat).Length();
        bool settled = fromThreat >= species.SafeDistance && (distance >= species.SafeDistance * 0.8f || !sense.Moving);
        if ((_stateTime >= table.FleeSeconds && settled) || _stateTime >= table.MaxFleeSeconds)
        {
            StartLookBack();
            return Vector3.Zero;
        }
        if (_escapeTimer <= 0.0f)
        {
            _escapeTimer = 0.5f;
            Vector3 escape = ChooseEscape(_threat, Lookahead(species.FleeSpeed), space);
            if (escape.IsZeroApprox())
            {
                return Corner(distance, carrier.Position);
            }
            _fleeDirection = escape;
            if (Herd != null)
            {
                Herd.FleeDirection = Herd.FleeDirection.IsZeroApprox() ? escape : (Herd.FleeDirection + escape).Normalized();
            }
        }
        else if (_probeTimer <= 0.0f)
        {
            _probeTimer = table.ProbeInterval;
            if (!Safe(_fleeDirection, Lookahead(species.FleeSpeed), space))
            {
                // Something ahead: pick a new way next tick, slowing meanwhile.
                _escapeTimer = 0.0f;
                return Vector3.Zero;
            }
        }
        // Easing off as the distance grows.
        float speed = species.FleeSpeed * (fromThreat > species.SafeDistance * 0.75f ? 0.65f : 1.0f);
        return Move(Steer(_fleeDirection, 0.6f), speed, dt);
    }

    /// <summary>Wary: walks away briskly, stopping now and then to look back, until far enough.</summary>
    private Vector3 RetreatStep(float dt, in CarrierSense carrier, Vector3 toCarrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        WildlifeSpecies species = Species!;
        if (sense.Aware && Reaction(sense, distance) == Mode.Flee)
        {
            Act(Mode.Flee, carrier.Position);
            return Vector3.Zero;
        }
        bool farEnough = !sense.Aware || distance >= species.FleeDistance * 1.6f * (_uneasy > 0.0f ? _table!.UneasyFactor : 1.0f);
        if ((farEnough && _stateTime > 2.0f) || _stateTime > 20.0f)
        {
            StartLookBack();
            return Vector3.Zero;
        }
        _glanceTimer -= dt;
        if (_glanceTimer <= 0.0f)
        {
            // A look back over the shoulder.
            if (distance > 0.01f)
            {
                Turn(toCarrier / distance, dt * 1.5f);
            }
            Play(species.IdleClip, _table!.AlertClipSpeed, -1.0f);
            if (_glanceTimer <= -0.8f)
            {
                _glanceTimer = _random.RandfRange(2.5f, 4.5f);
                PlayMove(1.3f);
            }
            return Vector3.Zero;
        }
        float speed = species.WalkSpeed * 1.4f;
        if (_escapeTimer <= 0.0f)
        {
            _escapeTimer = 0.8f;
            Vector3 away = ChooseEscape(carrier.Position, Lookahead(speed), space);
            if (away.IsZeroApprox())
            {
                return Corner(distance, carrier.Position);
            }
            _fleeDirection = away;
        }
        else if (_probeTimer <= 0.0f)
        {
            _probeTimer = _table!.ProbeInterval;
            if (!Safe(_fleeDirection, Lookahead(speed), space))
            {
                _escapeTimer = 0.0f;
                return Vector3.Zero;
            }
        }
        return Move(Steer(_fleeDirection, 0.6f), speed, dt);
    }

    /// <summary>Curious: walks up to a carrier standing still, sniffs, loses interest and wanders off.</summary>
    private Vector3 ApproachStep(float dt, in CarrierSense carrier, Vector3 toCarrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        WildlifeSpecies species = Species!;
        if (sense.Moving)
        {
            Mode reaction = Reaction(sense, distance);
            if (reaction is Mode.Flee or Mode.Retreat)
            {
                Act(reaction, carrier.Position);
                return Vector3.Zero;
            }
            LoseInterest(carrier.Position, space);
            return Vector3.Zero;
        }
        if (distance > 0.01f)
        {
            Turn(toCarrier / distance, dt);
        }
        if (_sniffing || distance <= species.ApproachStop)
        {
            if (!_sniffing)
            {
                _sniffing = true;
                _timer = _random.RandfRange(_table!.SniffSeconds.X, _table.SniffSeconds.Y);
                Play(species.IdleClip, 1.0f, -1.0f);
            }
            _timer -= dt;
            if (_timer <= 0.0f)
            {
                LoseInterest(carrier.Position, space);
            }
            return Vector3.Zero;
        }
        if (_stateTime > 25.0f || !sense.Aware)
        {
            LoseInterest(carrier.Position, space);
            return Vector3.Zero;
        }
        Vector3 direction = toCarrier / Mathf.Max(distance, 0.01f);
        if (_probeTimer <= 0.0f)
        {
            _probeTimer = _table!.ProbeInterval * 1.5f;
            if (!Safe(direction, Mathf.Min(Lookahead(species.WalkSpeed), Mathf.Max(distance - species.ApproachStop, 0.5f)), space))
            {
                LoseInterest(carrier.Position, space);
                return Vector3.Zero;
            }
        }
        return Move(direction, species.WalkSpeed * 0.8f, dt);
    }

    /// <summary>Stopped after fleeing: looks back at what scared it, then grazes again (uneasy for a while).</summary>
    private Vector3 LookBackStep(float dt, in CarrierSense carrier, Vector3 toCarrier, float distance, Perception sense, PhysicsDirectSpaceState3D space)
    {
        Vector3 look = sense.Aware ? toCarrier : Flat(_threat - GlobalPosition);
        if (look.LengthSquared() > 0.0001f)
        {
            Turn(look.Normalized(), dt * 1.5f);
        }
        Mode reaction = Reaction(sense, distance);
        if (reaction is Mode.Flee or Mode.Retreat && _stateTime > 0.5f)
        {
            Act(reaction, carrier.Position);
            return Vector3.Zero;
        }
        _timer -= dt;
        if (_timer <= 0.0f)
        {
            _alertCooldown = _random.RandfRange(_table!.AlertCooldown.X, _table.AlertCooldown.Y) * 0.5f;
            if (Herd != null)
            {
                Herd.FleeDirection = Vector3.Zero;
            }
            StartGraze();
        }
        return Vector3.Zero;
    }

    /// <summary>Nowhere safe to go: a big animal close to the carrier turns on it, others stand frozen.</summary>
    private Vector3 Corner(float distance, Vector3 carrier)
    {
        Cornered++;
        float reach = Radius + _table!.CarrierRadius + 1.5f;
        if (_threatIsCarrier && distance < reach && Species!.BodySize.Y >= _table.MinShoverHeight && _shoveCooldown <= 0.0f)
        {
            BeginShove();
            return Vector3.Zero;
        }
        EnterAlert(carrier, 1.2f, false);
        return Vector3.Zero;
    }

    // --- Starting states ---

    private void Act(Mode reaction, Vector3 carrier)
    {
        if (reaction == Mode.Flee)
        {
            StartFlee(carrier, true, true);
        }
        else if (reaction == Mode.Retreat)
        {
            StartRetreat(carrier, true);
        }
    }

    /// <summary>Head up, frozen, facing a point; the herd around lifts its heads too.</summary>
    private void EnterAlert(Vector3 at, float seconds, bool spread)
    {
        State = Mode.Alert;
        _stateTime = 0.0f;
        _timer = seconds * _random.RandfRange(0.8f, 1.2f);
        _threat = at;
        _toWater = false;
        Alerts++;
        Play(Species!.IdleClip, _table!.AlertClipSpeed, -1.0f);
        if (!spread || Herd == null)
        {
            return;
        }
        foreach (WildlifeAnimal member in Herd.Members)
        {
            if (member != this && WildlifeHerd.Alive(member) && member.IsCalm && Near(member))
            {
                member.EnterAlert(at, member.Species!.AlertSeconds, false);
            }
        }
    }

    private void StartFlee(Vector3 point, bool fromCarrier, bool spread)
    {
        if (Species == null || _table == null)
        {
            return;
        }
        _threat = point;
        _threatIsCarrier = fromCarrier;
        Vector3 away = Flat(GlobalPosition - point);
        if (away.LengthSquared() < 0.01f)
        {
            away = new Vector3(_random.RandfRange(-1.0f, 1.0f), 0.0f, _random.RandfRange(-1.0f, 1.0f));
        }
        // Not quite straight away: animals veer a little, so a herd spreads out. With the herd
        // already running, it runs their way.
        _fleeDirection = away.Normalized().Rotated(Vector3.Up, _random.RandfRange(-0.4f, 0.4f));
        if (Herd != null && !Herd.FleeDirection.IsZeroApprox() && Herd.FleeDirection.Dot(away.Normalized()) > -0.2f)
        {
            _fleeDirection = Herd.FleeDirection.Rotated(Vector3.Up, _random.RandfRange(-0.2f, 0.2f));
        }
        _escapeTimer = 0.0f;
        _probeTimer = 0.0f;
        _stateTime = 0.0f;
        _toWater = false;
        _uneasy = _table.UneasySeconds;
        State = Mode.Flee;
        Flees++;
        Play(Species.FleeClip, Species.FleeClipSpeed, 0.0f);
        if (!spread || Herd == null)
        {
            return;
        }
        Herd.FleeDirection = _fleeDirection;
        foreach (WildlifeAnimal member in Herd.Members)
        {
            if (member != this && WildlifeHerd.Alive(member) && member.State is not (Mode.Flee or Mode.Shove) && Near(member))
            {
                member.StartFlee(point, fromCarrier, false);
            }
        }
    }

    private void StartRetreat(Vector3 carrier, bool spread)
    {
        _threat = carrier;
        _threatIsCarrier = true;
        State = Mode.Retreat;
        _stateTime = 0.0f;
        _escapeTimer = 0.0f;
        _probeTimer = 0.0f;
        _toWater = false;
        _glanceTimer = _random.RandfRange(2.0f, 3.5f);
        PlayMove(1.3f);
        if (!spread || Herd == null)
        {
            return;
        }
        foreach (WildlifeAnimal member in Herd.Members)
        {
            if (member != this && WildlifeHerd.Alive(member) && (member.IsCalm || member.State == Mode.Alert) && Near(member))
            {
                member.StartRetreat(carrier, false);
            }
        }
    }

    private void StartApproach()
    {
        State = Mode.Approach;
        _stateTime = 0.0f;
        _sniffing = false;
        _probeTimer = 0.0f;
        Approaches++;
        PlayMove(0.8f);
    }

    /// <summary>Done looking: leaves the carrier alone for a while and wanders a few metres off.</summary>
    private void LoseInterest(Vector3 carrier, PhysicsDirectSpaceState3D space)
    {
        _sniffing = false;
        _curiousCooldown = _random.RandfRange(_table!.CuriousCooldown.X, _table.CuriousCooldown.Y);
        _alertCooldown = _random.RandfRange(_table.AlertCooldown.X, _table.AlertCooldown.Y);
        Vector3 away = Flat(GlobalPosition - carrier);
        if (away.LengthSquared() < 0.01f)
        {
            away = Facing;
        }
        Vector3 target = GlobalPosition + away.Normalized().Rotated(Vector3.Up, _random.RandfRange(-1.0f, 1.0f)) * _random.RandfRange(4.0f, 7.0f);
        if (!TryWalk(target, space))
        {
            StartGraze();
        }
    }

    private void StartLookBack()
    {
        State = Mode.LookBack;
        _stateTime = 0.0f;
        _timer = _random.RandfRange(2.0f, 4.0f);
        LookBacks++;
        Play(Species!.IdleClip, _table!.AlertClipSpeed, -1.0f);
    }

    private void StartGraze()
    {
        State = Mode.Graze;
        _stateTime = 0.0f;
        _toWater = false;
        _timer = _random.RandfRange(_table!.WanderInterval.X * 2.0f, _table.WanderInterval.Y * 2.0f);
        Play(ClipOr(Species!.GrazeClip), 1.0f, -1.0f);
    }

    private void StartIdle()
    {
        State = Mode.Idle;
        _stateTime = 0.0f;
        _timer = _random.RandfRange(_table!.WanderInterval.X, _table.WanderInterval.Y);
        _lookYaw = Rotation.Y + _random.RandfRange(-1.2f, 1.2f);
        Play(Species!.IdleClip, 1.0f, -1.0f);
    }

    private void StartRest()
    {
        State = Mode.Rest;
        _stateTime = 0.0f;
        _timer = _random.RandfRange(_table!.RestSeconds.X, _table.RestSeconds.Y);
        bool ownClip = Species!.RestClip.Length > 0 && _animation != null && _animation.HasAnimation(Species.RestClip);
        Play(ownClip ? Species.RestClip : Species.IdleClip, ownClip ? 1.0f : 0.35f, -1.0f);
    }

    private void StartDrink()
    {
        State = Mode.Drink;
        _stateTime = 0.0f;
        _toWater = false;
        _timer = _random.RandfRange(8.0f, 14.0f);
        Play(ClipOr(Species!.GrazeClip), 1.0f, -1.0f);
    }

    /// <summary>A spell of grazing ended: walk on (the leader, along the slope; followers, to their place), look round, lie down or graze on.</summary>
    private void NextActivity(PhysicsDirectSpaceState3D space)
    {
        WildlifeTable table = _table!;
        WildlifeHerd? herd = Herd;
        WildlifeAnimal? leader = herd?.Leader;
        bool follower = leader != null && leader != this;
        if (follower)
        {
            Vector3 anchor = leader!.State == Mode.Walk ? herd!.LeaderTarget : leader.GlobalPosition;
            Vector3 slot = anchor + HerdSlot + new Vector3(_random.RandfRange(-0.6f, 0.6f), 0.0f, _random.RandfRange(-0.6f, 0.6f));
            if (Flat(slot - GlobalPosition).Length() > Species!.HerdSpacing * 1.5f && TryWalk(slot, space))
            {
                return;
            }
        }
        else if (CanWalk && WaterNear != null && _random.Randf() < table.DrinkChance)
        {
            Vector3? water = WaterNear(GlobalPosition, table.DrinkRange);
            if (water.HasValue && TryWalk(water.Value, space))
            {
                _toWater = true;
                herd?.LeaderMoves(water.Value);
                return;
            }
        }
        float roll = _random.Randf();
        if (!follower && CanWalk && roll < table.WalkChance)
        {
            Vector3 target = GlobalPosition + DriftDirection() * _random.RandfRange(table.WanderDistance * 0.5f, table.WanderDistance);
            if (TryWalk(target, space))
            {
                herd?.LeaderMoves(target);
                return;
            }
            FlipDrift();
        }
        else if (roll < table.WalkChance + table.IdleChance)
        {
            StartIdle();
            return;
        }
        else if (roll < table.WalkChance + table.IdleChance + table.RestChance && CanRest)
        {
            StartRest();
            return;
        }
        StartGraze();
    }

    /// <summary>Sets off for a point if the ground there is ground it stands on.</summary>
    private bool TryWalk(Vector3 target, PhysicsDirectSpaceState3D space)
    {
        if (!CanWalk)
        {
            return false;
        }
        Vector3 to = Flat(target - GlobalPosition);
        float length = to.Length();
        if (length < 0.5f)
        {
            return false;
        }
        if (Species!.HoverHeight <= 0.0f)
        {
            float limit = Mathf.Tan(Mathf.DegToRad(Mathf.Min(Species.MaxSlope + _table!.SlopeTolerance, 75.0f))) * length;
            if (!ProbeGround(target, limit, space, out Vector3 point, out Vector3 normal)
                || Mathf.RadToDeg(normal.AngleTo(Vector3.Up)) > Species.MaxSlope
                || Mathf.Abs(point.Y - GlobalPosition.Y) > limit)
            {
                return false;
            }
            target = point;
        }
        _target = target;
        _timer = length / Mathf.Max(Species.WalkSpeed, 0.1f) * 2.0f + 1.0f;
        _probeTimer = 0.0f;
        State = Mode.Walk;
        _stateTime = 0.0f;
        PlayMove(1.0f);
        return true;
    }

    // --- Ground ahead ---

    /// <summary>
    /// Away from a threat, downhill or along the slope (never up a steep one), the way the herd runs,
    /// turning as little as it must to find ground it can stand on; zero when there is none.
    /// </summary>
    private Vector3 ChooseEscape(Vector3 threat, float lookahead, PhysicsDirectSpaceState3D space)
    {
        Vector3 away = Flat(GlobalPosition - threat);
        away = away.LengthSquared() < 0.0001f ? Facing : away.Normalized();
        float steep = _fallLine.Length();
        Vector3 preferred = away;
        if (steep > 0.02f)
        {
            Vector3 down = _fallLine / steep;
            Vector3 along = Vector3.Up.Cross(down).Normalized();
            if (along.Dot(away) < 0.0f)
            {
                along = -along;
            }
            preferred += down * 0.5f + along * 0.3f;
        }
        if (Herd != null && !Herd.FleeDirection.IsZeroApprox())
        {
            preferred += Herd.FleeDirection * 0.8f;
        }
        preferred = Flat(preferred);
        preferred = preferred.LengthSquared() < 0.0001f || preferred.Normalized().Dot(away) < 0.2f ? away : preferred.Normalized();
        float side = _random.Randf() < 0.5f ? 1.0f : -1.0f;
        for (int step = 0; step <= 4; step++)
        {
            for (int turn = 0; turn < (step == 0 ? 1 : 2); turn++)
            {
                float angle = Mathf.DegToRad(30.0f * step) * (turn == 0 ? side : -side);
                Vector3 direction = preferred.Rotated(Vector3.Up, angle);
                if (direction.Dot(away) < -0.35f)
                {
                    continue;
                }
                if (Safe(direction, lookahead, space))
                {
                    return direction;
                }
            }
        }
        return Vector3.Zero;
    }

    /// <summary>
    /// Whether it can go this way: nothing standing in the way at body height (a rock, a tree, a
    /// bank), and ground close ahead and farther on that isn't too steep or a drop.
    /// </summary>
    private bool Safe(Vector3 direction, float distance, PhysicsDirectSpaceState3D space)
    {
        WildlifeSpecies species = Species!;
        Vector3 origin = GlobalPosition;
        _probe ??= new PhysicsRayQueryParameters3D { CollisionMask = _table!.GroundMask };
        float height = Mathf.Max(0.25f, species.BodySize.Y * 0.5f);
        _probe.From = origin + Vector3.Up * height;
        _probe.To = _probe.From + direction * Mathf.Min(distance, 2.5f);
        Godot.Collections.Dictionary hit = space.IntersectRay(_probe);
        if (hit.Count > 0 && ((Vector3)hit["normal"]).Y < 0.7f)
        {
            return false;
        }
        if (species.HoverHeight > 0.0f)
        {
            return true;
        }
        float tangent = Mathf.Tan(Mathf.DegToRad(Mathf.Min(species.MaxSlope + _table!.SlopeTolerance, 75.0f)));
        float near = Mathf.Min(1.2f, distance);
        foreach (float along in stackalloc float[] { near, distance })
        {
            float limit = tangent * along;
            if (!ProbeGround(origin + direction * along, limit, space, out Vector3 point, out Vector3 normal))
            {
                return false;
            }
            if (Mathf.RadToDeg(normal.AngleTo(Vector3.Up)) > species.MaxSlope + _table.SlopeTolerance || point.Y - origin.Y > limit)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The ground under a point no farther above or below the animal than a walkable rise plus CliffDrop.</summary>
    private bool ProbeGround(Vector3 at, float rise, PhysicsDirectSpaceState3D space, out Vector3 point, out Vector3 normal)
    {
        _probe ??= new PhysicsRayQueryParameters3D { CollisionMask = _table!.GroundMask };
        float y = GlobalPosition.Y;
        _probe.From = new Vector3(at.X, y + rise + 1.0f, at.Z);
        _probe.To = new Vector3(at.X, y - rise - _table!.CliffDrop, at.Z);
        Godot.Collections.Dictionary hit = space.IntersectRay(_probe);
        if (hit.Count == 0)
        {
            point = at;
            normal = Vector3.Up;
            return false;
        }
        point = (Vector3)hit["position"];
        normal = (Vector3)hit["normal"];
        return true;
    }

    /// <summary>How far ahead a check looks at a speed, in m.</summary>
    private static float Lookahead(float speed)
    {
        return Mathf.Clamp(1.2f + speed * 0.5f, 1.5f, 4.5f);
    }

    /// <summary>The herd's (or its own) drift: along the slope, wandering a little, with a touch of downhill.</summary>
    private Vector3 DriftDirection()
    {
        Vector3 drift = Herd?.Drift ?? _drift;
        float steep = _fallLine.Length();
        if (drift.IsZeroApprox())
        {
            drift = steep > 0.02f
                ? Vector3.Up.Cross(_fallLine / steep).Normalized() * (_random.Randf() < 0.5f ? 1.0f : -1.0f)
                : Vector3.Forward.Rotated(Vector3.Up, _random.RandfRange(0.0f, Mathf.Tau));
        }
        drift = drift.Rotated(Vector3.Up, _random.RandfRange(-0.45f, 0.45f));
        if (steep > 0.02f)
        {
            Vector3 down = _fallLine / steep;
            // Across the slope, not up it.
            if (drift.Dot(down) < -0.3f)
            {
                drift -= down * (drift.Dot(down) + 0.3f);
            }
            drift += down * 0.12f;
        }
        drift = Flat(drift).Normalized();
        if (Herd != null)
        {
            Herd.Drift = drift;
        }
        else
        {
            _drift = drift;
        }
        return drift;
    }

    private void FlipDrift()
    {
        if (Herd != null)
        {
            Herd.Drift = -Herd.Drift;
        }
        else
        {
            _drift = -_drift;
        }
    }

    /// <summary>A direction nudged away from herd-mates that stand too close.</summary>
    private Vector3 Steer(Vector3 direction, float weight)
    {
        if (Herd == null)
        {
            return direction;
        }
        Vector3 push = Vector3.Zero;
        float spacing = Species!.HerdSpacing;
        foreach (WildlifeAnimal member in Herd.Members)
        {
            if (member == this || !WildlifeHerd.Alive(member))
            {
                continue;
            }
            Vector3 offset = Flat(GlobalPosition - member.GlobalPosition);
            float gap = offset.Length();
            if (gap < spacing && gap > 0.01f)
            {
                push += offset / gap * (spacing - gap) / spacing;
            }
        }
        Vector3 steered = Flat(direction + push * weight);
        return steered.LengthSquared() < 0.0001f ? direction : steered.Normalized();
    }

    private bool Near(WildlifeAnimal member)
    {
        return Flat(member.GlobalPosition - GlobalPosition).Length() <= _table!.HerdAlarmRadius;
    }

    private bool CanWalk => Species!.WalkSpeed > 0.05f && (Species.WalkClip.Length > 0 || Species.FleeClip.Length > 0);

    private bool CanRest => Species!.RestSink > 0.0f || Species.RestClip.Length > 0;

    private static Vector3 Flat(Vector3 v)
    {
        return new Vector3(v.X, 0.0f, v.Z);
    }

    // --- Pushing and shoving ---

    /// <summary>
    /// Whether the carrier is pushing: touching (within the margin) and steering into the animal.
    /// Counts the pushing time and starts the shove when patience runs out (big enough animals).
    /// Returns the direction it's being pushed in, or zero.
    /// </summary>
    private Vector3 Pushed(Vector3 toCarrier, float distance, Vector3 carrierInput, float dt)
    {
        float touching = Radius + _table!.CarrierRadius + _table.PushContactMargin;
        Vector3 input = new(carrierInput.X, 0.0f, carrierInput.Z);
        bool pushing = false;
        if (distance < touching && distance > 0.001f && input.LengthSquared() > 0.01f)
        {
            Vector3 away = -toCarrier / distance;
            pushing = input.Normalized().Dot(away) >= _table.PushMinAlignment;
        }
        if (!pushing)
        {
            _calm += dt;
            if (_calm >= _table.PushForgetTime)
            {
                PushTime = 0.0f;
            }
            return Vector3.Zero;
        }
        _calm = 0.0f;
        PushTime += dt;
        bool bigEnough = Species!.BodySize.Y >= _table.MinShoverHeight;
        if (bigEnough && PushTime >= _table.PushPatience && _shoveCooldown <= 0.0f)
        {
            BeginShove();
        }
        return -toCarrier / distance;
    }

    private void BeginShove()
    {
        State = Mode.Shove;
        _stateTime = 0.0f;
        _timer = _table!.ShoveWindUp;
        // Stamping: the run clip, fast, on the spot.
        Play(Species!.FleeClip, Species.FleeClipSpeed * 2.0f, 0.0f);
    }

    /// <summary>Turns round to face the carrier, braces for the wind-up, then throws them if still in reach.</summary>
    private void ShoveStep(float dt, Vector3 toCarrier, float distance, PhysicsDirectSpaceState3D space)
    {
        if (distance > 0.01f)
        {
            Turn(toCarrier / distance, dt * 4.0f);
        }
        _timer -= dt;
        Step(Vector3.Zero, dt, space);
        LeanToGround(dt);
        if (_timer > 0.0f)
        {
            return;
        }
        float reach = Radius + _table!.CarrierRadius + _table.PushContactMargin + 0.6f;
        if (distance < reach && distance > 0.001f)
        {
            if (TargetPeer == 0) { GameEvents.RequestLaunch(toCarrier / distance * _table.ShoveSpeed, _table.ShoveHeight, Species!.DisplayName); }
            else { GameEvents.PublishNetStrike(new NetStrike(TargetPeer, 0, toCarrier / distance, _table.ShoveSpeed, false, 0, _table.ShoveHeight, Species!.DisplayName)); }
            Shoves++;
        }
        PushTime = 0.0f;
        _shoveCooldown = _table.ShoveCooldown;
        _alertCooldown = _random.RandfRange(_table.AlertCooldown.X, _table.AlertCooldown.Y);
        StartIdle();
    }

    // --- Outside events ---

    /// <summary>Bolts away from a point (a fall, a sword swing, a blow); its herd runs with it.</summary>
    public void FleeFrom(Vector3 point)
    {
        if (Species == null || _table == null || State == Mode.Shove)
        {
            return;
        }
        StartFlee(point, false, true);
    }

    /// <summary>A loud noise at a point (a hard landing): skittish and wary animals bolt, the others look up, uneasy.</summary>
    public void Startle(Vector3 point)
    {
        if (Species == null || _table == null || State is Mode.Shove or Mode.Flee)
        {
            return;
        }
        if (Species.Temperament is Temperament.Skittish or Temperament.Wary)
        {
            StartFlee(point, false, true);
            return;
        }
        _uneasy = _table.UneasySeconds;
        EnterAlert(point, Species.AlertSeconds * 1.5f, true);
    }

    // --- Body and model ---

    /// <summary>Turns toward a direction and returns the walking velocity along it.</summary>
    private Vector3 Move(Vector3 direction, float speed, float dt)
    {
        Turn(direction, dt);
        return direction * speed;
    }

    /// <summary>Moves the body: walking on the ground under gravity, or floating at its height for flyers.</summary>
    private void Step(Vector3 walk, float dt, PhysicsDirectSpaceState3D space)
    {
        float vertical = Velocity.Y;
        if (Species!.HoverHeight > 0.0f)
        {
            float ground = GroundBelow(space, out bool found);
            HasGround = found;
            vertical = 0.0f;
            if (found)
            {
                vertical = (ground + Species.HoverHeight - GlobalPosition.Y) * 4.0f;
            }
        }
        else if (IsOnFloor())
        {
            // Refresh contact even when grazing: streamed terrain or another animal may have moved.
            // IsOnFloor describes the previous MoveAndSlide, not the current world's support.
            vertical = -0.5f;
        }
        else
        {
            vertical -= _gravity * dt;
        }
        Velocity = new Vector3(walk.X + _knock.X, vertical, walk.Z + _knock.Z);
        _knock = _knock.MoveToward(Vector3.Zero, KnockDecay * dt);
        MoveAndSlide();
        if (Species.HoverHeight <= 0.0f)
        {
            HasGround = IsOnFloor();
        }
    }

    /// <summary>Leans the model (not the body) so it lies along the slope underfoot; lowers it while lying down.</summary>
    private void LeanToGround(float dt)
    {
        if (_model == null || Species!.HoverHeight > 0.0f)
        {
            return;
        }
        Vector3 up = Vector3.Up;
        if (IsOnFloor())
        {
            up = GlobalBasis.Inverse() * GetFloorNormal();
        }
        Quaternion target = new Quaternion(Vector3.Up, up.Normalized()).Normalized();
        _tilt = _tilt.Normalized().Slerp(target, Mathf.Clamp(_table!.TiltSpeed * dt, 0.0f, 1.0f)).Normalized();
        float sink = State == Mode.Rest && Species.RestClip.Length == 0 ? Species.RestSink * Species.BodySize.Y : 0.0f;
        _sink = Mathf.MoveToward(_sink, sink, dt * 0.6f);
        _model.Transform = new Transform3D(new Basis(_tilt), Vector3.Down * _sink) * _modelBase;
    }

    /// <summary>Turns toward a horizontal direction (the model faces -Z).</summary>
    private void Turn(Vector3 direction, float dt)
    {
        float target = Mathf.Atan2(-direction.X, -direction.Z);
        float step = Mathf.DegToRad(Species!.TurnSpeed) * dt;
        float yaw = Rotation.Y;
        float difference = Mathf.Wrap(target - yaw, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0.0f, yaw + Mathf.Clamp(difference, -step, step), 0.0f);
    }

    /// <summary>Height of the ground under the animal (flyers keep their height above it).</summary>
    private float GroundBelow(PhysicsDirectSpaceState3D space, out bool found)
    {
        Vector3 from = GlobalPosition + Vector3.Up * 4.0f;
        Vector3 to = from + Vector3.Down * (12.0f + Species!.HoverHeight);
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = _table!.GroundMask };
        _ray.From = from;
        _ray.To = to;
        Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
        found = hit.Count > 0;
        if (!found)
        {
            return GlobalPosition.Y;
        }
        return ((Vector3)hit["position"]).Y;
    }

    /// <summary>Walking: its walk clip, or its flee clip slowed to a hop for the packs without one.</summary>
    private void PlayMove(float speed)
    {
        if (Species!.WalkClip.Length > 0)
        {
            Play(Species.WalkClip, speed, -1.0f);
        }
        else
        {
            Play(Species.FleeClip, Species.FleeClipSpeed * 0.6f * speed, -1.0f);
        }
    }

    /// <summary>A clip if the model has it, else the idle clip.</summary>
    private string ClipOr(string clip)
    {
        return clip.Length > 0 && _animation != null && _animation.HasAnimation(clip) ? clip : Species!.IdleClip;
    }

    private void Play(string clip, float speed, float startFraction)
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
        Animation animation = _animation.GetAnimation(clip);
        animation.LoopMode = Animation.LoopModeEnum.Linear;
        _animation.Play(clip, 0.25f);
        if (startFraction >= 0.0f)
        {
            _animation.Seek(animation.Length * startFraction, true);
        }
    }

    private static AnimationPlayer? FindPlayer(Node root)
    {
        if (root is AnimationPlayer player)
        {
            return player;
        }
        foreach (Node child in root.GetChildren())
        {
            AnimationPlayer? found = FindPlayer(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
