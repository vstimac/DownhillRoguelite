using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// A traveller who may sit by a station's fire (M11 NPCs): name, who they are (shown on the
/// tooltip), the language model's persona for them, a few lines they say without a model, from
/// which circle on they appear (0 = Limbo), the colour of their cloak and their voice's pitch.
/// </summary>
[GlobalClass]
public partial class NpcPersona : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>Who they are, in a few words ("a pilgrim who turned back").</summary>
    [Export] public string Title { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Persona { get; set; } = "";
    [Export] public string[] SilentLines { get; set; } = System.Array.Empty<string>();
    [Export] public int MinCircle { get; set; }
    [Export] public Color CloakColor { get; set; } = new(0.3f, 0.25f, 0.2f);
    [Export] public float VoicePitch { get; set; } = 1.0f;

    /// <summary>The speaker record for a conversation with them, at this spot.</summary>
    public SpeakerInfo Speaker(Vector3 at)
    {
        return new SpeakerInfo(Id, DisplayName, Persona, SilentLines, at, VoicePitch);
    }
}
