using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>How an animal takes the carrier: bolts early, walks off, comes to look, or holds its ground.</summary>
public enum Temperament
{
    /// <summary>Bolts early and runs, the whole herd with it (sheep, zebras, rats, deer).</summary>
    Skittish,
    /// <summary>Walks away and keeps an eye on the carrier; runs only from a sprinter (horses, snakes, foxes).</summary>
    Wary,
    /// <summary>Comes a few metres toward a carrier who stands still, then loses interest (cows, llamas, cats).</summary>
    Curious,
    /// <summary>Holds its ground and pushes back when pushed (pigs, polar bears).</summary>
    Bold,
}

/// <summary>
/// One kind of animal: its model and how to show it (scale, rotation, which clip means idle, graze,
/// walk and flee), how it moves, how it senses and takes the carrier (temperament, sight, hearing,
/// flight distances), how many come together and where it lives (circles, surfaces, slopes). Hides
/// the packs' different clip names and axes. See docs/survival-loop.md (M9).
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
    /// <summary>Grazing clip (head down); empty or missing means the idle clip.</summary>
    [Export] public string GrazeClip { get; set; } = "";
    /// <summary>Lying-down clip; empty means the idle clip, slowed, with the model lowered by RestSink.</summary>
    [Export] public string RestClip { get; set; } = "";
    /// <summary>How far the model sinks while lying down, as a share of its body height (0 = never lies down).</summary>
    [Export] public float RestSink { get; set; }
    /// <summary>Walking clip; empty means the flee clip, slowed (a hop). No walk and no flee clip: it grazes in place.</summary>
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

    [ExportGroup("Senses and temperament")]
    [Export] public Temperament Temperament { get; set; } = Temperament.Wary;
    /// <summary>Sees a moving carrier this far, in m (a still one at about half: WildlifeTable.StillSight).</summary>
    [Export] public float SightRange { get; set; } = 30.0f;
    /// <summary>Hears a sprinting carrier this far, in m (walking, standing and heavy loads scale it: WildlifeTable).</summary>
    [Export] public float HearingRange { get; set; } = 25.0f;
    /// <summary>Seconds of the alert beat (head up, frozen, facing the carrier) on first noticing it.</summary>
    [Export] public float AlertSeconds { get; set; } = 1.5f;
    /// <summary>Reacts (bolts if skittish, walks off if wary or curious) when a walking carrier comes this close, in m.</summary>
    [Export] public float FleeDistance { get; set; } = 10.0f;
    /// <summary>The same for a sprinting, falling or fast-sliding carrier, in m.</summary>
    [Export] public float SprintFleeDistance { get; set; } = 18.0f;
    /// <summary>Stops fleeing once this far from what scared it, in m.</summary>
    [Export] public float SafeDistance { get; set; } = 25.0f;
    /// <summary>A curious animal comes to look at a carrier standing still within this distance, in m.</summary>
    [Export] public float CuriousRange { get; set; } = 14.0f;
    /// <summary>...and stops this far from it, in m.</summary>
    [Export] public float ApproachStop { get; set; } = 3.0f;

    [ExportGroup("Groups and habitat")]
    /// <summary>Smallest and largest group.</summary>
    [Export] public Vector2I GroupSize { get; set; } = new(1, 3);
    /// <summary>Group members spawn within this radius of each other, in m.</summary>
    [Export] public float GroupSpread { get; set; } = 6.0f;
    /// <summary>Herd members keep at least this far apart, in m.</summary>
    [Export] public float HerdSpacing { get; set; } = 2.0f;
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
