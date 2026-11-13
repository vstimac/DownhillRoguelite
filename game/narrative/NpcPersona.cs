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
    /// <summary>
    /// A rigged model (Mixamo bone names, any prefix) shown instead of the cloaked figure. It follows
    /// the mannequin rig sitting by the fire (see docs/npc-models.md). Empty: the cloaked figure.
    /// </summary>
    [Export(PropertyHint.File, "*.glb,*.gltf,*.tscn")] public string ModelPath { get; set; } = "";
    /// <summary>Uniform scale for the model (generated models often come in about 1 m tall).</summary>
    [Export] public float ModelScale { get; set; } = 1.0f;

    /// <summary>
    /// A Croatian speaker (the šalteruša): the model answers in Croatian, or in English when Options →
    /// Dialogue text is English, the same way a recorded Croatian line gets an English subtitle.
    /// </summary>
    [Export] public bool Croatian { get; set; }
    /// <summary>The silent lines in Croatian (Croatian speakers, Hrvatski chosen); empty: SilentLines.</summary>
    [Export] public string[] SilentLinesHr { get; set; } = System.Array.Empty<string>();
    [Export(PropertyHint.MultilineText)] public string CroatianReplies { get; set; } =
        "Language: always reply in Croatian (standard Croatian, ijekavian), never in English, even when the carrier writes in English.";
    [Export(PropertyHint.MultilineText)] public string EnglishReplies { get; set; } =
        "Language: you are Croatian, but write your replies in English, keeping the odd Croatian word.";

    /// <summary>The speaker record for a conversation with them, at this spot (in the chosen dialogue language).</summary>
    public SpeakerInfo Speaker(Vector3 at)
    {
        if (!Croatian)
        {
            return new SpeakerInfo(Id, DisplayName, Persona, SilentLines, at, VoicePitch);
        }
        if (GameOptions.DialogueLanguage == DialogueLanguage.English)
        {
            return new SpeakerInfo(Id, DisplayName, Persona + "\n\n" + EnglishReplies, SilentLines, at, VoicePitch);
        }
        string[] silent = SilentLines;
        if (SilentLinesHr.Length > 0)
        {
            silent = SilentLinesHr;
        }
        return new SpeakerInfo(Id, DisplayName, Persona + "\n\n" + CroatianReplies, silent, at, VoicePitch);
    }
}
