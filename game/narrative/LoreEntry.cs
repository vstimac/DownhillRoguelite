using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// One found text (narrative/lore/*.tres): its id (kept across runs), the heading it is found under,
/// its words, what it is written on, how deep it may first appear, which circle it belongs to (-1:
/// any) and an exclusive group: two texts of one group never appear in the same run (incompatible
/// accounts stay unreconciled).
/// </summary>
[GlobalClass]
public partial class LoreEntry : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string Heading { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
    [Export] public LoreForm Form { get; set; } = LoreForm.Note;
    /// <summary>The first stage it may stand in (0: between the summit and Station 1).</summary>
    [Export] public int EarliestStage { get; set; } = 1;
    /// <summary>The circle it belongs to (0 = Limbo), or -1 for any.</summary>
    [Export] public int Circle { get; set; } = -1;
    [Export] public string ExclusiveGroup { get; set; } = "";
    /// <summary>Relative chance among the texts that fit.</summary>
    [Export] public float Weight { get; set; } = 1.0f;

    /// <summary>Why the entry can't be used, or "" when it can.</summary>
    public string Problem()
    {
        if (Id.Length == 0)
        {
            return "no id";
        }
        if (Heading.Length == 0 || Text.Length == 0)
        {
            return Id + ": no heading or text";
        }
        if (Weight <= 0.0f)
        {
            return Id + ": weight must be positive";
        }
        return "";
    }
}
