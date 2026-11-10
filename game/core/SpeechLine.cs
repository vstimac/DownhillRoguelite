namespace DownhillRoguelite.Core;

/// <summary>
/// Something said aloud in the world: who says it, the words, where the voice comes from (world
/// position; the audio plays from there and the subtitle is written on that side of the screen)
/// and how long it lasts, in seconds.
/// </summary>
public readonly record struct SpeechLine(string Speaker, string Text, Godot.Vector3 From, float Duration);
