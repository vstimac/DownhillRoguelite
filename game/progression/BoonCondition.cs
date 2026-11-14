namespace DownhillRoguelite.Progression;

/// <summary>When a boon works: always (most of them), or only in one situation (Beta 3.3).</summary>
public enum BoonCondition
{
    Always,
    /// <summary>Between sunset and sunrise on the world clock.</summary>
    AtNight,
    /// <summary>While a job's cargo is being carried.</summary>
    CarryingJob,
    /// <summary>In rain or snow at least as strong as RunState.BadWeatherStrength.</summary>
    InBadWeather,
}
