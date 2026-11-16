using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M9: the population fills up and stays within its cap over a 300 m descent;
/// animals stand on the ground and keep off ground steeper than they live on; only species of the
/// current circle appear, mostly on their own surfaces, and beyond the ninth circle only the cube
/// pets; a curious cow doesn't bolt from a carrier standing by, and pushed for long it throws the
/// carrier into the air (small ones never do); a fall, a sword swing or a hard landing scatters or
/// startles the animals nearby; all wildlife costs under 1 ms per frame. On a test field: an alert
/// beat then grazing, a herd bolting together and calming down, a curious approach, a wary walk-off
/// and a bold hold, stealth (crouching, from behind), hunting with loot, a neutral animal fighting
/// back, an aggressive one stinging, the stealth eye, and bolting along cliff edges, off steep banks
/// and round rocks.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WildlifeTests.tscn
/// </summary>
public partial class WildlifeTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public WildlifeSpawner? Spawner { get; set; }
    [Export] public float WalkDistance { get; set; } = 300.0f;
    [Export] public float StepDistance { get; set; } = 20.0f;
    /// <summary>Allowed gap from the capsule's round foot to its supporting plane, in m.</summary>
    [Export] public float MaxGroundError { get; set; } = 0.08f;
    [Export] public float BudgetMilliseconds { get; set; } = 1.0f;

    /// <summary>Circle index meaning "beyond the ninth" (see WildlifeSpecies.Circles).</summary>
    private const int WildlifeBeyondCircle = 9;

    private readonly List<string> _failures = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer == null || Spawner?.Table == null || _run == null)
        {
            GD.PushError("WildlifeTests: PlayerBody, Streamer, Spawner (with Table) and RunState are needed.");
            GetTree().Quit(1);
            return;
        }
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            const string prefix = "--wildlife-seed=";
            if (argument.StartsWith(prefix, System.StringComparison.Ordinal)
                && ulong.TryParse(argument[prefix.Length..], out ulong seed))
            {
                Spawner.PopulationSeed = seed;
            }
        }
        GD.Print($"Wildlife population seed: {Spawner.PopulationSeed}");
        await Place(PlayerBody, Streamer.SpawnPoint, 1.0f);

        await TestPopulation(PlayerBody, Spawner);
        TestGround(Spawner);
        TestHabitat(Spawner, 0);
        TestBudget(Spawner);
        await WaitSeconds(12.0f);
        TestSlopes(Spawner);
        TestBudget(Spawner);
        await TestStandGround(PlayerBody, Spawner);
        await TestShove(PlayerBody, Spawner);
        await TestScatter(PlayerBody, Spawner);
        await TestSwingAndLanding(PlayerBody, Spawner);

        Spawner.Spawning = false;
        Spawner.Clear();
        Vector3 home = PlayerBody.GlobalPosition;
        await BuildField(home);
        WildlifeTable table = Spawner.Table;
        WildlifeSpecies sheep = Find(Spawner, "Sheep");
        await TestAlertThenGraze(table, sheep);
        await TestHerdFleesAndCalms(table, sheep);
        await TestCurious(table, Find(Spawner, "Cow"));
        await TestWaryAndBold(table, Find(Spawner, "Horse"), Find(Spawner, "Pig"));
        await TestStealth(table, sheep);
        await TestHunting(table, sheep);
        await TestCooking();
        await TestNeutralFightsBack(table, Find(Spawner, "Pig"));
        await TestAggressive(table, Find(Spawner, "Wasp"));
        await TestStealthEye(table, sheep);
        await TestGroundAhead(table, sheep);
        _field?.QueueFree();
        await Place(PlayerBody, home, 1.0f);

        await TestBeyond(Spawner);

        if (_failures.Count == 0)
        {
            GD.Print("WILDLIFE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"WILDLIFE TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private async Task TestPopulation(PlayerController player, WildlifeSpawner spawner)
    {
        IRouteQuery route = _run!.Route!;
        int most = 0;
        float startZ = player.GlobalPosition.Z;
        for (float walked = 0.0f; walked <= WalkDistance; walked += StepDistance)
        {
            await Place(player, route.RoutePoint(startZ - walked), 0.5f);
            most = Mathf.Max(most, spawner.Animals.Count);
        }
        await WaitSeconds(3.0f);
        int cap = spawner.Table!.MaxAnimals;
        bool nearby = true;
        foreach (WildlifeAnimal animal in spawner.Animals)
        {
            if (animal.GlobalPosition.DistanceTo(player.GlobalPosition) > spawner.Table.DespawnDistance + 5.0f)
            {
                nearby = false;
            }
        }
        Check("population fills up and stays under its cap over a 300 m descent",
            most <= cap && spawner.Animals.Count >= cap / 2 && nearby,
            $"most {most}, now {spawner.Animals.Count} (cap {cap}), all within reach {nearby}");
    }

    private void TestGround(WildlifeSpawner spawner)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        float worst = 0.0f;
        int checkedCount = 0;
        int unsupported = 0;
        foreach (WildlifeAnimal animal in spawner.Animals)
        {
            if (animal.Species == null || animal.Species.HoverHeight > 0.0f || !animal.HasGround)
            {
                continue;
            }
            KinematicCollision3D support = new();
            bool supported = animal.TestMove(animal.GlobalTransform, Vector3.Down * 0.1f, support);
            if (!supported || support.GetNormal().Y <= 0.0f
                || animal.GetChild(0) is not CollisionShape3D shape || shape.Shape is not CapsuleShape3D capsule)
            {
                unsupported++;
                GD.Print($"      missing live support: {animal.Name} at {animal.GlobalPosition}, cached grounded {animal.HasGround}");
                continue;
            }
            // On slopes and edges the capsule touches away from its vertical centre ray.
            // Measure the bottom sphere against the actual contact plane instead.
            Vector3 centre = shape.GlobalTransform * new Vector3(0.0f, capsule.Radius - capsule.Height * 0.5f, 0.0f);
            float radius = capsule.Radius * shape.GlobalBasis.X.Length();
            Vector3 normal = support.GetNormal();
            float error = Mathf.Abs((centre - support.GetPosition()).Dot(normal) - radius);
            worst = Mathf.Max(worst, error);
            if (error > MaxGroundError)
            {
                spawner.Ground(space, animal.GlobalPosition, out Vector3 ground, out string _, out float _);
                GD.Print($"      ground mismatch: {animal.Name} at {animal.GlobalPosition}, ground {ground}, collider {support.GetCollider()}, normal {normal}, plane gap {error}");
            }
            checkedCount++;
        }
        Check("animals stand on the ground", checkedCount > 0 && unsupported == 0 && worst <= MaxGroundError,
            $"{checkedCount} animals, {unsupported} without support, worst contact error {worst * 100.0f:0.0} cm (max {MaxGroundError * 100.0f:0} cm)");
    }

    private void TestHabitat(WildlifeSpawner spawner, int circle)
    {
        int wrongCircle = 0;
        int onHabitat = 0;
        int total = 0;
        foreach (WildlifeAnimal animal in spawner.Animals)
        {
            if (animal.Species == null)
            {
                continue;
            }
            total++;
            if (!animal.Species.LivesIn(circle))
            {
                wrongCircle++;
            }
            if (animal.Species.LivesOn(animal.SpawnSurface, animal.SpawnSlope))
            {
                onHabitat++;
            }
        }
        Check($"only circle {circle + 1} species, placed on their own surfaces and slopes", total > 0 && wrongCircle == 0 && onHabitat == total,
            $"{total} animals, {wrongCircle} from other circles, {onHabitat} on their habitat");
    }

    private void TestBudget(WildlifeSpawner spawner)
    {
        Check("all wildlife costs under the frame budget", spawner.AverageMilliseconds < BudgetMilliseconds,
            $"{spawner.AverageMilliseconds:0.000} ms per frame with {spawner.Animals.Count} animals (budget {BudgetMilliseconds} ms)");
    }

    private async Task TestStandGround(PlayerController player, WildlifeSpawner spawner)
    {
        spawner.Spawning = false;
        spawner.Clear();
        // Back to the flat start (the station): on the slopes the carrier slides, and a sliding
        // carrier rightly scares animals. The checks after this one run here too.
        await Place(player, Streamer!.SpawnPoint, 1.0f);
        WildlifeSpecies cow = Find(spawner, "Cow");
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 at = player.GlobalPosition + new Vector3(0.0f, 0.0f, -3.0f);
        spawner.Ground(space, at, out Vector3 ground, out string _, out float _);
        WildlifeAnimal animal = spawner.Spawn(cow, ground);
        Vector3 animalBefore = animal.GlobalPosition;
        float near = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        float fastest = 0.0f;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float time = 0.0f; time < 2.5f; time += dt)
        {
            fastest = Mathf.Max(fastest, player.Velocity.Length());
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        float later = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        float moved = animal.GlobalPosition.DistanceTo(animalBefore);
        Check("a curious cow doesn't bolt from a carrier standing close by", animal.Flees == 0 && moved < 1.5f,
            $"state {animal.State}, animal moved {moved:0.0} m, distance {near:0.0} → {later:0.0} m, carrier at most {fastest:0.0} m/s");
    }

    /// <summary>
    /// The carrier leans into a cow (input toward it, touching): it gives way a little, puts up with
    /// it for PushPatience, turns round and throws the carrier about ShoveHeight up. A rat never shoves.
    /// </summary>
    private async Task TestShove(PlayerController player, WildlifeSpawner spawner)
    {
        spawner.Clear();
        WildlifeTable table = spawner.Table!;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 at = player.GlobalPosition + new Vector3(0.0f, 0.0f, -20.0f);
        spawner.Ground(space, at, out Vector3 ground, out string _, out float _);

        int launches = 0;
        float launchedAfter = -1.0f;
        float elapsed = 0.0f;
        void OnLaunch(Vector3 horizontal, float height, string cause)
        {
            launches++;
            if (launchedAfter < 0.0f)
            {
                launchedAfter = elapsed;
            }
        }
        GameEvents.LaunchRequested += OnLaunch;

        WildlifeAnimal cow = Standalone(Find(spawner, "Cow"), table, ground);
        await Place(player, ground + new Vector3(0.0f, 0.0f, cow.Radius + table.CarrierRadius + 0.1f), 0.3f);
        float startY = player.GlobalPosition.Y;
        float peak = startY;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float limit = table.PushPatience + table.ShoveWindUp + 3.0f;
        bool pushedBefore = false;
        for (; elapsed < limit; elapsed += dt)
        {
            if (launches == 0)
            {
                // The carrier keeps walking into the cow as it gives way (the test has no real input).
                Vector3 behind = player.GlobalPosition - cow.GlobalPosition;
                behind.Y = 0.0f;
                float touching = cow.Radius + table.CarrierRadius + 0.1f;
                if (behind.Length() > touching)
                {
                    player.Teleport(new Vector3(cow.GlobalPosition.X, player.GlobalPosition.Y, cow.GlobalPosition.Z) + behind.Normalized() * touching, 0.0f);
                }
                Vector3 toward = cow.GlobalPosition - player.GlobalPosition;
                toward.Y = 0.0f;
                cow.Tick(dt, player.GlobalPosition, toward.Normalized(), false, space);
                pushedBefore = pushedBefore || cow.PushTime > 0.5f;
            }
            peak = Mathf.Max(peak, player.GlobalPosition.Y);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        float rise = peak - startY;
        float expected = table.PushPatience + table.ShoveWindUp;
        Check("a cow pushed for long turns round and throws the carrier into the air",
            pushedBefore && launches == 1 && launchedAfter >= expected - 0.1f && launchedAfter <= expected + 0.6f && rise >= table.ShoveHeight * 0.8f,
            $"pushing counted {pushedBefore}, shoves {launches} after {launchedAfter:0.0} s (expected {expected:0.0} s), carrier rose {rise:0.0} m (aim {table.ShoveHeight:0.0} m)");
        cow.QueueFree();
        await WaitSeconds(2.0f);

        launches = 0;
        WildlifeAnimal rat = Standalone(Find(spawner, "Rat"), table, ground);
        await Place(player, ground + new Vector3(0.0f, 0.0f, rat.Radius + table.CarrierRadius + 0.1f), 0.3f);
        for (elapsed = 0.0f; elapsed < limit; elapsed += dt)
        {
            Vector3 behindRat = player.GlobalPosition - rat.GlobalPosition;
            behindRat.Y = 0.0f;
            float touchingRat = rat.Radius + table.CarrierRadius + 0.1f;
            if (behindRat.Length() > touchingRat)
            {
                player.Teleport(new Vector3(rat.GlobalPosition.X, player.GlobalPosition.Y, rat.GlobalPosition.Z) + behindRat.Normalized() * touchingRat, 0.0f);
            }
            Vector3 toward = rat.GlobalPosition - player.GlobalPosition;
            toward.Y = 0.0f;
            rat.Tick(dt, player.GlobalPosition, toward.Normalized(), false, space);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        Check("a rat is too small to shove: it just gets pushed", launches == 0 && rat.Shoves == 0 && rat.PushTime > 1.0f,
            $"shoves {launches}, pushed for {rat.PushTime:0.0} s");
        rat.QueueFree();
        GameEvents.LaunchRequested -= OnLaunch;
        await WaitSeconds(2.0f);
    }

    /// <summary>An animal outside the spawner, ticked by the test.</summary>
    private WildlifeAnimal Standalone(WildlifeSpecies species, WildlifeTable table, Vector3 ground, ulong seed = 7)
    {
        WildlifeAnimal animal = new();
        animal.Setup(species, table, seed);
        AddChild(animal);
        animal.GlobalPosition = ground;
        return animal;
    }

    private async Task TestScatter(PlayerController player, WildlifeSpawner spawner)
    {
        spawner.Clear();
        await WaitSeconds(0.2f);
        WildlifeSpecies sheep = Find(spawner, "Sheep");
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        List<WildlifeAnimal> near = new();
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4.0f;
            Vector3 at = player.GlobalPosition + new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * 15.0f;
            spawner.Ground(space, at, out Vector3 ground, out string _, out float _);
            near.Add(spawner.Spawn(sheep, ground));
        }
        Vector3 farAt = player.GlobalPosition + new Vector3(0.0f, 0.0f, -40.0f);
        spawner.Ground(space, farAt, out Vector3 farGround, out string _, out float _);
        WildlifeAnimal far = spawner.Spawn(sheep, farGround);
        await WaitSeconds(0.5f);
        bool calmBefore = near.TrueForAll(a => a.State != WildlifeAnimal.Mode.Flee);

        GameEvents.PublishDowned(true);
        await WaitSeconds(0.1f);
        // Bolted: a sheep that bolts into steep ground stops there, cornered, so count the bolts.
        int fleeing = near.FindAll(a => a.Flees > 0).Count;
        bool farCalm = far.Flees == 0;
        GameEvents.PublishDowned(false);
        Check("a fall scatters the animals nearby, not the far ones", calmBefore && fleeing == near.Count && farCalm,
            $"calm before {calmBefore}, {fleeing}/{near.Count} within 15 m bolted ({string.Join(", ", near.ConvertAll(a => $"{a.State}, cornered {a.Cornered}"))}), one at 40 m calm {farCalm}");
        await WaitSeconds(0.5f);
    }

    private async Task TestBeyond(WildlifeSpawner spawner)
    {
        spawner.Clear();
        int beyond = WildlifeBeyondCircle;
        spawner.CircleOverride = beyond;
        spawner.Spawning = true;
        await WaitSeconds(4.0f);
        TestHabitat(spawner, beyond);
        spawner.CircleOverride = -1;
    }

    // --- Behaviour on a test field: flat ground high above the terrain, with a cliff edge all round,
    // a 55-degree bank on its north side and a rock. Animals there are ticked by the test against a
    // carrier it moves itself, so every run sees the same thing.

    private const float FieldHalf = 100.0f;
    /// <summary>A co-op peer id no one has: blows aimed at it go out as network strikes the test counts.</summary>
    private const long StandInPeer = 990001;
    private StaticBody3D? _field;
    private Vector3 _fieldTop;

    private async Task BuildField(Vector3 near)
    {
        _fieldTop = near + new Vector3(0.0f, 400.0f, 0.0f);
        _field = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        AddChild(_field);
        _field.GlobalPosition = _fieldTop;
        AddBox(_field, new Vector3(0.0f, -0.5f, 0.0f), new Vector3(FieldHalf * 2.0f, 1.0f, FieldHalf * 2.0f), Basis.Identity);
        // The bank rises northward from z = -90.
        float angle = Mathf.DegToRad(55.0f);
        const float halfLength = 8.0f;
        AddBox(_field, new Vector3(0.0f, Mathf.Sin(angle) * halfLength, -90.0f - Mathf.Cos(angle) * halfLength),
            new Vector3(30.0f, 1.0f, halfLength * 2.0f), new Basis(Vector3.Right, angle));
        AddBox(_field, new Vector3(-60.0f, 1.5f, 60.0f), new Vector3(2.5f, 3.0f, 2.5f), Basis.Identity);
        await WaitSeconds(0.1f);
    }

    private static void AddBox(Node3D body, Vector3 position, Vector3 size, Basis basis)
    {
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis, position) });
    }

    /// <summary>A point on the field, x and z from its centre.</summary>
    private Vector3 Field(float x, float z)
    {
        return _fieldTop + new Vector3(x, 0.0f, z);
    }

    /// <summary>Ticks the animals for a while against a carrier the test describes (by time since the start).</summary>
    private async Task Live(IReadOnlyList<WildlifeAnimal> animals, float seconds, System.Func<float, CarrierSense> carrier, System.Action<float, CarrierSense>? watch = null)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (float time = 0.0f; time < seconds; time += dt)
        {
            CarrierSense sense = carrier(time);
            foreach (WildlifeAnimal animal in animals)
            {
                animal.Tick(dt, sense, space);
            }
            watch?.Invoke(time, sense);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>A carrier walking or running from a point at a velocity for some seconds, then standing.</summary>
    private static CarrierSense Moving(Vector3 start, Vector3 velocity, float until, float time, bool sprinting)
    {
        if (time >= until)
        {
            return CarrierSense.Still(start + velocity * until);
        }
        return new CarrierSense(start + velocity * time, velocity, velocity.Normalized(), sprinting, false, 0.0f);
    }

    private static void Free(IEnumerable<WildlifeAnimal> animals)
    {
        foreach (WildlifeAnimal animal in animals)
        {
            animal.Herd?.Leave(animal);
            animal.QueueFree();
        }
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }

    /// <summary>
    /// A sheep sees a carrier walking by at 20 m: it lifts its head and faces it (alert), then goes
    /// back to grazing; it doesn't stand staring. One 45 m off never notices.
    /// </summary>
    private async Task TestAlertThenGraze(WildlifeTable table, WildlifeSpecies sheep)
    {
        WildlifeAnimal near = Standalone(sheep, table, Field(0.0f, 0.0f), 11);
        WildlifeAnimal far = Standalone(sheep, table, Field(-25.0f, 0.0f), 12);
        Vector3 carrier = Field(20.0f, 0.0f);
        Face(near, Vector3.Right);
        await Live(new[] { near, far }, 0.5f, _ => CarrierSense.Still(carrier + Vector3.Right * 200.0f));
        Face(near, Vector3.Right);
        float firstAlert = -1.0f;
        float facing = 0.0f;
        float alertTime = 0.0f;
        bool calmAfter = false;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        await Live(new[] { near, far }, 20.0f, _ => new CarrierSense(carrier, new Vector3(0.0f, 0.0f, 1.3f), Vector3.Back, false, false, 0.0f),
            (time, sense) =>
            {
                if (near.State == WildlifeAnimal.Mode.Alert)
                {
                    alertTime += dt;
                    if (firstAlert < 0.0f)
                    {
                        firstAlert = time;
                    }
                    Vector3 to = sense.Position - near.GlobalPosition;
                    to.Y = 0.0f;
                    facing = Mathf.Max(facing, near.Facing.Dot(to.Normalized()));
                }
                else if (firstAlert >= 0.0f && near.IsCalm)
                {
                    calmAfter = true;
                }
            });
        Check("an animal that notices the carrier gives a short alert, then grazes again",
            firstAlert >= 0.0f && firstAlert < 3.0f && facing > 0.85f && calmAfter && alertTime < 20.0f * 0.35f && near.Flees == 0 && far.Alerts == 0,
            $"first alert after {firstAlert:0.0} s, faced the carrier {facing:0.00}, back to {(calmAfter ? "grazing" : "nothing")}, alert {alertTime:0.0} of 20 s ({near.Alerts} beats), fled {near.Flees}, one at 45 m alerted {far.Alerts}");
        Free(new[] { near, far });
    }

    /// <summary>
    /// A sprinter comes at a flock of five sheep: they bolt within a second of each other, run the
    /// same way (away from it) and stay together; once it stops they stop at a safe distance, look
    /// back and go back to grazing.
    /// </summary>
    private async Task TestHerdFleesAndCalms(WildlifeTable table, WildlifeSpecies sheep)
    {
        Vector3 centre = Field(-40.0f, -20.0f);
        List<WildlifeAnimal> flock = new();
        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = i == 0 ? Vector3.Zero : Vector3.Forward.Rotated(Vector3.Up, i * Mathf.Tau / 4.0f) * 2.5f;
            flock.Add(Standalone(sheep, table, centre + offset, 100 + (ulong)i));
        }
        WildlifeHerd herd = new();
        foreach (WildlifeAnimal animal in flock)
        {
            herd.Join(animal);
        }
        Vector3 start = centre + new Vector3(0.0f, 0.0f, 22.0f);
        Vector3 run = new(0.0f, 0.0f, -5.0f);
        await Live(flock, 0.5f, _ => CarrierSense.Still(start + Vector3.Back * 200.0f));
        List<Vector3> before = flock.ConvertAll(a => a.GlobalPosition);
        float[] fledAt = { -1, -1, -1, -1, -1 };
        float[] stoppedAt = { -1, -1, -1, -1, -1 };
        await Live(flock, 6.0f, time => Moving(start, run, 2.0f, time, true), (time, sense) =>
        {
            for (int i = 0; i < flock.Count; i++)
            {
                if (fledAt[i] < 0.0f && flock[i].State == WildlifeAnimal.Mode.Flee)
                {
                    fledAt[i] = time;
                }
            }
        });
        Vector3 stop = start + run * 2.0f;
        float earliest = 99.0f;
        float latest = -1.0f;
        float awayDot = 0.0f;
        bool allFarther = true;
        Vector3 centroid = Vector3.Zero;
        for (int i = 0; i < flock.Count; i++)
        {
            earliest = Mathf.Min(earliest, fledAt[i] < 0.0f ? 99.0f : fledAt[i]);
            latest = Mathf.Max(latest, fledAt[i] < 0.0f ? 99.0f : fledAt[i]);
            Vector3 moved = flock[i].GlobalPosition - before[i];
            moved.Y = 0.0f;
            awayDot += moved.Normalized().Dot(Vector3.Forward) / flock.Count;
            allFarther &= Flat(flock[i].GlobalPosition, stop) > Flat(before[i], stop) + 5.0f;
            centroid += flock[i].GlobalPosition / flock.Count;
        }
        float spread = 0.0f;
        foreach (WildlifeAnimal animal in flock)
        {
            spread = Mathf.Max(spread, Flat(animal.GlobalPosition, centroid));
        }
        Check("a herd bolts together from a sprinter, the same way, away from it, and stays together",
            latest < 90.0f && latest - earliest <= 1.0f && awayDot > 0.6f && allFarther && spread < 10.0f,
            $"bolted between {earliest:0.00} and {latest:0.00} s, heading away {awayDot:0.00}, all farther {allFarther}, widest from the middle {spread:0.0} m");

        float[] safeAt = new float[flock.Count];
        await Live(flock, 22.0f, _ => CarrierSense.Still(stop), (time, sense) =>
        {
            for (int i = 0; i < flock.Count; i++)
            {
                if (stoppedAt[i] < 0.0f && flock[i].State == WildlifeAnimal.Mode.LookBack)
                {
                    stoppedAt[i] = time;
                    safeAt[i] = Flat(flock[i].GlobalPosition, stop);
                }
            }
        });
        bool lookedBack = System.Array.TrueForAll(stoppedAt, t => t >= 0.0f);
        float nearest = 99.0f;
        foreach (float d in safeAt)
        {
            nearest = Mathf.Min(nearest, d);
        }
        bool grazing = flock.TrueForAll(a => a.IsCalm && a.Flees == 1);
        Check("after fleeing, animals stop at a safe distance, look back and graze again",
            lookedBack && nearest >= sheep.SafeDistance * 0.9f && grazing,
            $"all looked back {lookedBack}, nearest stop {nearest:0.0} m (safe {sheep.SafeDistance:0} m), calm and fled once {grazing} ({string.Join(", ", flock.ConvertAll(a => a.State.ToString()))})");
        Free(flock);
    }

    /// <summary>A cow walks up to a carrier standing still, stops short, sniffs, loses interest and wanders off.</summary>
    private async Task TestCurious(WildlifeTable table, WildlifeSpecies cow)
    {
        WildlifeAnimal animal = Standalone(cow, table, Field(40.0f, 30.0f), 21);
        Vector3 carrier = Field(49.0f, 30.0f);
        await Live(new[] { animal }, 0.5f, _ => CarrierSense.Still(carrier + Vector3.Right * 200.0f));
        Face(animal, Vector3.Right);
        float start = Flat(animal.GlobalPosition, carrier);
        float closest = start;
        float leftTo = 0.0f;
        bool sniffed = false;
        await Live(new[] { animal }, 45.0f, _ => CarrierSense.Still(carrier), (time, sense) =>
        {
            float distance = Flat(animal.GlobalPosition, carrier);
            closest = Mathf.Min(closest, distance);
            if (animal.Approaches > 0 && animal.State != WildlifeAnimal.Mode.Approach)
            {
                sniffed = true;
            }
            if (sniffed)
            {
                leftTo = Mathf.Max(leftTo, distance);
            }
        });
        Check("a curious animal comes to look at a carrier standing still, then loses interest and leaves",
            animal.Approaches == 1 && closest <= cow.ApproachStop + 2.0f && closest >= 1.8f && sniffed && leftTo >= closest + 2.5f && animal.Shoves == 0 && animal.Flees == 0,
            $"approached {animal.Approaches}x, from {start:0.0} m to {closest:0.0} m (stops at {cow.ApproachStop:0.0}), then off to {leftTo:0.0} m, now {animal.State}");
        Free(new[] { animal });
    }

    /// <summary>A wary horse walks off from a carrier walking at it, looking back, never running; a bold pig lets one walk by.</summary>
    private async Task TestWaryAndBold(WildlifeTable table, WildlifeSpecies horse, WildlifeSpecies pig)
    {
        WildlifeAnimal wary = Standalone(horse, table, Field(40.0f, -60.0f), 31);
        Vector3 start = Field(40.0f, -40.0f);
        Vector3 walk = new(0.0f, 0.0f, -1.3f);
        await Live(new[] { wary }, 0.5f, _ => CarrierSense.Still(start + Vector3.Back * 200.0f));
        Face(wary, Vector3.Back);
        bool retreated = false;
        bool glanced = false;
        float fastest = 0.0f;
        Vector3 last = wary.GlobalPosition;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        await Live(new[] { wary }, 30.0f, time => Moving(start, walk, 6.0f, time, false), (time, sense) =>
        {
            if (wary.State == WildlifeAnimal.Mode.Retreat)
            {
                retreated = true;
                fastest = Mathf.Max(fastest, Flat(wary.GlobalPosition, last) / dt);
                Vector3 to = sense.Position - wary.GlobalPosition;
                to.Y = 0.0f;
                glanced |= wary.Facing.Dot(to.Normalized()) > 0.7f;
            }
            last = wary.GlobalPosition;
        });
        float end = Flat(wary.GlobalPosition, start + walk * 6.0f);
        Check("a wary animal walks off from a walking carrier, looking back, without running",
            retreated && glanced && wary.Flees == 0 && fastest <= horse.WalkSpeed * 1.4f + 0.4f && wary.State is not (WildlifeAnimal.Mode.Retreat or WildlifeAnimal.Mode.Flee) && end >= horse.FleeDistance,
            $"walked off {retreated}, looked back {glanced}, fled {wary.Flees}, fastest {fastest:0.0} m/s (walk {horse.WalkSpeed:0.0}), now {wary.State} at {end:0.0} m");
        Free(new[] { wary });

        WildlifeAnimal bold = Standalone(pig, table, Field(-40.0f, 40.0f), 32);
        Vector3 pass = Field(-44.0f, 48.0f);
        await Live(new[] { bold }, 0.5f, _ => CarrierSense.Still(pass + Vector3.Back * 200.0f));
        Vector3 placed = bold.GlobalPosition;
        bool gave = false;
        await Live(new[] { bold }, 10.0f, time => Moving(pass, walk * 1.2f, 10.0f, time, false), (time, sense) =>
        {
            gave |= bold.State is WildlifeAnimal.Mode.Flee or WildlifeAnimal.Mode.Retreat;
        });
        Check("a bold animal holds its ground when a carrier walks by close", !gave && Flat(bold.GlobalPosition, placed) < 1.5f,
            $"gave way {gave}, moved {Flat(bold.GlobalPosition, placed):0.0} m, alerts {bold.Alerts}");
        Free(new[] { bold });
    }

    /// <summary>
    /// Bolting toward a cliff edge, a steep bank or a rock, a sheep turns along the edge, keeps off
    /// the bank and runs round the rock; a flock grazing along the edge never steps off it.
    /// </summary>
    private async Task TestGroundAhead(WildlifeTable table, WildlifeSpecies sheep)
    {
        float top = _fieldTop.Y;
        Vector3 sprint = new(5.0f, 0.0f, 0.0f);

        WildlifeAnimal edge = Standalone(sheep, table, Field(94.0f, 10.0f), 41);
        Vector3 edgeStart = edge.GlobalPosition;
        float lowest = top;
        await Live(new[] { edge }, 0.3f, _ => CarrierSense.Still(Field(-150.0f, 10.0f)));
        await Live(new[] { edge }, 8.0f, time => Moving(Field(80.0f, 10.0f), sprint, 2.0f, time, true), (time, sense) => lowest = Mathf.Min(lowest, edge.GlobalPosition.Y));
        float edgeRun = Flat(edge.GlobalPosition, edgeStart);

        WildlifeAnimal bank = Standalone(sheep, table, Field(0.0f, -86.0f), 42);
        Vector3 bankStart = bank.GlobalPosition;
        float highest = top;
        await Live(new[] { bank }, 0.3f, _ => CarrierSense.Still(Field(0.0f, 150.0f)));
        await Live(new[] { bank }, 8.0f, time => Moving(Field(0.0f, -70.0f), new Vector3(0.0f, 0.0f, -5.0f), 2.0f, time, true), (time, sense) => highest = Mathf.Max(highest, bank.GlobalPosition.Y));
        float bankRun = Flat(bank.GlobalPosition, bankStart);

        WildlifeAnimal rock = Standalone(sheep, table, Field(-60.0f, 64.0f), 43);
        Vector3 rockStart = rock.GlobalPosition;
        await Live(new[] { rock }, 0.3f, _ => CarrierSense.Still(Field(-60.0f, 250.0f)));
        await Live(new[] { rock }, 8.0f, time => Moving(Field(-60.0f, 78.0f), new Vector3(0.0f, 0.0f, -5.0f), 2.0f, time, true));
        float rockRun = Flat(rock.GlobalPosition, rockStart);
        bool pastRock = rock.GlobalPosition.Z < Field(0.0f, 57.0f).Z;

        WildlifeTable restless = (WildlifeTable)table.Duplicate();
        restless.WanderInterval = new Vector2(0.4f, 1.2f);
        restless.WalkChance = 0.8f;
        List<WildlifeAnimal> grazers = new();
        WildlifeHerd herd = new();
        for (int i = 0; i < 3; i++)
        {
            grazers.Add(Standalone(sheep, restless, Field(96.0f, -30.0f + i * 2.0f), 50 + (ulong)i));
        }
        grazers.ForEach(herd.Join);
        float lowestGrazer = top;
        float walked = 0.0f;
        List<Vector3> grazeStart = grazers.ConvertAll(a => a.GlobalPosition);
        await Live(grazers, 30.0f, _ => CarrierSense.Still(Field(-150.0f, -150.0f)), (time, sense) =>
        {
            foreach (WildlifeAnimal animal in grazers)
            {
                lowestGrazer = Mathf.Min(lowestGrazer, animal.GlobalPosition.Y);
            }
        });
        for (int i = 0; i < grazers.Count; i++)
        {
            walked = Mathf.Max(walked, Flat(grazers[i].GlobalPosition, grazeStart[i]));
        }

        Check("bolting animals keep to ground they can stand on: along a cliff edge, off a steep bank, round a rock",
            lowest > top - 0.5f && edgeRun > 5.0f && highest < top + 0.6f && bankRun > 5.0f && pastRock && rockRun > 8.0f,
            $"edge: lowest {lowest - top:0.00} m, ran {edgeRun:0.0} m; bank: highest {highest - top:0.00} m, ran {bankRun:0.0} m; rock: ran {rockRun:0.0} m, past it {pastRock}");
        Check("a flock grazing along a cliff edge never steps off it", lowestGrazer > top - 0.5f && walked > 2.0f,
            $"lowest {lowestGrazer - top:0.00} m, farthest walk {walked:0.0} m");
        Free(new[] { edge, bank, rock });
        Free(grazers);
    }

    /// <summary>A sword swing scatters every animal near, a bold pig too; a hard landing sends skittish ones off and makes a cow look up.</summary>
    private async Task TestSwingAndLanding(PlayerController player, WildlifeSpawner spawner)
    {
        spawner.Clear();
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        WildlifeAnimal Put(string name, float angle, float distance)
        {
            Vector3 at = player.GlobalPosition + Vector3.Forward.Rotated(Vector3.Up, angle) * distance;
            spawner.Ground(space, at, out Vector3 ground, out string _, out float _);
            return spawner.Spawn(Find(spawner, name), ground);
        }
        List<WildlifeAnimal> near = new() { Put("Sheep", 0.0f, 8.0f), Put("Pig", 2.0f, 7.0f), Put("Cow", 4.0f, 9.0f) };
        await WaitSeconds(0.5f);
        string before = string.Join(", ", near.ConvertAll(a => $"{a.Name} {a.State}"));
        List<int> flees = near.ConvertAll(a => a.Flees);
        GameEvents.PublishMeleeSwung(player.GlobalPosition, Vector3.Forward, "swing");
        int fled = 0;
        for (int i = 0; i < near.Count; i++)
        {
            fled += near[i].Flees > flees[i] ? 1 : 0;
        }
        Check("a sword swing scatters every animal near, bold ones too", fled == near.Count,
            $"{fled}/{near.Count} bolted at the swing (before: {before}; carrier moving at {player.Velocity.Length():0.0} m/s)");

        spawner.Clear();
        await WaitSeconds(0.2f);
        WildlifeAnimal sheep = Put("Sheep", 1.0f, 10.0f);
        WildlifeAnimal cow = Put("Cow", 3.0f, 10.0f);
        await WaitSeconds(0.5f);
        GameEvents.PublishLanded(spawner.Table!.HardLandingSpeed + 0.5f);
        bool downed = GameEvents.IsDowned;
        Check("a hard landing sends skittish animals off and makes others look up", !downed && sheep.State == WildlifeAnimal.Mode.Flee && cow.State == WildlifeAnimal.Mode.Alert,
            $"sheep {sheep.State}, cow {cow.State}, carrier downed {downed}");

        // Co-op: a guest swings 20 m off; on the host its animals scatter, the host's own carrier being far.
        spawner.Clear();
        await WaitSeconds(0.2f);
        Vector3 guest = player.GlobalPosition + Vector3.Back * 20.0f;
        spawner.Ground(space, guest, out Vector3 guestGround, out string _, out float _);
        WildlifeAnimal guestSheep = spawner.Spawn(Find(spawner, "Sheep"), guestGround + Vector3.Right * 6.0f);
        WildlifeAnimal guestPig = spawner.Spawn(Find(spawner, "Pig"), guestGround + Vector3.Left * 5.0f);
        await WaitSeconds(0.5f);
        int sheepFlees = guestSheep.Flees, pigFlees = guestPig.Flees;
        GameEvents.SetWorldAuthority(true, true);
        GameEvents.PublishSharedCarrierSwung(guestGround);
        GameEvents.SetWorldAuthority(false, false);
        Check("a co-op guest's sword swing scatters the host's animals near it", guestSheep.Flees > sheepFlees && guestPig.Flees > pigFlees,
            $"sheep bolted {guestSheep.Flees > sheepFlees}, pig bolted {guestPig.Flees > pigFlees}");
        spawner.Clear();
        await WaitSeconds(0.3f);
    }

    /// <summary>On the real slopes, after a while of life no animal stands on ground much steeper than it lives on.</summary>
    private void TestSlopes(WildlifeSpawner spawner)
    {
        int walkers = 0;
        int steep = 0;
        string worst = "";
        float worstOver = 0.0f;
        foreach (WildlifeAnimal animal in spawner.Animals)
        {
            if (animal.Species == null || animal.Species.HoverHeight > 0.0f || !animal.HasGround)
            {
                continue;
            }
            walkers++;
            float over = animal.GroundSlope - animal.Species.MaxSlope - spawner.Table!.SlopeTolerance;
            if (over > 5.0f)
            {
                steep++;
            }
            if (over > worstOver)
            {
                worstOver = over;
                worst = $"{animal.Name} on {animal.GroundSlope:0} degrees";
            }
        }
        Check("animals keep off ground steeper than they live on", walkers > 0 && steep <= Mathf.Max(1, walkers / 10),
            $"{steep} of {walkers} on ground over their slope{(worst.Length > 0 ? $", worst {worst}" : "")}");
    }

    /// <summary>Turns an animal to face a direction (the model faces -Z).</summary>
    private static void Face(WildlifeAnimal animal, Vector3 direction)
    {
        animal.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);
    }

    /// <summary>
    /// Stealth: a sprinter is noticed far off, a carrier crouching (the brake key) up from the front
    /// much closer, and one crouching up from behind gets within a few metres.
    /// </summary>
    private async Task TestStealth(WildlifeTable table, WildlifeSpecies sheep)
    {
        async Task<float> Approach(Vector3 at, Vector3 from, float speed, bool sprinting, bool crouching, ulong seed)
        {
            WildlifeAnimal animal = Standalone(sheep, table, at, seed);
            Face(animal, Vector3.Left);
            await Live(new[] { animal }, 0.3f, _ => CarrierSense.Still(at + Vector3.Right * 300.0f));
            Face(animal, Vector3.Left);
            Vector3 toward = (at - from) with { Y = 0.0f };
            float until = (toward.Length() - 1.0f) / speed;
            Vector3 velocity = toward.Normalized() * speed;
            float noticed = -1.0f;
            await Live(new[] { animal }, until + 1.0f, time =>
            {
                CarrierSense sense = Moving(from, velocity, until, time, sprinting);
                return sense with { Crouching = crouching };
            }, (time, sense) =>
            {
                if (noticed < 0.0f && animal.Aware == WildlifeAnimal.Awareness.Alerted)
                {
                    noticed = Flat(sense.Position, at);
                }
            });
            Free(new[] { animal });
            return noticed;
        }
        float sprint = await Approach(Field(-70.0f, -60.0f), Field(-110.0f, -60.0f), 6.0f, true, false, 61);
        float crouch = await Approach(Field(-70.0f, -30.0f), Field(-90.0f, -30.0f), 1.0f, false, true, 62);
        float behind = await Approach(Field(-70.0f, 0.0f), Field(-55.0f, 0.0f), 1.0f, false, true, 63);
        Check("a crouching carrier gets much closer before an animal is alerted than a sprinting one, and closest from behind",
            sprint >= 15.0f && crouch >= 0.0f && crouch < sprint - 5.0f && behind >= 0.0f && behind <= 6.0f,
            $"alerted at {sprint:0.0} m by a sprinter, {crouch:0.0} m by a crouching carrier in front, {behind:0.0} m from behind");
    }

    /// <summary>A sheep killed with the sword drops a hide through KillLoot, lies dead (shown down to guests) and can't be hit; its flock-mate bolts.</summary>
    private async Task TestHunting(WildlifeTable table, WildlifeSpecies sheep)
    {
        WildlifeAnimal prey = Standalone(sheep, table, Field(60.0f, 60.0f), 71);
        WildlifeAnimal mate = Standalone(sheep, table, Field(63.0f, 60.0f), 72);
        WildlifeHerd herd = new();
        herd.Join(prey);
        herd.Join(mate);
        Vector3 carrier = Field(58.5f, 60.0f);
        await Live(new[] { prey, mate }, 0.3f, _ => CarrierSense.Still(carrier + Vector3.Back * 300.0f));
        int lootBefore = CountLoot("leather_patch");
        int meatBefore = CountLoot("raw_meat");
        int blows = 0;
        while (!prey.IsDead && blows < 10)
        {
            prey.TakeHit(26.0f, Vector3.Right * 2.0f, "sword");
            blows++;
        }
        await Live(new[] { prey, mate }, 1.0f, _ => CarrierSense.Still(carrier));
        int loot = CountLoot("leather_patch") - lootBefore;
        int meat = CountLoot("raw_meat") - meatBefore;
        Check("an animal can be hunted: killed by the sword it drops a hide and raw meat and lies dead; its herd bolts",
            prey.IsDead && prey.NetDown && !prey.CanBeHit && loot >= 1 && meat >= 1 && blows <= 3 && mate.State == WildlifeAnimal.Mode.Flee && mate.Herd == herd && prey.Herd == null,
            $"dead {prey.IsDead} after {blows} blows, shown down {prey.NetDown}, can be hit {prey.CanBeHit}, {loot} hide and {meat} raw meat dropped, mate {mate.State}");
        Free(new[] { prey, mate });
        foreach (Node node in GetTree().CurrentScene.GetChildren())
        {
            if (node.Name.ToString().StartsWith("Loot_", System.StringComparison.Ordinal))
            {
                node.QueueFree();
            }
        }
    }

    private int CountLoot(string itemId)
    {
        int count = 0;
        foreach (Node node in GetTree().CurrentScene.GetChildren())
        {
            if (node.Name.ToString().StartsWith("Loot_" + itemId, System.StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Raw meat is cooked at a station's fire (in its shelter) into cooked meat, which fills much more;
    /// away from a fire it stays raw (it can still be eaten, for less).
    /// </summary>
    private async Task TestCooking()
    {
        // The test carrier has no pack of its own: a pack for this check only.
        CarrierInventory inventory = new()
        {
            Name = "CookingPack",
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = System.Array.Empty<string>(),
            AutoPackOnStart = false,
        };
        AddChild(inventory);
        await WaitSeconds(0.1f);
        ItemDefinition? raw = ItemText.Find("raw_meat");
        ItemDefinition? cooked = ItemText.Find("cooked_meat");
        if (inventory.Grid == null || raw == null || cooked == null)
        {
            Check("raw meat cooks at a station's fire", false, $"pack {inventory.Grid != null}, raw {raw != null}, cooked {cooked != null}");
            inventory.QueueFree();
            return;
        }
        int Count(string id) => inventory.AllItems.Count(item => item.ItemId == id);
        int rawBefore = Count("raw_meat");
        int cookedBefore = Count("cooked_meat");
        bool added = inventory.TryAdd(raw, -1.0f, false);
        bool wasSheltered = GameEvents.InShelter;
        GameEvents.PublishShelter(false, 0);
        bool cookedOutside = inventory.Cook("raw_meat");
        GameEvents.PublishShelter(true, 0);
        bool cookedAtFire = inventory.Cook("raw_meat");
        GameEvents.PublishShelter(wasSheltered, 0);
        bool swapped = Count("raw_meat") == rawBefore && Count("cooked_meat") == cookedBefore + 1;
        Check("raw meat cooks at a station's fire (not away from one), and cooked it fills much more",
            added && !cookedOutside && cookedAtFire && swapped && cooked.Nutrition >= raw.Nutrition * 3.0f
                && raw.Category == ItemCategory.Food && cooked.MassKg <= raw.MassKg,
            $"packed {added}, cooked away from a fire {cookedOutside}, at a fire {cookedAtFire}, swapped {swapped}; food +{raw.Nutrition:0} raw, +{cooked.Nutrition:0} cooked");
        inventory.QueueFree();
        await WaitSeconds(0.1f);
    }

    /// <summary>A neutral pig struck by the sword turns and fights with its herd-mate, shoves the carrier and calms down later.</summary>
    private async Task TestNeutralFightsBack(WildlifeTable table, WildlifeSpecies pig)
    {
        WildlifeAnimal struck = Standalone(pig, table, Field(60.0f, -30.0f), 81);
        WildlifeAnimal mate = Standalone(pig, table, Field(62.0f, -32.0f), 82);
        WildlifeHerd herd = new();
        herd.Join(struck);
        herd.Join(mate);
        // Their shoves go to a stand-in co-op peer (a network strike), not the test's own carrier.
        struck.TargetPeer = StandInPeer;
        mate.TargetPeer = StandInPeer;
        Vector3 carrier = Field(54.0f, -30.0f);
        await Live(new[] { struck, mate }, 0.5f, _ => CarrierSense.Still(carrier));
        bool passive = struck.State != WildlifeAnimal.Mode.Fight && mate.State != WildlifeAnimal.Mode.Fight;
        int launches = 0;
        void OnLaunch(NetStrike strike)
        {
            if (strike.Peer == StandInPeer && strike.StaggerHeight > 0.0f)
            {
                launches++;
            }
        }
        GameEvents.NetCarrierStruck += OnLaunch;
        struck.TakeHit(20.0f, Vector3.Right * 2.0f, "sword");
        bool fought = struck.State == WildlifeAnimal.Mode.Fight && mate.State == WildlifeAnimal.Mode.Fight;
        bool calmed = false;
        await Live(new[] { struck, mate }, 40.0f, _ => CarrierSense.Still(carrier), (time, sense) =>
        {
            calmed |= time > 5.0f && struck.IsCalm && mate.IsCalm;
        });
        GameEvents.NetCarrierStruck -= OnLaunch;
        Check("a neutral animal is passive until struck, then fights back with its herd and calms down later",
            passive && fought && launches >= 1 && calmed && !struck.IsDead,
            $"passive before {passive}, both fought {fought}, shoved the carrier {launches}x, calm again {calmed}, health {struck.Health:0}");
        Free(new[] { struck, mate });
    }

    /// <summary>
    /// An aggressive wasp that notices a carrier near its nest comes at it and stings, about once every
    /// 2.5 s; it gives up after a short chase (8 s, or 15 m from its nest) and flies back.
    /// </summary>
    private async Task TestAggressive(WildlifeTable table, WildlifeSpecies wasp)
    {
        int stings = 0;
        void OnDamage(float amount, string cause)
        {
            if (cause == wasp.DisplayName)
            {
                stings++;
            }
        }
        GameEvents.DamageRequested += OnDamage;
        WildlifeAnimal animal = Standalone(wasp, table, Field(30.0f, 80.0f) + Vector3.Up * wasp.HoverHeight, 91);
        Vector3 carrier = Field(25.0f, 80.0f);
        Face(animal, Vector3.Left);
        await Live(new[] { animal }, 10.0f, _ => new CarrierSense(carrier, new Vector3(0.0f, 0.0f, 1.3f), Vector3.Zero, false, false, 0.0f));
        int standing = stings;
        int bites = animal.Bites;
        int gaveUp = animal.GiveUps;
        Free(new[] { animal });

        // Running off: it follows, but not past its leash, then heads back to its nest.
        Vector3 nest = Field(30.0f, 50.0f) + Vector3.Up * wasp.HoverHeight;
        WildlifeAnimal chaser = Standalone(wasp, table, nest, 92);
        Face(chaser, Vector3.Left);
        Vector3 start = Field(26.0f, 50.0f);
        Vector3 run = Vector3.Left * 3.0f;
        float farthest = 0.0f;
        float end = 0.0f;
        await Live(new[] { chaser }, 18.0f, time => time < 12.0f
                ? new CarrierSense(start + run * time, run, Vector3.Left, false, false, 0.0f)
                : CarrierSense.Still(start + run * 12.0f),
            (time, sense) =>
            {
                end = Flat(chaser.GlobalPosition, nest);
                farthest = Mathf.Max(farthest, end);
            });
        GameEvents.DamageRequested -= OnDamage;
        PlayerBody?.Health?.Refill();
        Check("an aggressive animal stings a carrier near its nest about every 2.5 s, and gives up a chase after 8 s",
            bites >= 2 && bites <= 4 && standing == bites && gaveUp >= 1,
            $"stung {bites}x in 10 s ({standing} damage requests, {bites * wasp.AttackDamage:0} hp), gave up {gaveUp}x");
        Check("a chasing wasp turns back at its leash and flies home",
            chaser.GiveUps >= 1 && farthest >= 8.0f && farthest <= wasp.ChaseLeash + 1.5f && end < farthest - 4.0f,
            $"stung the runner {chaser.Bites}x, went at most {farthest:0.0} m from its nest (leash {wasp.ChaseLeash:0}), {end:0.0} m at the end");
        Free(new[] { chaser });
    }

    /// <summary>
    /// The stealth eye's reading: the strongest awareness near the carrier; a guest reads it from a
    /// puppet's streamed clip (frozen idle suspicious, running alerted, down unaware).
    /// </summary>
    private async Task TestStealthEye(WildlifeTable table, WildlifeSpecies sheep)
    {
        WildlifeAnimal calm = Standalone(sheep, table, Field(-20.0f, 40.0f), 95);
        WildlifeAnimal puppet = new() { Puppet = true };
        AddChild(puppet);
        puppet.Setup(sheep, new WildlifeTable(), 1);
        puppet.GlobalPosition = Field(-16.0f, 40.0f);
        await WaitSeconds(0.1f);
        Vector3 carrier = Field(-18.0f, 30.0f);
        List<Node> near = new() { calm, puppet };
        WildlifeAnimal.Awareness quiet = StealthEye.Strongest(near, carrier, 35.0f);
        puppet.NetShow(sheep.IdleClip, 0.0f, 0.08f, 1.0f, false);
        WildlifeAnimal.Awareness frozen = StealthEye.Strongest(near, carrier, 35.0f);
        puppet.NetShow(sheep.FleeClip, 0.0f, sheep.FleeClipSpeed, 1.0f, false);
        WildlifeAnimal.Awareness running = StealthEye.Strongest(near, carrier, 35.0f);
        WildlifeAnimal.Awareness farAway = StealthEye.Strongest(near, carrier + Vector3.Right * 100.0f, 35.0f);
        puppet.NetShow(sheep.IdleClip, 0.0f, 0.0f, 0.0f, true);
        WildlifeAnimal.Awareness down = StealthEye.Strongest(near, carrier, 35.0f);
        Check("the stealth eye reads unaware, suspicious and alerted, on a guest too",
            quiet == WildlifeAnimal.Awareness.Unaware && frozen == WildlifeAnimal.Awareness.Suspicious && running == WildlifeAnimal.Awareness.Alerted
                && farAway == WildlifeAnimal.Awareness.Unaware && down == WildlifeAnimal.Awareness.Unaware,
            $"grazing {quiet}, guest's frozen {frozen}, guest's running {running}, out of range {farAway}, dead {down}");
        Free(new[] { calm, puppet });
    }

    private static WildlifeSpecies Find(WildlifeSpawner spawner, string name)
    {
        foreach (WildlifeSpecies species in spawner.Table!.Species)
        {
            if (species.DisplayName == name)
            {
                return species;
            }
        }
        return spawner.Table.Species[0];
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(settle);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
