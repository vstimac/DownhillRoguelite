using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// How depth maps to the circles of docs/narrative.md: each circle is DepthPerCircle metres deep;
/// below the last named one the descent is "beyond". Loaded by RunState from core/circles.tres.
/// </summary>
[GlobalClass]
public partial class CircleTable : Resource
{
    /// <summary>Metres of descent per circle (3 stations at the default 600 m).</summary>
    [Export] public float DepthPerCircle { get; set; } = 1800.0f;
    [Export] public string[] Names { get; set; } =
        { "Limbo", "Lust", "Gluttony", "Greed", "Wrath", "Heresy", "Violence", "Fraud", "Treachery" };
    [Export] public string BeyondName { get; set; } = "Beyond the circles";

    /// <summary>0-based circle at a depth; Names.Length (or more) means beyond.</summary>
    public int CircleAt(float depth)
    {
        return Mathf.Max(0, Mathf.FloorToInt(depth / DepthPerCircle));
    }

    public string NameOf(int circle)
    {
        if (circle >= 0 && circle < Names.Length)
        {
            return Names[circle];
        }
        return BeyondName;
    }
}
