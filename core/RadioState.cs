namespace DownhillRoguelite.Core;

/// <summary>
/// The music player as the UI sees it: whether it has songs and is playing, which song (index,
/// count, name), how far into it (seconds) and how long it is, the volume (0–1) and the folder the
/// player is told to put songs in.
/// </summary>
public readonly record struct RadioState(
    bool HasTracks,
    bool Playing,
    bool Started,
    int Index,
    int Count,
    string TrackName,
    float Position,
    float Length,
    float Volume,
    string Folder);
