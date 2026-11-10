namespace DownhillRoguelite.Core;

/// <summary>How the game's window is shown (Options → Video).</summary>
public enum DisplayMode
{
    Windowed,
    /// <summary>A borderless window covering the screen; switches away instantly.</summary>
    Borderless,
    /// <summary>Exclusive fullscreen; can be smoother on some systems.</summary>
    Fullscreen,
}
