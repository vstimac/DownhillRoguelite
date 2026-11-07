using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Autoload holding state for the current run, and the run loop (M12): death is permanent. When
/// the carrier dies the run ends (RunEnded, with a summary that goes into the records); after
/// SummaryDelay a new run starts on a new mountain: a new seed (NewMountain, so terrain rebuilds),
/// then RespawnRequested so every system resets. Systems share run-wide data through this node
/// instead of referencing each other directly.
/// </summary>
public partial class RunState : Node
{
    /// <summary>Seed for all procedural generation in this run.</summary>
    public ulong Seed { get; set; }

    /// <summary>Deepest point reached this run, in metres below the start.</summary>
    public int Depth { get; set; }

    /// <summary>Highest station reached this run (0 before the first).</summary>
    public int Stage { get; set; }

    /// <summary>This run's number in the save (1 = first).</summary>
    public int RunNumber { get; private set; } = 1;
    /// <summary>Seconds between the carrier's death and the next run on a new mountain.</summary>
    public float SummaryDelay { get; set; } = 7.0f;
    /// <summary>False keeps the same mountain after death (tests that compare before and after).</summary>
    public bool NewMountainOnDeath { get; set; } = true;
    public bool RunOver { get; private set; }
    public RunSummary LastSummary { get; private set; }

    /// <summary>What this run's boons changed (reset with every run).</summary>
    public RunModifiers Modifiers { get; } = new();
    /// <summary>Names of the items this run started with (unlocked by the records).</summary>
    public System.Collections.Generic.List<string> StartingKit { get; } = new();
    /// <summary>Names of the boons chosen this run, in order.</summary>
    public System.Collections.Generic.List<string> Boons { get; } = new();

    /// <summary>The circle the carrier has descended into (0 = Limbo).</summary>
    public int Circle => Circles.CircleAt(Depth);

    /// <summary>Hunger and thirst drain from depth: rises with every circle descended.</summary>
    public float DepthNeedsFactor => 1.0f + DifficultySettings.NeedsDrainPerCircle * Circle;

    /// <summary>Night cold and weather chill from depth: rises with every circle descended.</summary>
    public float DepthColdFactor => 1.0f + DifficultySettings.ColdPerCircle * Circle;

    /// <summary>What carries over between runs (user://records.cfg).</summary>
    public RunRecords Records
    {
        get
        {
            // Headless runs are tests and tools: keep their runs out of the player's records.
            _records ??= new RunRecords(DisplayServer.GetName() != "headless");
            return _records;
        }
    }

    public Difficulty Difficulty
    {
        get => _difficulty;
        set
        {
            _difficulty = value;
            _difficultySettings = null;
        }
    }

    /// <summary>Settings for the run's difficulty, from core/difficulty/&lt;name&gt;.tres.</summary>
    public DifficultySettings DifficultySettings
    {
        get
        {
            _difficultySettings ??= ResourceLoader.Load($"res://core/difficulty/{_difficulty.ToString().ToLowerInvariant()}.tres") as DifficultySettings
                ?? new DifficultySettings();
            return _difficultySettings;
        }
    }

    /// <summary>How depth maps to circles, from core/circles.tres.</summary>
    public CircleTable Circles
    {
        get
        {
            _circles ??= ResourceLoader.Load("res://core/circles.tres") as CircleTable ?? new CircleTable();
            return _circles;
        }
    }

    /// <summary>The walkable route through the world, set by terrain; null until terrain exists.</summary>
    public IRouteQuery? Route { get; set; }

    private Difficulty _difficulty = Difficulty.Normal;
    private DifficultySettings? _difficultySettings;
    private CircleTable? _circles;
    private RunRecords? _records;
    private float _overTimer;

    public override void _Ready()
    {
        GameEvents.DepthChanged += OnDepthChanged;
        GameEvents.StationReached += OnStationReached;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.CarrierDied += OnCarrierDied;
        RunNumber = Records.Runs + 1;
    }

    /// <summary>A seed nobody chose: the clock's microseconds through SplitMix64.</summary>
    public static ulong RandomSeed()
    {
        return new SplitMix64((ulong)Time.GetTicksUsec() ^ (ulong)System.DateTime.UtcNow.Ticks).Next();
    }

    /// <summary>
    /// Ends whatever run is going and starts the next one: a new mountain from <paramref name="seed"/>
    /// (random when null), then a full reset of every system.
    /// </summary>
    public void StartNewRun(ulong? seed = null)
    {
        Seed = seed ?? RandomSeed();
        RunOver = false;
        _overTimer = 0.0f;
        GameEvents.PublishNewMountain(Seed);
        GameEvents.PublishRespawnRequest();
    }

    private void OnCarrierDied(string cause)
    {
        if (RunOver)
        {
            return;
        }
        RunOver = true;
        _overTimer = 0.0f;
        int day = 1;
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        if (clock != null)
        {
            day = clock.Day;
        }
        bool record = Records.Add(Depth, Stage);
        LastSummary = new RunSummary(RunNumber, Seed, Depth, Stage, day, Circles.NameOf(Circles.CircleAt(Depth)), cause, Records.BestDepth, record);
        GameEvents.PublishRunEnded(LastSummary);
    }

    public override void _Process(double delta)
    {
        if (!RunOver)
        {
            return;
        }
        _overTimer += (float)delta;
        if (_overTimer < SummaryDelay)
        {
            return;
        }
        RunNumber++;
        if (NewMountainOnDeath)
        {
            StartNewRun();
        }
        else
        {
            RunOver = false;
            GameEvents.PublishRespawnRequest();
        }
    }

    private void OnStationReached(int station)
    {
        Stage = Mathf.Max(Stage, station);
    }

    private void OnRespawnRequested()
    {
        Stage = 0;
        Depth = 0;
        Modifiers.Reset();
        Boons.Clear();
    }

    private void OnDepthChanged(float depth)
    {
        Depth = Mathf.FloorToInt(depth);
    }

    public override void _ExitTree()
    {
        GameEvents.DepthChanged -= OnDepthChanged;
        GameEvents.StationReached -= OnStationReached;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.CarrierDied -= OnCarrierDied;
        _difficultySettings = null;
        _circles = null;
        Route = null;

        // Autoloads leave the tree last. Collect now so managed wrappers that still reference Godot
        // resources are finalized before the engine reports "resources still in use at exit".
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
