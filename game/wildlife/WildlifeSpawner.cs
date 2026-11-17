using System.Collections.Generic;
using System.Diagnostics;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Keeps a small population of animals around the carrier: groups are placed in a ring, mostly
/// ahead downhill, on ground found by a ray, as species that live in the current circle on that
/// surface and slope (a group of two or more is a herd: WildlifeHerd); animals far away or well
/// behind are removed, so the cost stays flat as the carrier descends. Each animal watches the
/// nearest carrier standing (CarrierSense: where, how fast, sprinting, falling, how loaded; a
/// guest's speed comes from its streamed feet; crouching is the brake key held; daylight from the
/// WorldClock). A fall (DownedChanged, a guest's fall) or a sword swing scatters every animal nearby,
/// a hard landing startles them; killed animals are removed after a while; a new run clears them.
/// Adds the stealth eye (StealthEye) to the screen.
/// Predators (wolves, PredatorTuning) are placed more often at night, one pack at a time, and only
/// while PredatorTuning.Enabled; each picks its prey among all carriers (fallen ones first, never one
/// at a station's fire, sticking with its prey: PickPrey), and every machine tells its own carrier
/// once a run when wolves hunt it and when one crouches to spring.
/// Reads LocomotionUpdated, VitalsUpdated (sprinting), LoadChanged, Landed, MeleeSwung and
/// RunState (circle). Times itself (AverageMilliseconds) so tests can hold it under budget.
/// </summary>
public partial class WildlifeSpawner : Node3D
{
    /// <summary>A carrier a predator may hunt: who (0 = the host's own), where, fallen, bleeding out, at a station's fire.</summary>
    public readonly record struct Prey(long Peer, Vector3 Position, bool Downed, bool Bleeding, bool AtFire);

    [Export] public WildlifeTable? Table { get; set; }
    /// <summary>Off stops spawning (existing animals keep living); for tests.</summary>
    [Export] public bool Spawning { get; set; } = true;
    /// <summary>Forces the circle (tests, debugging); -1 follows the run's depth.</summary>
    [Export] public int CircleOverride { get; set; } = -1;

    /// <summary>The input that crouches the carrier (braking crouches it while walking; held standing still, it crouches too).</summary>
    public const string CrouchAction = "player_brake";

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
    private bool _carrierFalling;
    private bool _crouching;
    private WorldClock? _clock;
    private float _loadMass;
    private bool _hasCarrier;
    /// <summary>Guests' feet last frame and their smoothed velocity, for their CarrierSense.</summary>
    private readonly Dictionary<long, (Vector3 Feet, Vector3 Velocity)> _guests = new();
    private bool _sprinting;
    private float _spawnTimer;
    private ulong _serial;
    private readonly List<Prey> _prey = new();
    private float _hintTimer;
    private bool _toldStalk;
    private bool _toldWindUp;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        AddChild(new StealthEye { Name = "StealthEye" });
        _random.Randomize();
        Table ??= new WildlifeTable();
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.VitalsUpdated += OnVitalsUpdated;
        GameEvents.DownedChanged += OnDownedChanged;
        GameEvents.RespawnRequested += Clear;
        GameEvents.RespawnRequested += ForgetHints;
        GameEvents.SharedCarrierFell += OnSharedFall;
        GameEvents.SharedCarrierSwung += OnSharedSwing;
        GameEvents.LoadChanged += OnLoadChanged;
        GameEvents.Landed += OnLanded;
        GameEvents.MeleeSwung += OnMeleeSwung;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.VitalsUpdated -= OnVitalsUpdated;
        GameEvents.DownedChanged -= OnDownedChanged;
        GameEvents.RespawnRequested -= Clear;
        GameEvents.RespawnRequested -= ForgetHints;
        GameEvents.SharedCarrierFell -= OnSharedFall;
        GameEvents.SharedCarrierSwung -= OnSharedSwing;
        GameEvents.LoadChanged -= OnLoadChanged;
        GameEvents.Landed -= OnLanded;
        GameEvents.MeleeSwung -= OnMeleeSwung;
    }

    private int _tick;
    private void OnSharedFall(Vector3 at) { if (GameEvents.SharedWorldHost) { ScatterAt(at); } }
    private void OnSharedSwing(Vector3 at) { if (GameEvents.SharedWorldHost && Table != null) { ScatterAt(at, Table.SwingScatterRadius); } }

    public override void _PhysicsProcess(double delta)
    {
        WolfHints((float)delta);
        if (!GameEvents.MaySimulateWorld) { if (_animals.Count > 0) { Clear(); } return; }
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
        TrackGuests(dt);
        GatherPrey();

        for (int i = _animals.Count - 1; i >= 0; i--)
        {
            WildlifeAnimal animal = _animals[i];
            Vector3 nearestCarrier = WorldInterests.Nearest(animal.GlobalPosition, _carrier);
            Vector3 offset = animal.GlobalPosition - nearestCarrier;
            float horizontal = new Vector2(offset.X, offset.Z).Length();
            // The route runs toward -Z: +Z is uphill, behind the carrier.
            if (horizontal > Table.DespawnDistance || offset.Z > Table.BehindDistance || animal.Gone)
            {
                animal.Herd?.Leave(animal);
                animal.QueueFree();
                _animals.RemoveAt(i);
                continue;
            }
            // Far, calm animals step less often: nobody sees a grazing sheep 100 m off at 60 Hz.
            bool calm = animal.IsCalm;
            int every = Mathf.Max(Table.FarTickEvery, 1);
            if (calm && every > 1 && horizontal > Table.FarDistance)
            {
                if ((_tick + i) % every != 0)
                {
                    continue;
                }
                TickAnimal(animal, dt * every, space);
                continue;
            }
            TickAnimal(animal, dt, space);
        }
        _tick++;

        _spawnTimer -= dt;
        if (Spawning && _spawnTimer <= 0.0f && _animals.Count < Table.MaxAnimals)
        {
            _spawnTimer = Table.SpawnInterval;
            Vector3 original = _carrier;
            _carrier = WorldInterests.At(_tick, original);
            // Off the path the ground climbs into steep rock and scree, where most spots suit no animal:
            // try a few places per interval so the population still fills up (each try is a few rays).
            for (int attempt = 0; attempt < SpawnAttempts && !TrySpawnGroup(space); attempt++)
            {
            }
            _carrier = original;
        }

        _watch.Stop();
        float ms = (float)_watch.Elapsed.TotalMilliseconds;
        AverageMilliseconds = Mathf.Lerp(AverageMilliseconds, ms, 0.05f);
    }

    /// <summary>Ticks an animal against the nearest carrier standing (the host's own, or a guest's); a predator against its prey.</summary>
    private void TickAnimal(WildlifeAnimal animal, float dt, PhysicsDirectSpaceState3D space)
    {
        float daylight = _clock?.Daylight ?? 1.0f;
        if (animal.IsPredator && _prey.Count > 0)
        {
            int pick = PickPrey(animal.GlobalPosition, _prey, animal.TargetPeer);
            Prey prey = _prey[pick];
            animal.TargetPeer = prey.Peer;
            CarrierSense hunted;
            if (prey.Peer == 0)
            {
                hunted = new CarrierSense(_carrier, _carrierVelocity, _carrierInput, _sprinting, _carrierFalling, _loadMass,
                    _crouching, GameEvents.CarrierTorchLit, daylight);
            }
            else
            {
                NetCarrier other = System.Array.Find(GameEvents.OtherCarriers, c => c.Peer == prey.Peer);
                Vector3 velocity = _guests.TryGetValue(other.Peer, out var guest) ? guest.Velocity : Vector3.Zero;
                hunted = new CarrierSense(other.Feet, velocity, other.InputDirection, other.Sprinting, false, other.LoadMass,
                    other.Crouching, other.TorchLit, daylight);
            }
            animal.Tick(dt, hunted with { AtFire = prey.AtFire, Downed = prey.Downed, Bleeding = prey.Bleeding }, space);
            return;
        }
        CarrierSense sense = new(_carrier, _carrierVelocity, _carrierInput, _sprinting, _carrierFalling, _loadMass,
            _crouching, GameEvents.CarrierTorchLit, daylight);
        animal.TargetPeer = 0;
        float nearest = animal.GlobalPosition.DistanceSquaredTo(_carrier);
        if (GameEvents.SharedWorldHost)
        {
            if (GameEvents.IsDowned || GameEvents.CarrierBleeding)
            {
                nearest = float.MaxValue;
                sense = sense with { Velocity = Vector3.Zero, Input = Vector3.Zero, Sprinting = false, Falling = false, Crouching = false };
            }
            foreach (NetCarrier other in GameEvents.OtherCarriers)
            {
                float distance = animal.GlobalPosition.DistanceSquaredTo(other.Feet);
                if (!other.Down && distance < nearest)
                {
                    nearest = distance;
                    Vector3 velocity = _guests.TryGetValue(other.Peer, out var guest) ? guest.Velocity : Vector3.Zero;
                    // A guest's crouch and load come in its pose flags (load in 20 kg steps).
                    sense = new CarrierSense(other.Feet, velocity, other.InputDirection, other.Sprinting, false, other.LoadMass,
                        other.Crouching, other.TorchLit, daylight);
                    animal.TargetPeer = other.Peer;
                }
            }
        }
        animal.Tick(dt, sense, space);
    }

    /// <summary>Every carrier a predator may hunt this frame: the host's own and each guest's.</summary>
    private void GatherPrey()
    {
        _prey.Clear();
        bool downed = GameEvents.IsDowned;
        bool bleeding = GameEvents.CarrierBleeding;
        _prey.Add(new Prey(0, _carrier, downed && !bleeding, bleeding, GameEvents.InShelter || NearFire(_carrier)));
        if (!GameEvents.SharedWorldHost)
        {
            return;
        }
        foreach (NetCarrier other in GameEvents.OtherCarriers)
        {
            _prey.Add(new Prey(other.Peer, other.Feet, other.Down && !other.Bleeding, other.Bleeding, NearFire(other.Feet)));
        }
    }

    /// <summary>
    /// The carrier a predator goes for: the nearest, a fallen one counting closer (a chance), its
    /// current prey counting closer too (it doesn't switch for a step), never one at a station's
    /// fire. With everyone at a fire it watches the nearest (and keeps off). Index into prey.
    /// </summary>
    public static int PickPrey(Vector3 from, IReadOnlyList<Prey> prey, long current)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        int nearest = 0;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < prey.Count; i++)
        {
            Prey candidate = prey[i];
            float distance = new Vector2(candidate.Position.X - from.X, candidate.Position.Z - from.Z).Length();
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
            if (candidate.AtFire)
            {
                continue;
            }
            float score = distance;
            if (candidate.Downed)
            {
                score *= PredatorTuning.DownedPull;
            }
            if (candidate.Peer == current)
            {
                score *= PredatorTuning.Loyalty;
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best >= 0 ? best : nearest;
    }

    /// <summary>Near a station's fire: the stations around a point's stage (the beasts' rule).</summary>
    public bool NearFire(Vector3 at)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return false;
        }
        int next = route.NextStationIndex(at.Z);
        for (int index = Mathf.Max(1, next - 1); index <= next; index++)
        {
            Vector3 fire = route.Station(index);
            if (new Vector2(fire.X - at.X, fire.Z - at.Z).Length() < PredatorTuning.FireRadius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// On every machine: tells its own carrier once a run when wolves hunt it, and when one first
    /// crouches to spring at it (read from the host's own wolves or a guest's puppets alike).
    /// </summary>
    private void WolfHints(float dt)
    {
        _hintTimer -= dt;
        if (_hintTimer > 0.0f || !_hasCarrier || (_toldStalk && _toldWindUp))
        {
            return;
        }
        _hintTimer = 0.25f;
        foreach (Node node in GetTree().GetNodesInGroup(StealthEye.AnimalGroup))
        {
            if (node is not WildlifeAnimal animal || !animal.IsPredator || animal.IsDead)
            {
                continue;
            }
            float distance = new Vector2(animal.GlobalPosition.X - _carrier.X, animal.GlobalPosition.Z - _carrier.Z).Length();
            if (distance > PredatorTuning.HintRange)
            {
                continue;
            }
            // On the host only the wolves after its own carrier; on a guest every wolf near it.
            bool mine = animal.Puppet || animal.TargetPeer == 0;
            if (!_toldStalk && mine && animal.Aware == WildlifeAnimal.Awareness.Alerted && (animal.Puppet || animal.Hunting))
            {
                _toldStalk = true;
                GameEvents.PublishHint(PredatorTuning.StalkHint);
            }
            if (!_toldWindUp && mine && animal.WindingUp && distance < PredatorTuning.WindUpDistance * 3.0f)
            {
                _toldWindUp = true;
                GameEvents.PublishHint(PredatorTuning.WindUpHint);
            }
        }
    }

    private void ForgetHints()
    {
        _toldStalk = false;
        _toldWindUp = false;
    }

    /// <summary>Guests' speed from their streamed feet (a jump of metres in a frame is a teleport, not speed).</summary>
    private void TrackGuests(float dt)
    {
        if (!GameEvents.SharedWorldHost)
        {
            if (_guests.Count > 0)
            {
                _guests.Clear();
            }
            return;
        }
        foreach (NetCarrier other in GameEvents.OtherCarriers)
        {
            Vector3 velocity = Vector3.Zero;
            if (_guests.TryGetValue(other.Peer, out var last) && dt > 0.0f)
            {
                Vector3 moved = other.Feet - last.Feet;
                velocity = moved.Length() > 8.0f ? Vector3.Zero : last.Velocity.Lerp(moved / dt, 0.3f);
            }
            _guests[other.Peer] = (other.Feet, velocity);
        }
    }

    /// <summary>Every animal within the scatter radius of a point (measured flat, as slopes are steep) bolts away from it.</summary>
    public int ScatterAt(Vector3 point)
    {
        return Table == null ? 0 : ScatterAt(point, Table.ScatterRadius);
    }

    /// <summary>Every animal within a radius of a point bolts away from it (a fall, a sword swing).</summary>
    public int ScatterAt(Vector3 point, float radius)
    {
        int scattered = 0;
        foreach (WildlifeAnimal animal in _animals)
        {
            Vector3 offset = animal.GlobalPosition - point;
            if (!animal.IsDead && new Vector2(offset.X, offset.Z).Length() <= radius)
            {
                animal.FleeFrom(point);
                scattered++;
            }
        }
        return scattered;
    }

    /// <summary>A loud noise at a point: animals within the radius startle by temperament (WildlifeAnimal.Startle).</summary>
    public int StartleAt(Vector3 point, float radius)
    {
        int startled = 0;
        foreach (WildlifeAnimal animal in _animals)
        {
            Vector3 offset = animal.GlobalPosition - point;
            if (!animal.IsDead && new Vector2(offset.X, offset.Z).Length() <= radius)
            {
                animal.Startle(point);
                startled++;
            }
        }
        return startled;
    }

    /// <summary>Removes every animal.</summary>
    public void Clear()
    {
        foreach (WildlifeAnimal animal in _animals)
        {
            animal.Herd?.Leave(animal);
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

    /// <summary>Places tried per spawn interval before giving up until the next one.</summary>
    private const int SpawnAttempts = 4;

    /// <summary>Tries one place for a group; false when the ground there suits no animal.</summary>
    private bool TrySpawnGroup(PhysicsDirectSpaceState3D space)
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
            return false;
        }
        WildlifeSpecies? species = PickSpecies(surface, slope);
        if (species == null)
        {
            return false;
        }
        if (species.Temperament == Temperament.Predator)
        {
            // A pack lives on its own ground, away from the carrier's station fire.
            if (NearFire(point))
            {
                return false;
            }
        }
        string spotSurface = surface;
        float spotSlope = slope;
        int size = _random.RandiRange(species.GroupSize.X, Mathf.Max(species.GroupSize.X, species.GroupSize.Y));
        size = Mathf.Min(size, Table.MaxAnimals - _animals.Count);
        WildlifeHerd? herd = size > 1 ? new WildlifeHerd() : null;
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
            WildlifeAnimal animal = Spawn(species, spot, spotSurface, spotSlope);
            herd?.Join(animal);
        }
        return true;
    }

    /// <summary>Predator packs alive now.</summary>
    public int PredatorPacks
    {
        get
        {
            HashSet<object> packs = new();
            foreach (WildlifeAnimal animal in _animals)
            {
                if (animal.IsPredator && !animal.IsDead)
                {
                    packs.Add((object?)animal.Herd ?? animal);
                }
            }
            return packs.Count;
        }
    }

    /// <summary>A species' chance here and now: predators only while switched on, one pack at a time, more often at night.</summary>
    public static float SpawnWeight(WildlifeSpecies species, int predatorPacks, float daylight)
    {
        if (species.Temperament != Temperament.Predator)
        {
            return species.Weight;
        }
        if (!PredatorTuning.Enabled || predatorPacks >= PredatorTuning.MaxPacks)
        {
            return 0.0f;
        }
        bool night = PredatorTuning.IsNight(daylight);
        return species.Weight * (night ? PredatorTuning.NightWeight : PredatorTuning.DayWeight);
    }

    private WildlifeSpecies? PickSpecies(string surface, float slope)
    {
        float total = 0.0f;
        int packs = PredatorPacks;
        float daylight = _clock?.Daylight ?? 1.0f;
        foreach (WildlifeSpecies species in Table!.Species)
        {
            if (species.LivesIn(Circle) && species.LivesOn(surface, slope))
            {
                total += SpawnWeight(species, packs, daylight);
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
            float weight = SpawnWeight(species, packs, daylight);
            if (weight <= 0.0f)
            {
                continue;
            }
            pick -= weight;
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
        _carrierFalling = snapshot.State == LocomotionState.Airborne && snapshot.Velocity.Y < -(Table?.FastSpeed ?? 4.5f);
        _crouching = IsCrouching(snapshot);
        _hasCarrier = true;
    }

    /// <summary>Crouching: braking, or standing still with the brake key held.</summary>
    public static bool IsCrouching(LocomotionSnapshot snapshot)
    {
        return snapshot.State == LocomotionState.Braking
            || (snapshot.State == LocomotionState.Idle && Input.IsActionPressed(CrouchAction));
    }

    private void OnLoadChanged(LoadSnapshot load)
    {
        _loadMass = load.Mass;
    }

    /// <summary>A hard landing is heard around: the harder, the farther.</summary>
    private void OnLanded(float downwardSpeed)
    {
        if (Table == null || !GameEvents.MaySimulateWorld || downwardSpeed < Table.HardLandingSpeed)
        {
            return;
        }
        StartleAt(_carrier, Mathf.Min(downwardSpeed * Table.LandingNoisePerSpeed, Table.ScatterRadius));
    }

    /// <summary>A sword swung nearby scatters every animal around, bold ones too.</summary>
    private void OnMeleeSwung(Vector3 feet, Vector3 facing, string kind)
    {
        if (Table == null || !GameEvents.MaySimulateWorld)
        {
            return;
        }
        ScatterAt(feet, Table.SwingScatterRadius);
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
