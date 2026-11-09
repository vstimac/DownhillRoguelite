using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One of the night's beasts (Dante's leopard, lion and she-wolf): how it looks in the dark (model,
/// tint, glowing eyes), how it stalks and charges, how far its hit throws the carrier, and in which
/// circles it hunts. See docs/night-beasts.md.
/// </summary>
[GlobalClass]
public partial class BeastSpecies : Resource
{
    [Export] public string DisplayName { get; set; } = "";
    [Export] public PackedScene? Model { get; set; }
    [Export] public float ModelScale { get; set; } = 1.0f;
    /// <summary>Model rotation so it stands up and faces -Z, in degrees.</summary>
    [Export] public Vector3 ModelRotationDegrees { get; set; } = new(0, 180, 0);
    /// <summary>Darkens the model toward a silhouette (alpha = how dark).</summary>
    [Export] public Color Shade { get; set; } = new(0.0f, 0.0f, 0.0f, 0.55f);
    [Export] public Color EyeColor { get; set; } = new(1.0f, 0.75f, 0.2f);
    /// <summary>Between the eyes, in the beast's space (front is -Z), in m.</summary>
    [Export] public Vector3 EyeOffset { get; set; } = new(0.0f, 0.75f, -0.45f);
    [Export] public float EyeSpacing { get; set; } = 0.16f;
    [Export] public float EyeSize { get; set; } = 0.035f;

    [ExportGroup("Clips")]
    [Export] public string IdleClip { get; set; } = "idle";
    [Export] public string WalkClip { get; set; } = "walk";
    [Export] public string RunClip { get; set; } = "run";
    /// <summary>Playback speed of the run clip (a model with only a walk cycle plays it faster to run).</summary>
    [Export] public float RunClipSpeed { get; set; } = 1.0f;

    [ExportGroup("Voice")]
    /// <summary>Played as it crouches before a charge (the warning), one picked at random.</summary>
    [Export] public string[] GrowlSounds { get; set; } = System.Array.Empty<string>();
    /// <summary>Played as it charges and as it tears food from the pack.</summary>
    [Export] public string[] SnarlSounds { get; set; } = System.Array.Empty<string>();
    /// <summary>Its call while it stalks (a howl, a roar), now and then, heard from far off.</summary>
    [Export] public string[] CallSounds { get; set; } = System.Array.Empty<string>();
    /// <summary>Seconds between calls while stalking.</summary>
    [Export] public Vector2 CallInterval { get; set; } = new(16.0f, 30.0f);
    [Export] public float VoiceVolumeDb { get; set; } = 6.0f;
    /// <summary>How far its voice carries, in m.</summary>
    [Export] public float VoiceRange { get; set; } = 70.0f;

    [ExportGroup("Body")]
    /// <summary>Width, height and length of the collision body, in m (a capsule fits inside; keep it a little smaller than the model so it doesn't snag on rough ground).</summary>
    [Export] public Vector3 BodySize { get; set; } = new(0.6f, 1.0f, 1.1f);
    /// <summary>Steepest ground it walks up, in degrees.</summary>
    [Export] public float MaxSlope { get; set; } = 60.0f;

    [ExportGroup("Hunting")]
    /// <summary>Keeps this far from the carrier while stalking (min, max), in m.</summary>
    [Export] public Vector2 StalkRadius { get; set; } = new(14.0f, 22.0f);
    [Export] public float StalkSpeed { get; set; } = 2.2f;
    /// <summary>Runs this fast to catch up when left behind, in m/s.</summary>
    [Export] public float CatchUpSpeed { get; set; } = 7.0f;
    /// <summary>Seconds between attacks (random in this range); shorter when the carrier smells of food.</summary>
    [Export] public Vector2 AttackInterval { get; set; } = new(12.0f, 24.0f);
    /// <summary>Seconds it crouches and growls before charging: the warning to get out of the way.</summary>
    [Export] public float WindUp { get; set; } = 1.2f;
    [Export] public float ChargeSpeed { get; set; } = 10.0f;
    /// <summary>How fast the charge bends toward the carrier, in degrees per second (low: you can sidestep).</summary>
    [Export] public float ChargeTurnSpeed { get; set; } = 35.0f;
    [Export] public float TurnSpeed { get; set; } = 240.0f;

    [ExportGroup("Hit")]
    /// <summary>How high a hit aims to throw the carrier, in m.</summary>
    [Export] public float ThrowHeight { get; set; } = 3.0f;
    /// <summary>How fast a hit throws the carrier along the charge, in m/s.</summary>
    [Export] public float ThrowSpeed { get; set; } = 6.0f;
    /// <summary>Tears food out of the pack instead of a full throw, when there is food (the she-wolf's hunger).</summary>
    [Export] public bool StealsFood { get; set; }

    [ExportGroup("Where")]
    /// <summary>From this circle on (0 = Limbo).</summary>
    [Export] public int MinCircle { get; set; }
    /// <summary>Relative chance among the beasts that hunt in a circle.</summary>
    [Export] public float Weight { get; set; } = 1.0f;
}
