using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Every tunable value for the carrier's movement. Edit player/default_locomotion.tres in the
/// Inspector to tune feel without touching code.
/// </summary>
[GlobalClass]
public partial class LocomotionSettings : Resource
{
    [ExportGroup("Speed")]
    /// <summary>Target speed on flat ground when not braking, in m/s.</summary>
    [Export] public float StrideSpeed { get; set; } = 3.5f;

    /// <summary>Target speed while the brake is held, in m/s.</summary>
    [Export] public float BrakeSpeed { get; set; } = 1.2f;

    /// <summary>Hard cap on ground speed, in m/s.</summary>
    [Export] public float MaxSpeed { get; set; } = 6.0f;

    /// <summary>Extra target speed per unit of downhill grade (sine of the slope along the movement direction). Not applied while braking.</summary>
    [Export] public float DownhillSpeedBonus { get; set; } = 1.0f;

    /// <summary>Lost target speed per unit of uphill grade.</summary>
    [Export] public float UphillSpeedPenalty { get; set; } = 1.2f;

    /// <summary>Uphill target speed never drops below this fraction.</summary>
    [Export] public float MinUphillSpeedFactor { get; set; } = 0.35f;

    [ExportGroup("Acceleration")]
    /// <summary>Speed gain toward the target, in m/s².</summary>
    [Export] public float Acceleration { get; set; } = 4.0f;

    /// <summary>Speed loss when above the target or with no input, in m/s².</summary>
    [Export] public float Deceleration { get; set; } = 6.0f;

    /// <summary>Speed loss while braking, in m/s².</summary>
    [Export] public float BrakeDeceleration { get; set; } = 9.0f;

    /// <summary>Fraction of deceleration lost per unit of downhill grade: momentum makes downhill harder to stop.</summary>
    [Export] public float DownhillDecelerationLoss { get; set; } = 1.2f;

    /// <summary>Deceleration never drops below this fraction, however steep the descent.</summary>
    [Export] public float MinDecelerationFactor { get; set; } = 0.25f;

    [ExportGroup("Turning")]
    /// <summary>Turn rate when nearly stopped, in degrees per second.</summary>
    [Export] public float TurnRateAtRest { get; set; } = 720.0f;

    /// <summary>Turn rate at MaxSpeed, in degrees per second.</summary>
    [Export] public float TurnRateAtMaxSpeed { get; set; } = 140.0f;

    /// <summary>Below this speed the movement direction snaps to the input, in m/s.</summary>
    [Export] public float TurnInPlaceSpeed { get; set; } = 0.5f;

    /// <summary>How much a misaligned turn lowers the target speed (0 = not at all, 1 = fully).</summary>
    [Export] public float TurnSlowdown { get; set; } = 0.6f;

    /// <summary>How quickly the body yaws toward its movement direction.</summary>
    [Export] public float FacingSharpness { get; set; } = 12.0f;

    /// <summary>Below this speed the body faces the input direction instead of the velocity, in m/s.</summary>
    [Export] public float FacingMinSpeed { get; set; } = 0.2f;

    [ExportGroup("Ground")]
    /// <summary>Steeper surfaces are not floor: the carrier slides on them.</summary>
    [Export] public float MaxWalkableAngle { get; set; } = 38.0f;

    /// <summary>Surfaces steeper than this count as walls, not slides.</summary>
    [Export] public float MaxSlideAngle { get; set; } = 80.0f;

    /// <summary>How far down the body snaps to keep contact over crests and step-downs, in m.</summary>
    [Export] public float FloorSnapLength { get; set; } = 0.5f;

    /// <summary>Tallest ledge the carrier steps up without slowing, in m.</summary>
    [Export] public float StepHeight { get; set; } = 0.35f;

    /// <summary>
    /// Minimum forward distance moved onto a ledge when stepping up, in m. Must exceed the capsule
    /// radius, or the rounded bottom lands on the ledge's edge and the step is rejected.
    /// </summary>
    [Export] public float StepForwardProbe { get; set; } = 0.35f;

    /// <summary>No step-up attempts below this speed, in m/s.</summary>
    [Export] public float MinStepSpeed { get; set; } = 0.1f;

    /// <summary>Below this speed the carrier counts as idle, in m/s.</summary>
    [Export] public float IdleSpeed { get; set; } = 0.1f;

    [ExportGroup("Surface")]
    /// <summary>Grip used on geometry without a SurfaceType.</summary>
    [Export] public float DefaultGrip { get; set; } = 1.0f;
    /// <summary>Walkable angle at zero grip; MaxWalkableAngle applies at full grip, in degrees.</summary>
    [Export] public float LowGripWalkableAngle { get; set; } = 20.0f;
    /// <summary>Fraction of acceleration and braking left at zero grip.</summary>
    [Export] public float MinTraction { get; set; } = 0.2f;
    /// <summary>Downhill creep at zero grip on a vertical face; scaled by (1 - grip) and the slope's sine, in m/s.</summary>
    [Export] public float SurfaceCreepSpeed { get; set; } = 1.5f;

    [ExportGroup("Air and sliding")]
    /// <summary>Multiplier on the project's default gravity.</summary>
    [Export] public float GravityScale { get; set; } = 1.0f;

    /// <summary>Fraction of ground acceleration available in the air and while sliding.</summary>
    [Export] public float AirControl { get; set; } = 0.3f;

    /// <summary>Terminal falling speed, in m/s.</summary>
    [Export] public float MaxFallSpeed { get; set; } = 40.0f;
}
