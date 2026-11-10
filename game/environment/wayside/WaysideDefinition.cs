using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A kind of point of interest beside the path (the purpose patch): something worth a detour, with
/// what it holds (item resource paths, taken once with E), how it looks from afar (pennant colour,
/// a crate, a stone or a basin) and what it says. Placed by WaysidePlacer from the circle it may
/// first appear in, weighted.
/// </summary>
[GlobalClass]
public partial class WaysideDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    /// <summary>What it is, as the tooltip and objective name it ("A cold spring").</summary>
    [Export] public string Title { get; set; } = "";
    /// <summary>What it looks like up close, and why it's worth it.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    /// <summary>The verb on the E prompt ("Fill a flask").</summary>
    [Export] public string Prompt { get; set; } = "Take";
    /// <summary>The journal's line once it's been used ("I filled a flask at the spring.").</summary>
    [Export(PropertyHint.MultilineText)] public string TakenText { get; set; } = "";
    /// <summary>What it holds: item resource paths, each packed once.</summary>
    [Export] public string[] ItemPaths { get; set; } = System.Array.Empty<string>();
    /// <summary>The first circle it may appear in (0 = Limbo).</summary>
    [Export] public int MinCircle { get; set; }
    [Export] public float Weight { get; set; } = 1.0f;
    [Export] public Color PennantColor { get; set; } = new(0.75f, 0.2f, 0.15f);
    /// <summary>What stands at its foot: 0 a crate, 1 a stone, 2 a basin of water.</summary>
    [Export(PropertyHint.Enum, "Crate,Stone,Basin")] public int Look { get; set; }

    /// <summary>Whether it can be placed: an id, a title and something to take.</summary>
    public bool IsValid => Id.Length > 0 && Title.Length > 0 && ItemPaths.Length > 0;
}
