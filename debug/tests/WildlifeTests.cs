using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M9: the population fills up and stays within its cap over a 300 m descent;
/// animals stand on the ground; only species of the current circle appear, mostly on their own
/// surfaces, and beyond the ninth circle only the cube pets; an animal stands its ground when the
/// carrier comes close, and pushed for long it throws the carrier into the air (small ones never do);
/// a fall scatters the animals nearby; all wildlife costs under 1 ms per frame.
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
        await TestStandGround(PlayerBody, Spawner);
        await TestShove(PlayerBody, Spawner);
        await TestScatter(PlayerBody, Spawner);
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
        WildlifeSpecies cow = Find(spawner, "Cow");
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 at = player.GlobalPosition + new Vector3(0.0f, 0.0f, -30.0f);
        spawner.Ground(space, at, out Vector3 ground, out string _, out float _);
        WildlifeAnimal animal = spawner.Spawn(cow, ground);
        await WaitSeconds(1.0f);

        await Place(player, ground + new Vector3(0.0f, 0.0f, 3.0f), 0.2f);
        Vector3 animalBefore = animal.GlobalPosition;
        float near = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        await WaitSeconds(1.5f);
        float later = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        float moved = animal.GlobalPosition.DistanceTo(animalBefore);
        Check("an animal stands its ground when the carrier comes close", animal.State != WildlifeAnimal.Mode.Flee && moved < 1.5f,
            $"state {animal.State}, animal moved {moved:0.0} m, distance {near:0.0} → {later:0.0} m");
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
    private WildlifeAnimal Standalone(WildlifeSpecies species, WildlifeTable table, Vector3 ground)
    {
        WildlifeAnimal animal = new();
        animal.Setup(species, table, 7);
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
        int fleeing = near.FindAll(a => a.State == WildlifeAnimal.Mode.Flee).Count;
        bool farCalm = far.State != WildlifeAnimal.Mode.Flee;
        GameEvents.PublishDowned(false);
        Check("a fall scatters the animals nearby, not the far ones", calmBefore && fleeing == near.Count && farCalm,
            $"calm before {calmBefore}, {fleeing}/{near.Count} within 15 m fled, one at 40 m calm {farCalm}");
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
