using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A small composition of models left on the mountain (environment/set_pieces/*.tres): a ruined
/// arch, an abandoned wagon, a row of banners, a far windmill. Wayside pieces stand beside a path;
/// landmarks stand far off it and are seen from a long way. A piece favours some circles.
/// </summary>
[GlobalClass]
public partial class SetPiece : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public SetPieceKind Kind { get; set; } = SetPieceKind.Wayside;
    [Export] public Godot.Collections.Array<SetPiecePart> Parts { get; set; } = new();
    /// <summary>Circles (0 = Limbo) where the piece is three times as likely; empty: every circle alike.</summary>
    [Export] public int[] FavouredCircles { get; set; } = System.Array.Empty<int>();
    /// <summary>The first circle it may appear in.</summary>
    [Export] public int FirstCircle { get; set; }
    [Export] public float Weight { get; set; } = 1.0f;
    /// <summary>Radius of ground it takes, in m (kept clear of paths and other pieces).</summary>
    [Export] public float Radius { get; set; } = 3.0f;

    public bool Favours(int circle)
    {
        foreach (int favoured in FavouredCircles)
        {
            if (favoured == circle)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Why the piece can't be used, or "" when it can.</summary>
    public string Problem()
    {
        if (Id.Length == 0)
        {
            return "no id";
        }
        if (Parts.Count == 0)
        {
            return Id + ": no parts";
        }
        foreach (SetPiecePart part in Parts)
        {
            if (part == null || !ResourceLoader.Exists(part.Model))
            {
                return Id + ": a part's model is missing";
            }
        }
        if (Weight <= 0.0f || Radius <= 0.0f)
        {
            return Id + ": weight and radius must be positive";
        }
        return "";
    }
}
