namespace DownhillRoguelite.Narrative;

/// <summary>
/// One recorded line of a voice pack (NPC_Vucic_Dialogues.json): its recording id (vucic_001...),
/// key and context as the pack names them, the words, and the two takes of the same line
/// (forest and outdoor acoustics) with their lengths in seconds.
/// </summary>
public readonly record struct VoicePackLine(
    string Id,
    string Key,
    string Context,
    string Text,
    string ForestPath,
    string OutdoorPath,
    float ForestSeconds,
    float OutdoorSeconds)
{
    public const string Forest = "forest";
    public const string Outdoor = "outdoor";

    /// <summary>The take for a variant ("forest" or "outdoor"), then the other one: never both.</summary>
    public string[] PathsFor(string variant)
    {
        if (variant == Forest)
        {
            return new[] { ForestPath, OutdoorPath };
        }
        return new[] { OutdoorPath, ForestPath };
    }
}
