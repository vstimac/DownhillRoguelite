namespace DownhillRoguelite.Narrative;

/// <summary>Where one of the president's quests stands (saved as not_started / active / ready / completed).</summary>
public enum QuestStatus
{
    NotStarted,
    Active,
    /// <summary>Everything gathered: hand it in at the president.</summary>
    Ready,
    Completed,
}
