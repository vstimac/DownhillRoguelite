namespace DownhillRoguelite.Core;

/// <summary>
/// A point of interest beside the path (a spring, an abandoned pack, a grave): its id and title,
/// a line of text (what lies there, or what was taken), where it is, how far away it is and on which
/// side of the carrier's view ("left", "right", "ahead", "behind").
/// </summary>
public readonly record struct WaysideNote(
    string Id,
    string Title,
    string Text,
    Godot.Vector3 Position,
    float Distance = 0.0f,
    string Side = "");
