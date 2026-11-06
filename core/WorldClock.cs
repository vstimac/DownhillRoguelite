using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Autoload: the run's 24-hour clock. Starts at dawn on day 1 and runs at the speed the run's
/// difficulty sets (DifficultySettings.DayLengthMinutes). Survival systems read HoursPerSecond to
/// drain needs in in-game hours. A respawn starts a new run at dawn.
/// </summary>
public partial class WorldClock : Node
{
    public const float HoursPerDay = 24.0f;

    /// <summary>Hour of the day a run begins.</summary>
    public float StartHour { get; set; } = 6.0f;
    public int Day { get; private set; } = 1;
    /// <summary>Hours since midnight, 0 to 24.</summary>
    public float TimeOfDay { get; private set; }
    /// <summary>In-game hours since the run began.</summary>
    public double TotalHours { get; private set; }
    public bool Paused { get; set; }
    /// <summary>Speeds the clock up for tests and debugging; 1 in normal play.</summary>
    public float DebugTimeScale { get; set; } = 1.0f;

    /// <summary>In-game hours that pass per real second (0 while paused).</summary>
    public float HoursPerSecond
    {
        get
        {
            if (Paused)
            {
                return 0.0f;
            }
            float minutes = 36.0f;
            RunState? run = GetNodeOrNull<RunState>("/root/RunState");
            if (run != null)
            {
                minutes = run.DifficultySettings.DayLengthMinutes;
            }
            return HoursPerDay / (minutes * 60.0f) * DebugTimeScale;
        }
    }

    public override void _Ready()
    {
        ResetToDawn();
        GameEvents.RespawnRequested += ResetToDawn;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= ResetToDawn;
    }

    public void ResetToDawn()
    {
        Day = 1;
        TimeOfDay = StartHour;
        TotalHours = 0.0;
    }

    /// <summary>Jumps to an hour of the current day (debug and tests).</summary>
    public void SetTimeOfDay(float hour)
    {
        TimeOfDay = Mathf.PosMod(hour, HoursPerDay);
    }

    public override void _PhysicsProcess(double delta)
    {
        float hours = HoursPerSecond * (float)delta;
        TotalHours += hours;
        TimeOfDay += hours;
        while (TimeOfDay >= HoursPerDay)
        {
            TimeOfDay -= HoursPerDay;
            Day++;
        }
    }
}
