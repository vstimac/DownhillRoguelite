using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One kind of animal: its model and how to show it (scale, rotation, which clip means idle, walk
/// and flee), how it moves, how shy it is, how many come together and where it lives (circles,
/// surfaces, slopes). Hides the packs' different clip names and axes. See docs/survival-loop.md (M9).
/// </summary>
[GlobalClass]
public partial class WildlifeSpecies : Resource
{
    [Export] public string DisplayName { get; set; } = "";
    [Export] public PackedScene? Model { get; set; }
    /// <summary>Uniform scale applied to the model (the Quaternius FBX packs import at about 1/25 size).</summary>
    [Export] public float ModelScale { get; set; } = 1.0f;
    /// <summary>Model rotation so it stands up and faces -Z, in degrees.</summary>
    [Export] public Vector3 ModelRotationDegrees { get; set; }

    [ExportGroup("Clips")]
    [Export] public string IdleClip { get; set; } = "";
    /// <summary>Walking clip; empty means the animal doesn't wander (it grazes in place).</summary>
    [Export] public string WalkClip { get; set; } = "";
    /// <summary>Fleeing clip (a run, or a hop for animals without one).</summary>
    [Export] public string FleeClip { get; set; } = "";
    /// <summary>Clip speed multiplier while fleeing.</summary>
    [Export] public float FleeClipSpeed { get; set; } = 1.0f;

    [ExportGroup("Body")]
    /// <summary>The animal's body for collisions: width, height and length, in m (a capsule fits inside).</summary>
    [Export] public Vector3 BodySize { get; set; } = new(0.6f, 1.2f, 1.6f);

    [ExportGroup("Movement")]
    [Export] public float WalkSpeed { get; set; } = 0.8f;
    [Export] public float FleeSpeed { get; set; } = 5.0f;
    /// <summary>How fast it turns, in degrees per second.</summary>
    [Export] public float TurnSpeed { get; set; } = 180.0f;
    /// <summary>Flies at this height above the ground (0 = walks).</summary>
    [Export] public float HoverHeight { get; set; }

    [ExportGroup("Shyness")]
    /// <summary>Bolts when the carrier comes this close, in m.</summary>
    [Export] public float FleeDistance { get; set; } = 10.0f;
    /// <summary>Bolts from this far when the carrier sprints, in m.</summary>
    [Export] public float SprintFleeDistance { get; set; } = 18.0f;
    /// <summary>Turns to watch the carrier within this distance, in m.</summary>
    [Export] public float WatchDistance { get; set; } = 25.0f;

    [ExportGroup("Groups and habitat")]
    /// <summary>Smallest and largest group.</summary>
    [Export] public Vector2I GroupSize { get; set; } = new(1, 3);
    /// <summary>Group members spawn within this radius of each other, in m.</summary>
    [Export] public float GroupSpread { get; set; } = 6.0f;
    /// <summary>Relative chance among the species that fit a spot.</summary>
    [Export] public float Weight { get; set; } = 1.0f;
    /// <summary>0-based circles it lives in (Limbo 0, Treachery 8, 9 = beyond the ninth).</summary>
    [Export] public int[] Circles { get; set; } = { 0 };
    /// <summary>Surface display names it lives on (Grass, Mud…); empty means any.</summary>
    [Export] public string[] Surfaces { get; set; } = System.Array.Empty<string>();
    /// <summary>Steepest ground it stands on, in degrees.</summary>
    [Export] public float MaxSlope { get; set; } = 30.0f;

    public bool LivesIn(int circle)
    {
        foreach (int c in Circles)
        {
            if (c == circle || (c >= 9 && circle >= 9))
            {
                return true;
            }
        }
        return false;
    }

    public bool LivesOn(string surface, float slopeDegrees)
    {
        if (slopeDegrees > MaxSlope)
        {
            return false;
        }
        if (Surfaces.Length == 0)
        {
            return true;
        }
        foreach (string s in Surfaces)
        {
            if (string.Equals(s, surface, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
