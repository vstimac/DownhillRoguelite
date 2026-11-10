namespace DownhillRoguelite.Core;

/// <summary>What the music player is asked to do (by the radio page, the summit car or a key).</summary>
public enum RadioCommand
{
    Play,
    Pause,
    Toggle,
    Next,
    Previous,
    Stop,
    /// <summary>Looks in the music folders again (songs dropped in while the game runs).</summary>
    Rescan,
}
