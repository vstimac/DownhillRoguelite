namespace DownhillRoguelite.Core;

/// <summary>
/// Someone the player can talk with: the companion, or a traveller met at a station (M11 NPCs).
/// Id keys their conversation; Persona is the language model's instructions for them (empty for
/// the companion, whose persona lives in its own file); SilentLines are what they say when no model
/// is available; Position is where their voice comes from (the companion's moves with the
/// carrier, see GameEvents.CompanionPosition); Pitch shapes their spoken voice.
/// </summary>
public readonly record struct SpeakerInfo(string Id, string Name, string Persona, string[] SilentLines, Godot.Vector3 Position, float Pitch)
{
    public const string CompanionId = "companion";

    public static readonly SpeakerInfo Companion = new(CompanionId, "Companion", "", System.Array.Empty<string>(), Godot.Vector3.Zero, 0.95f);

    public bool IsCompanion => Id == CompanionId;
}
