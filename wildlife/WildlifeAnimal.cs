using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One animal: grazes, wanders a few metres now and then, turns to watch the carrier go by, and
/// bolts away when the carrier comes too close, sprints nearby or falls (Scatter). Stays on the
/// ground by a downward ray (flyers hover above it). No collision: it is scenery. The spawner
/// drives it through Tick so all animals together can be timed.
/// </summary>
public partial class WildlifeAnimal : Node3D
{
    public enum Mode
    {
        Idle,
        Walk,
        Flee,
    }

    public WildlifeSpecies? Species { get; private set; }
    public Mode State { get; private set; } = Mode.Idle;
    /// <summary>Height of the animal's base above the ground under it (minus hover), in m; for tests.</summary>
    public float GroundGap { get; private set; }
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

    public void Setup(WildlifeSpecies species, WildlifeTable table, ulong seed)
    {
        Species = species;
        _table = table;
        _random.Seed = seed;
        Name = species.DisplayName;
        if (species.Model != null)
        {
            Node3D model = species.Model.Instantiate<Node3D>();
            model.Scale = Vector3.One * species.ModelScale;
            model.RotationDegrees = species.ModelRotationDegrees;
            AddChild(model);
            _animation = FindPlayer(model);
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

        if (State == Mode.Flee)
        {
            Move(_fleeDirection, Species.FleeSpeed, dt);
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
                Move(toTarget.Normalized(), Species.WalkSpeed, dt);
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

        SnapToGround(space);
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

    private void Move(Vector3 direction, float speed, float dt)
    {
        Turn(direction, dt);
        GlobalPosition += direction * speed * dt;
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

    private void SnapToGround(PhysicsDirectSpaceState3D space)
    {
        Vector3 from = GlobalPosition + Vector3.Up * (4.0f + Species!.HoverHeight);
        Vector3 to = from + Vector3.Down * (12.0f + Species.HoverHeight);
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = _table!.GroundMask };
        _ray.From = from;
        _ray.To = to;
        Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
        if (hit.Count == 0)
        {
            HasGround = false;
            return;
        }
        HasGround = true;
        float ground = ((Vector3)hit["position"]).Y;
        GlobalPosition = new Vector3(GlobalPosition.X, ground + Species.HoverHeight, GlobalPosition.Z);
        GroundGap = GlobalPosition.Y - Species.HoverHeight - ground;
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
