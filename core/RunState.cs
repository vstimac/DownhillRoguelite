using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Autoload holding state for the current run. Systems share run-wide data through
/// this node instead of referencing each other directly. Placeholder until the run loop exists.
/// </summary>
public partial class RunState : Node
{
    /// <summary>Seed for all procedural generation in this run.</summary>
    public ulong Seed { get; set; }

    /// <summary>Deepest point reached this run, in metres below the start.</summary>
    public int Depth { get; set; }

    /// <summary>Highest station reached this run (0 before the first).</summary>
    public int Stage { get; set; }

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

    public override void _Ready()
    {
        GameEvents.DepthChanged += OnDepthChanged;
        GameEvents.StationReached += OnStationReached;
        GameEvents.RespawnRequested += OnRespawnRequested;
    }

    private void OnStationReached(int station)
    {
        Stage = Mathf.Max(Stage, station);
    }

    private void OnRespawnRequested()
    {
        Stage = 0;
        Depth = 0;
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
        _difficultySettings = null;
        _circles = null;
        Route = null;

        // Autoloads leave the tree last. Collect now so managed wrappers that still reference Godot
        // resources are finalized before the engine reports "resources still in use at exit".
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
