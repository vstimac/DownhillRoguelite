using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Brings humanoid enemies (HumanoidEnemy) onto the route: now and then one, or a pair deeper down,
/// appears on the ground ahead of the carrier, off to a side, and wanders until it sees them. More of
/// them, and tougher, the deeper the circle. None come while the carrier shelters, sits by a station's
/// fire or is down; ones left far behind are removed.
///
/// Debug: Alt+N puts the next kind ten metres ahead;
/// `-- --enemies-now` brings the first ones at once.
/// </summary>
public partial class EnemyDirector : Node3D
{
    [Export] public EnemySpecies[] Species { get; set; } = System.Array.Empty<EnemySpecies>();
    /// <summary>Seconds before the first arrive, and between arrivals.</summary>
    [Export] public Vector2 FirstDelay { get; set; } = new(90.0f, 150.0f);
    [Export] public Vector2 Interval { get; set; } = new(120.0f, 240.0f);
    /// <summary>Most enemies at once in Limbo; one more every EnemiesPerCircles circles.</summary>
    [Export] public int BaseEnemies { get; set; } = 1;
    [Export] public int EnemiesPerCircles { get; set; } = 2;
    [Export] public int MaxEnemies { get; set; } = 4;
    /// <summary>From this circle on, they come in pairs.</summary>
    [Export] public int PairsFromCircle { get; set; } = 3;
    /// <summary>Extra health per circle (0.1 = +10% each circle down).</summary>
    [Export] public float ToughnessPerCircle { get; set; } = 0.1f;
    /// <summary>How far ahead they appear (m), how far to the sides (degrees each side of straight ahead).</summary>
    [Export] public Vector2 SpawnDistance { get; set; } = new(35.0f, 50.0f);
    [Export] public float SpawnSpread { get; set; } = 70.0f;
    [Export] public float MaxSpawnDrop { get; set; } = 25.0f;
    [Export] public int SpawnAttempts { get; set; } = 8;
    /// <summary>Beyond this they are removed.</summary>
    [Export] public float GoneDistance { get; set; } = 90.0f;
    [Export] public float FireRadius { get; set; } = 25.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    [Export] public Key SpawnKey { get; set; } = Key.N;
    /// <summary>Debug spawns this far ahead of the carrier (m).</summary>
    [Export] public float DebugDistance { get; set; } = 10.0f;
    [Export] public int CircleOverride { get; set; } = -1;
    /// <summary>Off: only the debug key brings them.</summary>
    [Export] public bool Arrivals { get; set; } = true;

    public IReadOnlyList<HumanoidEnemy> Enemies => _enemies;

    private readonly List<HumanoidEnemy> _enemies = new();
    private readonly RandomNumberGenerator _random = new();
    private RunState? _run;
    private LocomotionSnapshot _carrier;
    private bool _hasCarrier;
    private float _next;
    private int _debugKind;
    private PhysicsRayQueryParameters3D? _ray;

    public override void _Ready()
    {
        _random.Randomize();
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _next = _random.RandfRange(FirstDelay.X, FirstDelay.Y);
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "--enemies-now")
            {
                _next = 1.0f;
            }
        }
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.NewMountain += OnNewMountain;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.NewMountain -= OnNewMountain;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot;
        _hasCarrier = true;
    }

    private void OnNewMountain(ulong seed) => Clear();

    public override void _PhysicsProcess(double delta)
    {
        _enemies.RemoveAll(enemy => !IsInstanceValid(enemy) || enemy.IsQueuedForDeletion());
        if (!_hasCarrier)
        {
            return;
        }
        foreach (HumanoidEnemy enemy in _enemies)
        {
            Vector3 apart = enemy.GlobalPosition - _carrier.Position;
            if (new Vector2(apart.X, apart.Z).Length() > GoneDistance)
            {
                enemy.QueueFree();
            }
        }
        if (!Arrivals)
        {
            return;
        }
        _next -= (float)delta;
        if (_next > 0.0f)
        {
            return;
        }
        _next = _random.RandfRange(Interval.X, Interval.Y);
        if (GameEvents.IsDowned || GameEvents.InShelter || NearFire(_carrier.Position) || Species.Length == 0)
        {
            return;
        }
        int room = MostEnemies() - _enemies.Count;
        int count = 1;
        if (Circle() >= PairsFromCircle)
        {
            count = 2;
        }
        count = Mathf.Min(count, room);
        if (count <= 0)
        {
            return;
        }
        EnemySpecies species = Species[_random.RandiRange(0, Species.Length - 1)];
        if (!Spot(out Vector3 ground))
        {
            return;
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 beside = new(_random.RandfRange(-2.5f, 2.5f), 0.0f, _random.RandfRange(-2.5f, 2.5f));
            Bring(species, ground + beside * i);
        }
    }

    /// <summary>Puts an enemy of this kind on the ground at <paramref name="at"/>, turned toward the carrier.</summary>
    public HumanoidEnemy Bring(EnemySpecies species, Vector3 at)
    {
        HumanoidEnemy enemy = HumanoidEnemy.Create(species);
        enemy.Toughness = 1.0f + ToughnessPerCircle * Circle();
        AddChild(enemy);
        enemy.GlobalPosition = at + Vector3.Up * 0.2f;
        Vector3 look = _carrier.Position - at;
        enemy.Rotation = new Vector3(0.0f, Mathf.Atan2(-look.X, -look.Z), 0.0f);
        if (_hasCarrier)
        {
            enemy.Track(_carrier.Position);
        }
        _enemies.Add(enemy);
        return enemy;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != SpawnKey || !key.AltPressed)
        {
            return;
        }
        if (GetTree().Paused || GameEvents.IsScreenOpen || GameEvents.IsConversing || Species.Length == 0 || !_hasCarrier)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        EnemySpecies species = Species[_debugKind % Species.Length];
        _debugKind++;
        Vector3 ahead = Ahead();
        Bring(species, Ground(_carrier.Position + ahead * DebugDistance));
    }

    /// <summary>Removes every enemy (a new mountain).</summary>
    public void Clear()
    {
        foreach (HumanoidEnemy enemy in _enemies)
        {
            if (IsInstanceValid(enemy))
            {
                enemy.QueueFree();
            }
        }
        _enemies.Clear();
        _next = _random.RandfRange(FirstDelay.X, FirstDelay.Y);
    }

    public int MostEnemies()
    {
        int extra = Circle() / Mathf.Max(EnemiesPerCircles, 1);
        return Mathf.Min(BaseEnemies + extra, MaxEnemies);
    }

    private int Circle()
    {
        if (CircleOverride >= 0)
        {
            return CircleOverride;
        }
        if (_run != null)
        {
            return _run.Circle;
        }
        return 0;
    }

    /// <summary>Where the carrier looks (their facing, flat), or down the route (-Z).</summary>
    private Vector3 Ahead()
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        Vector3 ahead = Vector3.Forward;
        if (camera != null)
        {
            ahead = -camera.GlobalBasis.Z;
        }
        ahead.Y = 0.0f;
        if (ahead.LengthSquared() < 0.0001f)
        {
            return Vector3.Forward;
        }
        return ahead.Normalized();
    }

    /// <summary>The ground under a spot (or the spot itself if there is none).</summary>
    private Vector3 Ground(Vector3 at)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = GroundMask };
        _ray.From = at + Vector3.Up * 40.0f;
        _ray.To = at + Vector3.Down * 40.0f;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        if (hit.Count == 0)
        {
            return at;
        }
        return (Vector3)hit["position"];
    }

    /// <summary>A spot on the ground ahead (down the route, -Z) and off to a side, not far above or below the carrier.</summary>
    private bool Spot(out Vector3 ground)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = GroundMask };
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 carrier = _carrier.Position;
        for (int attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            float angle = Mathf.DegToRad(_random.RandfRange(-SpawnSpread, SpawnSpread));
            float distance = _random.RandfRange(SpawnDistance.X, SpawnDistance.Y);
            Vector3 at = carrier + new Vector3(Mathf.Sin(angle), 0.0f, -Mathf.Cos(angle)) * distance;
            _ray.From = at + Vector3.Up * 120.0f;
            _ray.To = at + Vector3.Down * 250.0f;
            Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
            if (hit.Count == 0)
            {
                continue;
            }
            ground = (Vector3)hit["position"];
            if (Mathf.Abs(ground.Y - carrier.Y) <= MaxSpawnDrop)
            {
                return true;
            }
        }
        ground = carrier;
        return false;
    }

    /// <summary>Near a station's fire: the stations around the carrier's stage.</summary>
    private bool NearFire(Vector3 carrier)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return false;
        }
        int next = route.NextStationIndex(carrier.Z);
        for (int index = Mathf.Max(1, next - 1); index <= next; index++)
        {
            Vector3 fire = route.Station(index);
            if (new Vector2(fire.X - carrier.X, fire.Z - carrier.Z).Length() < FireRadius)
            {
                return true;
            }
        }
        return false;
    }
}
