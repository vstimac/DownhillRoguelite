using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One animal: grazes, wanders a few metres now and then, turns to watch the carrier go by, and
/// bolts away when the carrier comes too close, sprints nearby or falls (Scatter). A physics body:
/// a capsule sized to the species walks on the ground with gravity, slides on what's too steep and
/// bumps into rocks, other animals and the carrier; its model leans to the slope underfoot so legs
/// and head don't sink in. Flyers float at their height above the ground. The spawner drives it
/// through Tick so all animals together can be timed.
/// </summary>
public partial class WildlifeAnimal : CharacterBody3D
{
    public enum Mode
    {
        Idle,
        Walk,
        Flee,
    }

    public WildlifeSpecies? Species { get; private set; }
    public Mode State { get; private set; } = Mode.Idle;
    public bool HasGround { get; private set; }
    /// <summary>Surface and slope where the spawner placed it (empty when placed by hand).</summary>
    public string SpawnSurface { get; set; } = "";
    public float SpawnSlope { get; set; }

    private WildlifeTable? _table;
    private RandomNumberGenerator _random = new();
    private AnimationPlayer? _animation;
    private string _clip = "";
    private float _timer;
    private Vector3 _target;
    private Vector3 _fleeDirection;
    private PhysicsRayQueryParameters3D? _ray;
    private Node3D? _model;
    private Transform3D _modelBase;
    private Quaternion _tilt = Quaternion.Identity;
    private float _gravity = 9.8f;

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
        }
        Rotation = new Vector3(0.0f, _random.RandfRange(0.0f, Mathf.Tau), 0.0f);
        _timer = _random.RandfRange(0.0f, table.WanderInterval.Y);
        Play(species.IdleClip, 1.0f, _random.Randf());
    }

    /// <summary>Advances the animal by dt given where the carrier is and whether it sprints.</summary>
    public void Tick(float dt, Vector3 carrier, bool sprinting, PhysicsDirectSpaceState3D space)
    {
        if (Species == null || _table == null)
        {
            return;
        }
        Vector3 toCarrier = carrier - GlobalPosition;
        toCarrier.Y = 0.0f;
        float distance = toCarrier.Length();

        if (State != Mode.Flee)
        {
            float shy = Species.FleeDistance;
            if (sprinting)
            {
                shy = Species.SprintFleeDistance;
            }
            if (distance < shy)
            {
                FleeFrom(carrier);
            }
        }

        Vector3 walk = Vector3.Zero;
        if (State == Mode.Flee)
        {
            walk = Move(_fleeDirection, Species.FleeSpeed, dt);
            _timer -= dt;
            if (_timer <= 0.0f)
            {
                Rest();
            }
        }
        else if (State == Mode.Walk)
        {
            Vector3 toTarget = _target - GlobalPosition;
            toTarget.Y = 0.0f;
            _timer -= dt;
            if (toTarget.Length() < 0.3f || _timer <= 0.0f)
            {
                Rest();
            }
            else
            {
                walk = Move(toTarget.Normalized(), Species.WalkSpeed, dt);
            }
        }
        else
        {
            if (distance < Species.WatchDistance && distance > 0.01f)
            {
                Turn(toCarrier / distance, dt * 0.5f);
            }
            _timer -= dt;
            if (_timer <= 0.0f)
            {
                Wander();
            }
        }

        Step(walk, dt, space);
        LeanToGround(dt);
    }

    /// <summary>Bolts away from a point (a fall, a sprinting carrier).</summary>
    public void FleeFrom(Vector3 point)
    {
        if (Species == null || _table == null)
        {
            return;
        }
        Vector3 away = GlobalPosition - point;
        away.Y = 0.0f;
        if (away.LengthSquared() < 0.01f)
        {
            away = new Vector3(_random.RandfRange(-1.0f, 1.0f), 0.0f, _random.RandfRange(-1.0f, 1.0f));
        }
        // Not straight away: animals veer a little, so a herd spreads out.
        _fleeDirection = away.Normalized().Rotated(Vector3.Up, _random.RandfRange(-0.5f, 0.5f));
        _timer = _table.FleeSeconds * _random.RandfRange(0.8f, 1.2f);
        State = Mode.Flee;
        Play(Species.FleeClip, Species.FleeClipSpeed, 0.0f);
    }

    private void Rest()
    {
        State = Mode.Idle;
        _timer = _random.RandfRange(_table!.WanderInterval.X, _table.WanderInterval.Y);
        Play(Species!.IdleClip, 1.0f, -1.0f);
    }

    private void Wander()
    {
        if (Species!.WalkClip.Length == 0)
        {
            _timer = _random.RandfRange(_table!.WanderInterval.X, _table.WanderInterval.Y);
            return;
        }
        float angle = _random.RandfRange(0.0f, Mathf.Tau);
        _target = GlobalPosition + new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * _table!.WanderDistance;
        _timer = _table.WanderDistance / Mathf.Max(Species.WalkSpeed, 0.1f) * 2.0f;
        State = Mode.Walk;
        Play(Species.WalkClip, 1.0f, -1.0f);
    }

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
            // Standing still on the ground: nothing to move, so skip the physics step (most animals graze).
            if (walk.LengthSquared() < 0.0001f && Velocity.LengthSquared() < 0.25f)
            {
                Velocity = Vector3.Zero;
                return;
            }
            vertical = -0.5f;
        }
        else
        {
            vertical -= _gravity * dt;
        }
        Velocity = new Vector3(walk.X, vertical, walk.Z);
        MoveAndSlide();
        if (Species.HoverHeight <= 0.0f)
        {
            HasGround = IsOnFloor();
        }
    }

    /// <summary>Leans the model (not the body) so it lies along the slope underfoot.</summary>
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
        _model.Transform = new Transform3D(new Basis(_tilt), Vector3.Zero) * _modelBase;
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
