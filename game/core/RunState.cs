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
    public CharacterRun Character { get; } = new();
    public bool CharacterRunStarted { get; private set; }
    public event System.Action? CharacterChanged;
    public event System.Action<CharacterStats>? CharacterAbilityActivated;
    /// <summary>Names of the items this run started with (unlocked by the records).</summary>
    public System.Collections.Generic.List<string> StartingKit { get; } = new();
    /// <summary>Names of the boons chosen this run, in order.</summary>
    public System.Collections.Generic.List<string> Boons { get; } = new();
    /// <summary>This run's contracts and cargo score (reset with every run).</summary>
    public RunTelemetry Telemetry { get; } = new();

    /// <summary>Score per metre descended (INITIAL TUNING: a tenth of a point).</summary>
    [Export] public float ScorePerMetre { get; set; } = 0.1f;
    /// <summary>Score per station reached (INITIAL TUNING).</summary>
    [Export] public int ScorePerStation { get; set; } = 20;

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

    /// <summary>Achievements and their tallies, across runs (user://achievements.cfg).</summary>
    public AchievementBook Achievements
    {
        get
        {
            _achievements ??= new AchievementBook(DisplayServer.GetName() != "headless");
            return _achievements;
        }
    }

    /// <summary>Relics found, ever and this run (user://relics_seen.cfg).</summary>
    public LoreBook RelicsSeen
    {
        get
        {
            _relicsSeen ??= new LoreBook(DisplayServer.GetName() != "headless", "user://relics_seen.cfg");
            return _relicsSeen;
        }
    }

    /// <summary>Relics studied at an altar, ever and this run (user://relics_studied.cfg).</summary>
    public LoreBook RelicsStudied
    {
        get
        {
            _relicsStudied ??= new LoreBook(DisplayServer.GetName() != "headless", "user://relics_studied.cfg");
            return _relicsStudied;
        }
    }

    /// <summary>Found texts read, ever and this run (user://lore.cfg).</summary>
    public LoreBook Lore
    {
        get
        {
            _lore ??= new LoreBook(DisplayServer.GetName() != "headless");
            return _lore;
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
    private LoreBook? _lore;
    private AchievementBook? _achievements;
    private LoreBook? _relicsSeen;
    private LoreBook? _relicsStudied;
    private float _overTimer;

    public override void _Ready()
    {
        GameEvents.DepthChanged += OnDepthChanged;
        GameEvents.StationReached += OnStationReached;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.CarrierDied += OnCarrierDied;
        GameEvents.ContractAccepted += OnContractAccepted;
        GameEvents.ContractResolved += OnContractResolved;
        GameEvents.RelicDelivered += OnRelicDelivered;
        RunNumber = Records.Runs + 1;
        ConfigureCharacter(null, false);
        GameOptions.Changed += OnOptionsChanged;
    }

    /// <summary>Picking another character in Options during a run swaps to them on the spot (look and stats).</summary>
    private void OnOptionsChanged()
    {
        if (!CharacterRunStarted || DisplayServer.GetName() == "headless" || GameOptions.Character == Character.Id)
        {
            return;
        }
        ConfigureCharacter(GameOptions.Character, true);
    }

    private void ConfigureCharacter(string? requested, bool started)
    {
        string id = "default";
        if (DisplayServer.GetName() != "headless") { GameOptions.Load(); id = GameOptions.Character; }
        if (requested != null) { id = requested; }
        foreach (string arg in OS.GetCmdlineUserArgs())
        { if (arg.StartsWith("--character=", System.StringComparison.Ordinal)) { id = arg["--character=".Length..]; } }
        CarrierCharacter? selected = null;
        // Core headless regressions have a deterministic neutral Carrier baseline, independent of player saves/model availability.
        if (DisplayServer.GetName() == "headless" && id == "default")
        { selected = CarrierRoster.LoadCharacter("res://art/characters/carrier.tres"); }
        else { selected = CarrierRoster.Load()?.Pick(id); }
        Character.Select(selected);
        CharacterRunStarted = started;
        CharacterChanged?.Invoke();
    }

    public void BeginCharacterRun(string? id = null) { ConfigureCharacter(id, true); }

    /// <summary>The run's own numbers for a save (the rest of the world is saved by RunKeeper).</summary>
    public void CaptureInto(RunSave save)
    {
        save.Seed = Seed;
        save.RunNumber = RunNumber;
        save.Depth = Depth;
        save.Stage = Stage;
        save.Character = Character.Id;
        save.Boons = new System.Collections.Generic.List<string>(Boons);
        save.Modifiers.Clear();
        foreach (System.Reflection.PropertyInfo property in typeof(RunModifiers).GetProperties())
        {
            if (property.PropertyType == typeof(float) && property.CanRead && property.CanWrite)
            {
                save.Modifiers[property.Name] = (float)property.GetValue(Modifiers)!;
            }
        }
    }

    /// <summary>Continue: puts back a saved run's numbers, character and boons (after StartNewRun(save.Seed) rebuilt its mountain).</summary>
    public void RestoreFrom(RunSave save)
    {
        RunOver = false;
        _overTimer = 0.0f;
        RunNumber = System.Math.Max(save.RunNumber, 1);
        Stage = save.Stage;
        Depth = save.Depth;
        if (save.Character.Length > 0)
        {
            ConfigureCharacter(save.Character, true);
        }
        Boons.Clear();
        Boons.AddRange(save.Boons);
        foreach (System.Reflection.PropertyInfo property in typeof(RunModifiers).GetProperties())
        {
            if (property.PropertyType == typeof(float) && property.CanWrite && save.Modifiers.TryGetValue(property.Name, out float value) && float.IsFinite(value))
            {
                property.SetValue(Modifiers, value);
            }
        }
    }

    public bool TryUseCharacterAbility()
    {
        if (RunOver || GetTree().Paused || GameEvents.IsDowned || GameEvents.IsPacking || GameEvents.IsConversing || !Character.Activate()) { return false; }
        CharacterAbilityActivated?.Invoke(Character.Stats);
        // A stamina ability says how much it gave back itself (player/CharacterAbility).
        if (Character.Stats.RestoreStaminaFraction <= 0) { GameEvents.PublishHint(Character.Stats.AbilityName + " activated."); }
        return true;
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
        LastSummary = new RunSummary(RunNumber, Seed, Depth, Stage, day, Circles.NameOf(Circles.CircleAt(Depth)), cause, Records.BestDepth, record,
            Telemetry.ContractsDelivered, Telemetry.ContractsFailed, Telemetry.CargoScore, ScoreOf(Depth, Stage, Telemetry.CargoScore), Telemetry.RelicsDelivered,
            string.Join(", ", Boons));
        GameEvents.PublishRunEnded(LastSummary);
    }

    public override void _Process(double delta)
    {
        if (!RunOver)
        {
            Character.Tick((float)delta);
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

    /// <summary>
    /// A run's score: a point per ten metres, a little for every station, plus what delivered cargo
    /// earned. Never negative. Depth stays the headline; the score is the second line.
    /// </summary>
    public int ScoreOf(int depth, int stations, int cargoScore)
    {
        double depthScore = 0.0;
        if (depth > 0 && ScorePerMetre > 0.0f)
        {
            depthScore = System.Math.Floor(depth * (double)ScorePerMetre);
        }
        long stationScore = (long)System.Math.Max(stations, 0) * System.Math.Max(ScorePerStation, 0);
        double score = depthScore + stationScore + System.Math.Max(cargoScore, 0);
        return (int)System.Math.Min(score, int.MaxValue);
    }

    private void OnContractAccepted(ContractNote note)
    {
        Telemetry.RecordAccepted(note);
    }

    private void OnContractResolved(ContractNote note)
    {
        Telemetry.RecordResolved(note);
    }

    private void OnRelicDelivered(RelicNote note)
    {
        Telemetry.RecordRelic(note);
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
        ConfigureCharacter(null, true);
        Boons.Clear();
        Telemetry.Reset();
        Lore.BeginRun();
        RelicsSeen.BeginRun();
        RelicsStudied.BeginRun();
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
        GameEvents.ContractAccepted -= OnContractAccepted;
        GameEvents.ContractResolved -= OnContractResolved;
        GameEvents.RelicDelivered -= OnRelicDelivered;
        GameOptions.Changed -= OnOptionsChanged;
        _difficultySettings = null;
        _circles = null;
        Route = null;

        // Autoloads leave the tree last. Collect now so managed wrappers that still reference Godot
        // resources are finalized before the engine reports "resources still in use at exit".
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
