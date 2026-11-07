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
/// surfaces, and beyond the ninth circle only the cube pets; an animal bolts when the carrier comes
/// close and a fall scatters the animals nearby; all wildlife costs under 1 ms per frame.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WildlifeTests.tscn
/// </summary>
public partial class WildlifeTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public WildlifeSpawner? Spawner { get; set; }
    [Export] public float WalkDistance { get; set; } = 300.0f;
    [Export] public float StepDistance { get; set; } = 20.0f;
    /// <summary>A capsule's round foot rests a little above a slope (radius × (1/cos slope - 1)), so a few cm are allowed.</summary>
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
        await Place(PlayerBody, Streamer.SpawnPoint, 1.0f);

        await TestPopulation(PlayerBody, Spawner);
        TestGround(Spawner);
        TestHabitat(Spawner, 0);
        TestBudget(Spawner);
        await TestFlee(PlayerBody, Spawner);
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
        foreach (WildlifeAnimal animal in spawner.Animals)
        {
            if (animal.Species == null || animal.Species.HoverHeight > 0.0f || !animal.HasGround)
            {
                continue;
            }
            if (spawner.Ground(space, animal.GlobalPosition, out Vector3 ground, out string _, out float _))
            {
                worst = Mathf.Max(worst, Mathf.Abs(animal.GlobalPosition.Y - ground.Y));
                checkedCount++;
            }
        }
        Check("animals stand on the ground", checkedCount > 0 && worst <= MaxGroundError,
            $"{checkedCount} animals, worst {worst * 100.0f:0.0} cm off the ground (max {MaxGroundError * 100.0f:0} cm)");
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

    private async Task TestFlee(PlayerController player, WildlifeSpawner spawner)
    {
        spawner.Spawning = false;
        spawner.Clear();
        WildlifeSpecies cow = Find(spawner, "Cow");
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 at = player.GlobalPosition + new Vector3(0.0f, 0.0f, -30.0f);
        spawner.Ground(space, at, out Vector3 ground, out string _, out float _);
        WildlifeAnimal animal = spawner.Spawn(cow, ground);
        await WaitSeconds(1.0f);
        bool calm = animal.State != WildlifeAnimal.Mode.Flee;

        await Place(player, ground + new Vector3(0.0f, 0.0f, 5.0f), 0.2f);
        float near = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        await WaitSeconds(1.5f);
        float later = animal.GlobalPosition.DistanceTo(player.GlobalPosition);
        Check("an animal bolts when the carrier comes close", calm && animal.State == WildlifeAnimal.Mode.Flee && later > near + 2.0f,
            $"calm at 30 m {calm}, state {animal.State}, distance {near:0.0} → {later:0.0} m");
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
