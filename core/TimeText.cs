namespace DownhillRoguelite.Core;

/// <summary>
/// Time the way the player reads it: real minutes, not the in-game clock. Durations ("about 9 min",
/// "under a minute") from real seconds or from in-game hours at the clock's pace, and the part of
/// the day in words ("morning", "night").
/// </summary>
public static class TimeText
{
    /// <summary>A real duration in words: "under a minute", "about 9 min", "1 h 05 min".</summary>
    public static string Real(float seconds)
    {
        if (seconds < 60.0f)
        {
            return "under a minute";
        }
        int minutes = Godot.Mathf.RoundToInt(seconds / 60.0f);
        if (minutes < 60)
        {
            return $"{minutes} min";
        }
        return $"{minutes / 60} h {minutes % 60:00} min";
    }

    /// <summary>In-game hours as real time at the clock's pace (in-game hours per real second).</summary>
    public static string FromHours(float hours, float hoursPerSecond)
    {
        if (hoursPerSecond <= 0.0f)
        {
            return "a while";
        }
        return Real(hours / hoursPerSecond);
    }

    /// <summary>The part of the day at an in-game hour.</summary>
    public static string PartOfDay(float hour)
    {
        float h = Godot.Mathf.PosMod(hour, 24.0f);
        if (h < 5.0f || h >= 21.0f)
        {
            return "night";
        }
        if (h < 8.0f)
        {
            return "dawn";
        }
        if (h < 12.0f)
        {
            return "morning";
        }
        if (h < 17.0f)
        {
            return "afternoon";
        }
        return "evening";
    }
}
