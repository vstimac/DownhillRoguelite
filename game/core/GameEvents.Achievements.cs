using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Achievements (progression/AchievementTracker): kills are counted from EnemyKilled, and an
/// unlocked achievement is announced once, ever, through AchievementUnlocked (the HUD's toast).
/// </summary>
public static partial class GameEvents
{
    /// <summary>The carrier killed something (a humanoid enemy or a night beast), by its display name.</summary>
    public static event Action<string>? EnemyKilled;
    /// <summary>An achievement was unlocked for the first time: its id and title.</summary>
    public static event Action<string, string>? AchievementUnlocked;

    public static void PublishEnemyKilled(string name) => EnemyKilled?.Invoke(name);

    public static void PublishAchievementUnlocked(string id, string title) => AchievementUnlocked?.Invoke(id, title);
}
