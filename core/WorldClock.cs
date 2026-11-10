using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Autoload: the run's 24-hour clock. Starts at dawn on day 1 and runs at the speed the run's
/// difficulty sets (DifficultySettings.DayLengthMinutes). Survival systems read HoursPerSecond to
/// drain needs in in-game hours; lighting reads Daylight; stations call SleepUntilMorning.
/// A respawn starts a new run at dawn.
/// </summary>
public partial class WorldClock : Node
{
    private static readonly NodePath RunStatePath = new("/root/RunState");
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

    public float SunriseHour { get; set; } = 6.0f;
    public float SunsetHour { get; set; } = 20.0f;
    /// <summary>Length of dawn and dusk, in hours, centred on sunrise and sunset.</summary>
    public float TwilightHours { get; set; } = 1.0f;
    /// <summary>Sleeping is allowed from this hour (and through the night).</summary>
    public float SleepFromHour { get; set; } = 16.0f;

    /// <summary>Between sunset and sunrise.</summary>
    public bool IsNight => TimeOfDay >= SunsetHour || TimeOfDay < SunriseHour;

    /// <summary>1 in full daylight, 0 at night, easing through dawn and dusk.</summary>
    public float Daylight => DaylightAt(TimeOfDay);

    public bool CanSleepNow => IsNight || TimeOfDay >= SleepFromHour;

    public float DaylightAt(float hour)
    {
        float half = TwilightHours * 0.5f;
        float dawn = Mathf.SmoothStep(SunriseHour - half, SunriseHour + half, hour);
        float dusk = 1.0f - Mathf.SmoothStep(SunsetHour - half, SunsetHour + half, hour);
        return Mathf.Clamp(Mathf.Min(dawn, dusk), 0.0f, 1.0f);
    }

    /// <summary>Hours of daylight left before sunset (0 at night).</summary>
    public float HoursUntilSunset
    {
        get
        {
            if (IsNight)
            {
                return 0.0f;
            }
            return SunsetHour - TimeOfDay;
        }
    }

    /// <summary>
    /// Sleeps through to the next sunrise if it's late enough: advances the clock and raises
    /// GameEvents.Slept with the hours slept. Returns the hours slept, or 0 if it's too early.
    /// </summary>
    public float SleepUntilMorning()
    {
        if (!CanSleepNow)
        {
            return 0.0f;
        }
        float hours = Mathf.PosMod(SunriseHour - TimeOfDay, HoursPerDay);
        AdvanceHours(hours);
        GameEvents.PublishSlept(hours);
        return hours;
    }

    /// <summary>Moves the clock forward (sleep and debug).</summary>
    public void AdvanceHours(float hours)
    {
        TotalHours += hours;
        TimeOfDay += hours;
        while (TimeOfDay >= HoursPerDay)
        {
            TimeOfDay -= HoursPerDay;
            Day++;
        }
    }

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
            RunState? run = GetNodeOrNull<RunState>(RunStatePath);
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

    /// <summary>Continue: back to a saved day and hour.</summary>
    public void Restore(int day, float hour)
    {
        Day = System.Math.Max(day, 1);
        TimeOfDay = Mathf.PosMod(hour, HoursPerDay);
        TotalHours = (Day - 1) * HoursPerDay + TimeOfDay - StartHour;
    }

    /// <summary>Jumps to an hour of the current day (debug and tests).</summary>
    public void SetTimeOfDay(float hour)
    {
        TimeOfDay = Mathf.PosMod(hour, HoursPerDay);
    }

    public override void _PhysicsProcess(double delta)
    {
        AdvanceHours(HoursPerSecond * (float)delta);
    }
}
