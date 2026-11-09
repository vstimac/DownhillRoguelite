using System.Collections.Generic;
using System.Diagnostics;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Keeps a small population of animals around the carrier: groups are placed in a ring, mostly
/// ahead downhill, on ground found by a ray, as species that live in the current circle on that
/// surface and slope; animals far away or well behind are removed, so the cost stays flat as the
/// carrier descends. A fall (DownedChanged) scatters every animal nearby; a new run clears them.
/// Reads LocomotionUpdated (where the carrier is), VitalsUpdated (sprinting) and RunState
/// (circle). Times itself (AverageMilliseconds) so tests can hold it under budget.
/// </summary>
public partial class WildlifeSpawner : Node3D
{
    [Export] public WildlifeTable? Table { get; set; }
    /// <summary>Off stops spawning (existing animals keep living); for tests.</summary>
    [Export] public bool Spawning { get; set; } = true;
    /// <summary>Forces the circle (tests, debugging); -1 follows the run's depth.</summary>
    [Export] public int CircleOverride { get; set; } = -1;

    public IReadOnlyList<WildlifeAnimal> Animals => _animals;
    /// <summary>Smoothed cost of all wildlife per physics frame, in ms.</summary>
    public float AverageMilliseconds { get; private set; }
    public int Circle { get; private set; }
    /// <summary>Where the carrier was last seen (LocomotionUpdated).</summary>
    public Vector3 Carrier => _carrier;
    /// <summary>Population RNG seed; set before the first tick to replay a wildlife scenario.</summary>
    public ulong PopulationSeed
    {
        get => _random.Seed;
        set => _random.Seed = value;
    }

    private readonly List<WildlifeAnimal> _animals = new();
    private readonly RandomNumberGenerator _random = new();
    private readonly Stopwatch _watch = new();
    private RunState? _run;
    private Vector3 _carrier;
    private Vector3 _carrierVelocity;
    private Vector3 _carrierInput;
    private bool _hasCarrier;
    private bool _sprinting;
    private float _spawnTimer;
    private ulong _serial;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _random.Randomize();
        Table ??= new WildlifeTable();
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.VitalsUpdated += OnVitalsUpdated;
        GameEvents.DownedChanged += OnDownedChanged;
        GameEvents.RespawnRequested += Clear;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.VitalsUpdated -= OnVitalsUpdated;
        GameEvents.DownedChanged -= OnDownedChanged;
        GameEvents.RespawnRequested -= Clear;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasCarrier || Table == null)
        {
            return;
        }
        _watch.Restart();
        float dt = (float)delta;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        if (CircleOverride >= 0)
        {
            Circle = CircleOverride;
        }
        else if (_run != null)
        {
            Circle = _run.Circles.CircleAt(_run.Depth);
        }

        for (int i = _animals.Count - 1; i >= 0; i--)
        {
            WildlifeAnimal animal = _animals[i];
            Vector3 offset = animal.GlobalPosition - _carrier;
            float horizontal = new Vector2(offset.X, offset.Z).Length();
            // The route runs toward -Z: +Z is uphill, behind the carrier.
            if (horizontal > Table.DespawnDistance || offset.Z > Table.BehindDistance)
            {
                animal.QueueFree();
                _animals.RemoveAt(i);
                continue;
            }
            animal.Tick(dt, _carrier, _carrierInput, _sprinting, space);
        }

        _spawnTimer -= dt;
        if (Spawning && _spawnTimer <= 0.0f && _animals.Count < Table.MaxAnimals)
        {
            _spawnTimer = Table.SpawnInterval;
            TrySpawnGroup(space);
        }

        _watch.Stop();
        float ms = (float)_watch.Elapsed.TotalMilliseconds;
        AverageMilliseconds = Mathf.Lerp(AverageMilliseconds, ms, 0.05f);
    }

    /// <summary>Every animal within the scatter radius of a point (measured flat, as slopes are steep) bolts away from it.</summary>
    public int ScatterAt(Vector3 point)
    {
        if (Table == null)
        {
            return 0;
        }
        int scattered = 0;
        foreach (WildlifeAnimal animal in _animals)
        {
            Vector3 offset = animal.GlobalPosition - point;
            if (new Vector2(offset.X, offset.Z).Length() <= Table.ScatterRadius)
            {
                animal.FleeFrom(point);
                scattered++;
            }
        }
        return scattered;
    }

    /// <summary>Removes every animal.</summary>
    public void Clear()
    {
        foreach (WildlifeAnimal animal in _animals)
        {
            animal.QueueFree();
        }
        _animals.Clear();
    }

    /// <summary>Places one animal of a species at a ground point; the surface and slope it was placed on are kept for checks.</summary>
    public WildlifeAnimal Spawn(WildlifeSpecies species, Vector3 groundPoint, string surface = "", float slopeDegrees = 0.0f)
    {
        WildlifeAnimal animal = new();
        AddChild(animal);
        animal.Setup(species, Table!, ++_serial * 0x9E3779B97F4A7C15ul);
        animal.SpawnSurface = surface;
        animal.SpawnSlope = slopeDegrees;
        // A touch above the ground: the body settles onto it under gravity.
        animal.GlobalPosition = groundPoint + Vector3.Up * (species.HoverHeight + 0.05f);
        _animals.Add(animal);
        return animal;
    }

    private void TrySpawnGroup(PhysicsDirectSpaceState3D space)
    {
        Vector3 ahead = new(_carrierVelocity.X, 0.0f, _carrierVelocity.Z);
        if (ahead.Length() < 0.5f)
        {
            ahead = Vector3.Forward;
        }
        ahead = ahead.Normalized();
        Vector3 direction;
        if (_random.Randf() < Table!.AheadBias)
        {
            direction = ahead.Rotated(Vector3.Up, _random.RandfRange(-1.0f, 1.0f));
        }
        else
        {
            direction = Vector3.Forward.Rotated(Vector3.Up, _random.RandfRange(0.0f, Mathf.Tau));
        }
        Vector3 centre = _carrier + direction * _random.RandfRange(Table.SpawnRing.X, Table.SpawnRing.Y);
        if (!Ground(space, centre, out Vector3 point, out string surface, out float slope))
        {
            return;
        }
        WildlifeSpecies? species = PickSpecies(surface, slope);
        if (species == null)
        {
            return;
        }
        string spotSurface = surface;
        float spotSlope = slope;
        int size = _random.RandiRange(species.GroupSize.X, Mathf.Max(species.GroupSize.X, species.GroupSize.Y));
        size = Mathf.Min(size, Table.MaxAnimals - _animals.Count);
        for (int i = 0; i < size; i++)
        {
            Vector3 spot = point;
            if (i > 0)
            {
                float angle = _random.RandfRange(0.0f, Mathf.Tau);
                spot += new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * _random.RandfRange(1.0f, species.GroupSpread);
                if (!Ground(space, spot, out spot, out spotSurface, out spotSlope) || !species.LivesOn(spotSurface, spotSlope))
                {
                    continue;
                }
            }
            Spawn(species, spot, spotSurface, spotSlope);
        }
    }

    private WildlifeSpecies? PickSpecies(string surface, float slope)
    {
        float total = 0.0f;
        foreach (WildlifeSpecies species in Table!.Species)
        {
            if (species.LivesIn(Circle) && species.LivesOn(surface, slope))
            {
                total += species.Weight;
            }
        }
        if (total <= 0.0f)
        {
            return null;
        }
        float pick = _random.Randf() * total;
        foreach (WildlifeSpecies species in Table.Species)
        {
            if (!species.LivesIn(Circle) || !species.LivesOn(surface, slope))
            {
                continue;
            }
            pick -= species.Weight;
            if (pick <= 0.0f)
            {
                return species;
            }
        }
        return null;
    }

    /// <summary>The ground under a point: where, what surface and how steep.</summary>
    public bool Ground(PhysicsDirectSpaceState3D space, Vector3 at, out Vector3 point, out string surface, out float slopeDegrees)
    {
        point = at;
        surface = "";
        slopeDegrees = 0.0f;
        Vector3 from = new(at.X, _carrier.Y + 120.0f, at.Z);
        Vector3 to = new(at.X, _carrier.Y - 250.0f, at.Z);
        PhysicsRayQueryParameters3D ray = PhysicsRayQueryParameters3D.Create(from, to, Table!.GroundMask);
        Godot.Collections.Dictionary hit = space.IntersectRay(ray);
        if (hit.Count == 0)
        {
            return false;
        }
        point = (Vector3)hit["position"];
        Vector3 normal = (Vector3)hit["normal"];
        slopeDegrees = Mathf.RadToDeg(normal.AngleTo(Vector3.Up));
        SurfaceType? type = SurfaceLookup.Find((GodotObject)hit["collider"], point);
        if (type != null)
        {
            surface = type.DisplayName;
        }
        return true;
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot.Position;
        _carrierVelocity = snapshot.Velocity;
        _carrierInput = snapshot.InputDirection;
        _hasCarrier = true;
    }

    private void OnVitalsUpdated(VitalsSnapshot vitals)
    {
        _sprinting = vitals.Sprinting;
    }

    private void OnDownedChanged(bool downed)
    {
        if (downed)
        {
            ScatterAt(_carrier);
        }
    }
}
