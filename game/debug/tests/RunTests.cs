using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M12's run loop: the run starts on the summit, the highest ground around,
/// with a cliff behind; going behind the summit doesn't count as descent and falling into the
/// abyss kills; death ends the run with a summary that goes into the records; then a new run
/// starts on a new mountain (new seed, new terrain, carrier on the new summit, empty pack); and
/// the same seed always builds the same mountain.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RunTests.tscn
/// </summary>
public partial class RunTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public float SummitSearchRadius { get; set; } = 150.0f;

    private readonly System.Collections.Generic.List<string> _failures = new();
    private RunState? _run;
    private int _ended;
    private RunSummary _summary;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer?.Generator == null || _run == null || inventory == null)
        {
            GD.PushError("RunTests: PlayerBody (with Inventory), Streamer and RunState are needed.");
            GetTree().Quit(1);
            return;
        }
        _run.SummaryDelay = 1.5f;
        GameEvents.RunEnded += OnRunEnded;
        await Place(PlayerBody, Streamer.SpawnPoint, 1.0f);

        TestSummit(Streamer.Generator);
        TestSameSeed(Streamer.Generator);
        await TestBehindSummit(PlayerBody, Streamer);
        await TestDeathAndNewMountain(PlayerBody, Streamer, inventory);

        GameEvents.RunEnded -= OnRunEnded;
        if (_failures.Count == 0)
        {
            GD.Print("RUN TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"RUN TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnRunEnded(RunSummary summary)
    {
        _ended++;
        _summary = summary;
    }

    private void TestSummit(TerrainGenerator generator)
    {
        Vector3 spawn = Streamer!.SpawnPoint;
        float highest = float.MinValue;
        for (float x = -SummitSearchRadius; x <= SummitSearchRadius; x += 3.0f)
        {
            for (float z = -SummitSearchRadius; z <= SummitSearchRadius; z += 3.0f)
            {
                highest = Mathf.Max(highest, generator.Height(spawn.X + x, spawn.Z + z));
            }
        }
        float back = generator.Settings.SummitBack;
        float cliffTop = generator.Height(spawn.X, spawn.Z + back + 1.0f);
        float cliffBelow = generator.Height(spawn.X, spawn.Z + back + 21.0f);
        float drop = Mathf.RadToDeg(Mathf.Atan2(cliffTop - cliffBelow, 20.0f));
        Check("the run starts on the summit, with a cliff behind",
            highest <= generator.SummitHeight + 0.01f && spawn.Y >= generator.SummitHeight - 1.0f && drop > 45.0f,
            $"highest ground within {SummitSearchRadius} m {highest - spawn.Y:+0.00;-0.00} m vs the start (summit {generator.SummitHeight - spawn.Y:+0.0} m), cliff {drop:0}°");
    }

    private void TestSameSeed(TerrainGenerator generator)
    {
        TerrainGenerator again = new(generator.Settings, generator.Seed);
        TerrainGenerator other = new(generator.Settings, generator.Seed + 1);
        bool same = true;
        bool differs = false;
        for (float z = 0.0f; z > -2000.0f; z -= 97.0f)
        {
            float x = generator.SpineX(z) + 23.0f;
            if (!Mathf.IsEqualApprox(generator.Height(x, z), again.Height(x, z)))
            {
                same = false;
            }
            if (Mathf.Abs(generator.Height(x, z) - other.Height(x, z)) > 0.5f)
            {
                differs = true;
            }
        }
        Check("the same seed builds the same mountain, another seed another", same && differs, $"same {same}, other differs {differs}");
    }

    private async Task TestBehindSummit(PlayerController player, TerrainStreamer streamer)
    {
        TerrainGenerator generator = streamer.Generator!;
        Vector3 spawn = streamer.SpawnPoint;
        float depthBefore = streamer.Depth;
        // On the cliff below the summit, but not yet in the abyss: no descent counted.
        Vector3 cliff = new(spawn.X, 0.0f, spawn.Z + generator.Settings.SummitBack + 8.0f);
        cliff.Y = generator.Height(cliff.X, cliff.Z);
        await Place(player, cliff, 0.2f);
        bool noDepth = streamer.Depth <= depthBefore + 0.5f;

        _ended = 0;
        Vector3 abyss = new(spawn.X, 0.0f, spawn.Z + generator.Settings.SummitBack + 40.0f);
        abyss.Y = generator.Height(abyss.X, abyss.Z);
        await Place(player, abyss, 0.5f);
        Check("going behind the summit isn't descent, and the abyss kills",
            noDepth && player.Health!.Dead && _ended == 1 && _summary.Cause == "the abyss",
            $"depth {depthBefore:0.0} → {streamer.Depth:0.0} on the cliff, dead {player.Health!.Dead}, run ended {_ended}, cause \"{_summary.Cause}\"");
        // Let this death's new run happen before the next check.
        await WaitSeconds(_run!.SummaryDelay + 1.0f);
    }

    private async Task TestDeathAndNewMountain(PlayerController player, TerrainStreamer streamer, CarrierInventory inventory)
    {
        ulong firstSeed = _run!.Seed;
        int runs = _run.Records.Runs;
        int runNumber = _run.RunNumber;
        await Place(player, streamer.SpawnPoint, 0.5f);
        IRouteQuery route = _run.Route!;
        await Place(player, route.RoutePoint(streamer.SpawnPoint.Z - 120.0f), 1.0f);
        int depth = _run.Depth;
        inventory.TryAdd(ResourceLoader.Load<ItemDefinition>("res://inventory/items/ration.tres"));

        _ended = 0;
        player.Health!.TakeDamage(player.Health.Max * 2.0f, "a test");
        await WaitSeconds(0.2f);
        bool summary = _ended == 1 && _summary.Depth == depth && _summary.Cause == "a test" && _summary.Seed == firstSeed
            && _run.Records.Runs == runs + 1 && _summary.BestDepth >= depth;
        Check("death ends the run with a summary that goes into the records", summary,
            $"ended {_ended}, run {_summary.RunNumber}, {_summary.Depth} m (was {depth} m), cause \"{_summary.Cause}\", records {runs} → {_run.Records.Runs} runs, best {_summary.BestDepth} m");

        await WaitSeconds(_run.SummaryDelay + 1.0f);
        Vector3 spawn = streamer.SpawnPoint;
        float fromSpawn = new Vector2(player.GlobalPosition.X - spawn.X, player.GlobalPosition.Z - spawn.Z).Length();
        bool fresh = _run.Seed != firstSeed && streamer.Generator!.Seed == _run.Seed && _run.RunNumber == runNumber + 1
            && !player.Health.Dead && fromSpawn < 2.0f && inventory.AllItems.Count == 0 && _run.Depth == 0;
        Check("then a new run starts on a new mountain, at its summit, with nothing", fresh,
            $"seed {firstSeed} → {_run.Seed}, terrain seed {streamer.Generator!.Seed}, run {runNumber} → {_run.RunNumber}, alive {!player.Health.Dead}, {fromSpawn:0.0} m from the new summit, items {inventory.AllItems.Count}, depth {_run.Depth}");
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
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
