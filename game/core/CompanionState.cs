namespace DownhillRoguelite.Core;

/// <summary>Where the companion's live conversation (M11) stands.</summary>
public enum CompanionState
{
    /// <summary>No language model found (or switched off): the companion can't talk back.</summary>
    Silent,
    /// <summary>A model is there but not loaded yet; it loads the first time you talk.</summary>
    Asleep,
    /// <summary>The model is loading.</summary>
    Waking,
    /// <summary>Ready for a question.</summary>
    Listening,
    /// <summary>Writing a reply.</summary>
    Thinking,
}
