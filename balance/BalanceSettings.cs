using Godot;

namespace DownhillRoguelite.Balance;

/// <summary>
/// Every tunable value of the balance model. Edit balance/default_balance.tres in the Inspector.
/// Angles are in degrees here and converted where used.
/// </summary>
[GlobalClass]
public partial class BalanceSettings : Resource
{
    [ExportGroup("Body")]
    [Export] public float BodyMass { get; set; } = 70.0f;
    /// <summary>Height of the carrier's own centre of mass above the feet, in m.</summary>
    [Export] public float BodyComHeight { get; set; } = 1.0f;
    /// <summary>Body inertia about the feet as a multiple of BodyMass · BodyComHeight².</summary>
    [Export] public float BodyInertiaFactor { get; set; } = 1.0f;

    [ExportGroup("Carrier correction")]
    /// <summary>
    /// Muscle stiffness pulling the tilt toward its target, in N·m/rad. The carrier's own weight is
    /// folded in, so this only has to beat the load's m·g·h; the closer the load gets to it, the
    /// harder balancing becomes.
    /// </summary>
    [Export] public float Strength { get; set; } = 600.0f;
    /// <summary>Damping as a fraction of critical damping.</summary>
    [Export] public float DampingRatio { get; set; } = 0.55f;
    /// <summary>Lower bound on (Strength - load m·g·h) used for damping, so it never vanishes, in N·m/rad.</summary>
    [Export] public float MinEffectiveStiffness { get; set; } = 50.0f;
    /// <summary>How much of a forward/back load offset the carrier leans into automatically (1 = all of it).</summary>
    [Export] public float AutoPitchCompensation { get; set; } = 1.0f;
    /// <summary>How much of a sideways load offset the carrier leans into automatically; the rest is the player's job.</summary>
    [Export] public float AutoRollCompensation { get; set; } = 0.25f;

    [ExportGroup("Player input")]
    /// <summary>Extra sideways lean at full lean input, in degrees.</summary>
    [Export] public float MaxLeanAngle { get; set; } = 7.0f;
    /// <summary>How fast the lean input follows the buttons, in units per second.</summary>
    [Export] public float LeanResponse { get; set; } = 6.0f;
    [Export] public float BraceStiffnessMultiplier { get; set; } = 1.8f;
    [Export] public float BraceDampingMultiplier { get; set; } = 1.5f;

    [ExportGroup("Disturbances")]
    /// <summary>
    /// Scale on the load's torque from the carrier's acceleration (braking, turning). Below 1
    /// because a real carrier anticipates its own stops and turns.
    /// </summary>
    [Export] public float InertialFactor { get; set; } = 0.25f;
    /// <summary>Acceleration is clamped to this before use, so teleports and step-ups don't spike, in m/s².</summary>
    [Export] public float MaxAcceleration { get; set; } = 15.0f;
    /// <summary>Cross-slope torque as a fraction of m·g·h·sin(roll under the feet).</summary>
    [Export] public float SlopeRollFactor { get; set; } = 0.15f;
    /// <summary>Descent torque as a fraction of m·g·h·sin(pitch under the feet).</summary>
    [Export] public float SlopePitchFactor { get; set; } = 0.1f;
    /// <summary>Footfall angular impulse per (m/s of speed · kg·m of load height moment), in N·m·s.</summary>
    [Export] public float StepJolt { get; set; } = 0.15f;
    /// <summary>Footfall jolt multiplier added at zero grip: jolt × (1 + this × (1 - grip)).</summary>
    [Export] public float LowGripJoltMultiplier { get; set; } = 2.0f;
    /// <summary>Landing angular impulse per (m/s of landing speed · kg·m of load height moment), in N·m·s; pitches forward.</summary>
    [Export] public float LandingJolt { get; set; } = 0.05f;
    /// <summary>Fraction of a footfall jolt that pitches forward, per unit of downhill slope.</summary>
    [Export] public float StepPitchJoltFraction { get; set; } = 1.0f;

    [ExportGroup("Wind")]
    /// <summary>Wind force per m² of exposed area per (m/s)² of wind speed: half air density times drag coefficient.</summary>
    [Export] public float WindDrag { get; set; } = 0.6f;
    /// <summary>Carrier's own area facing the wind, in m².</summary>
    [Export] public float WindBodyArea { get; set; } = 0.25f;
    /// <summary>Height the wind pushes the body at, in m.</summary>
    [Export] public float WindBodyHeight { get; set; } = 1.0f;
    /// <summary>Extra area per kg of load (a bigger pack catches more wind), in m²; pushed at the load's height.</summary>
    [Export] public float WindAreaPerKg { get; set; } = 0.012f;

    [ExportGroup("Support")]
    [Export] public float FootHalfWidth { get; set; } = 0.05f;
    [Export] public float FootHalfLength { get; set; } = 0.13f;
    [Export] public float MinSupportHalfWidth { get; set; } = 0.12f;
    [Export] public float MinSupportHalfLength { get; set; } = 0.15f;
    [Export] public float SupportSharpness { get; set; } = 6.0f;

    [ExportGroup("States")]
    /// <summary>Imbalance (1 = centre of mass at the support edge) where wobbling starts.</summary>
    [Export] public float WobbleThreshold { get; set; } = 0.5f;
    [Export] public float StumbleThreshold { get; set; } = 1.0f;
    /// <summary>A stumble is recovered once imbalance drops back below this.</summary>
    [Export] public float RecoverThreshold { get; set; } = 0.75f;
    /// <summary>Imbalance that ends a stumble in a fall immediately.</summary>
    [Export] public float FallThreshold { get; set; } = 2.5f;
    /// <summary>Seconds a stumble lasts before it becomes a fall.</summary>
    [Export] public float StumbleWindow { get; set; } = 0.9f;
    /// <summary>Carrier stiffness multiplier while stumbling: the stumble is harder to fight.</summary>
    [Export] public float StumbleStiffnessFactor { get; set; } = 0.85f;
    /// <summary>While stumbling the body keeps tipping: a push along the stumble of this × the load's m·g·h.</summary>
    [Export] public float StumblePushFactor { get; set; } = 0.4f;
    /// <summary>Torque the player's recovery effort (leaning against the stumble, or bracing) applies, in N·m.</summary>
    [Export] public float RecoveryTorque { get; set; } = 250.0f;
    [Export] public float FallDuration { get; set; } = 2.5f;
    /// <summary>Visual tilt the body falls to, in degrees.</summary>
    [Export] public float FallTiltAngle { get; set; } = 70.0f;
    [Export] public float FallTiltSpeed { get; set; } = 4.0f;
    /// <summary>Tilt is clamped to this on each axis, in degrees.</summary>
    [Export] public float MaxTiltAngle { get; set; } = 60.0f;
    /// <summary>A carrier jump larger than this in one frame (respawn) resets balance, in m.</summary>
    [Export] public float TeleportDistance { get; set; } = 1.0f;
}
