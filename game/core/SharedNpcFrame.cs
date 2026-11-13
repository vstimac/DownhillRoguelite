using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Public presentation only. Dialogue choices and personal saves stay with the participant.</summary>
public sealed record SharedNpcFrame(Vector3 Position, float Yaw, bool Busy, bool Speaking, float Clock,
    string Clip, float ClipTime, string Line, int LineSequence, float LineElapsed);
