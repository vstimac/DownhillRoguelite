using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// Everything that shapes the ground fog: how deep and thick it lies, how fast it runs downhill,
/// how it churns, how hard the carrier ploughs it aside and how quickly it closes again, and how the
/// shader draws it. One resource (default_ground_fog.tres) so a circle or a weather front can swap in
/// its own; the console's /fog set changes these live.
/// </summary>
[GlobalClass]
public partial class GroundFogSettings : Resource
{
    [ExportGroup("Grid")]
    /// <summary>Cells along each side of the simulated patch around the carrier.</summary>
    [Export] public int GridSize { get; set; } = 128;
    /// <summary>Size of one cell, in m (the patch is GridSize × CellSize across).</summary>
    [Export] public float CellSize { get; set; } = 0.75f;
    /// <summary>The patch shifts (whole cells, keeping what it knows) when the carrier is this many cells off its centre.</summary>
    [Export] public int ShiftCells { get; set; } = 8;
    /// <summary>Simulation steps per second (fixed; the shader blends between the last two).</summary>
    [Export] public float StepsPerSecond { get; set; } = 30.0f;
    /// <summary>At most this many steps in one frame, so a hitch doesn't snowball.</summary>
    [Export] public int MaxStepsPerFrame { get; set; } = 3;

    [ExportGroup("Body")]
    /// <summary>How high the fog stands where its density is 1, in m.</summary>
    [Export] public float Depth { get; set; } = 1.1f;
    /// <summary>The density the fog settles back to on open, even ground.</summary>
    [Export] public float Ambient { get; set; } = 0.7f;
    /// <summary>Extra settled density per metre a cell lies below the ground around it (hollows fill).</summary>
    [Export] public float PoolBoost { get; set; } = 0.9f;
    /// <summary>How far around a cell (in cells) "the ground around it" reaches, and the most a hollow counts for (m).</summary>
    [Export] public int PoolRadius { get; set; } = 6;
    [Export] public float PoolMaxDepth { get; set; } = 1.5f;
    /// <summary>Seconds for a gap in the fog to close most of the way (recovery toward the settled density).</summary>
    [Export] public float RecoveryTime { get; set; } = 3.5f;
    /// <summary>The thickest the fog may pile up (density).</summary>
    [Export] public float MaxDensity { get; set; } = 2.2f;

    [ExportGroup("Flow")]
    /// <summary>How hard the fog's own surface slope drives it (m/s² per unit of slope): its speed downhill.</summary>
    [Export] public float Speed { get; set; } = 2.6f;
    /// <summary>Steeper slopes than this don't push any harder (rise over run).</summary>
    [Export] public float MaxSlope { get; set; } = 0.8f;
    /// <summary>The fastest the fog may move, in m/s.</summary>
    [Export] public float MaxFlow { get; set; } = 4.0f;
    /// <summary>Velocity lost per second (fraction, as a rate).</summary>
    [Export] public float Drag { get; set; } = 0.9f;
    /// <summary>How quickly density evens out between neighbours (m²/s).</summary>
    [Export] public float Diffusion { get; set; } = 0.12f;
    /// <summary>How quickly velocity evens out between neighbours (m²/s).</summary>
    [Export] public float Viscosity { get; set; } = 0.25f;
    /// <summary>Strength of the slow swirling that keeps still fog alive (m/s²).</summary>
    [Export] public float Turbulence { get; set; } = 0.45f;
    /// <summary>Size of those swirls (m) and how fast they drift (per s).</summary>
    [Export] public float TurbulenceScale { get; set; } = 6.0f;
    [Export] public float TurbulenceRate { get; set; } = 0.25f;

    [ExportGroup("Carrier")]
    /// <summary>Overall strength of what the carrier does to the fog (1 = as designed).</summary>
    [Export] public float WakeStrength { get; set; } = 1.0f;
    /// <summary>Radius of the bubble the body clears, in m, and how much wider it gets at a sprint.</summary>
    [Export] public float PushRadius { get; set; } = 1.1f;
    [Export] public float SprintRadiusBoost { get; set; } = 0.6f;
    /// <summary>The speed counted as a full sprint, in m/s.</summary>
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    /// <summary>How fast the body shoves fog outward (m/s at the middle), and how much of its own velocity it drags along.</summary>
    [Export] public float PushSpeed { get; set; } = 2.4f;
    [Export] public float DragAlong { get; set; } = 0.55f;
    /// <summary>How much of the fog in the bubble is cleared per second (fraction, as a rate).</summary>
    [Export] public float Displace { get; set; } = 5.0f;
    /// <summary>The pair of eddies peeling off behind: spin (m/s), radius (m), distance behind (m).</summary>
    [Export] public float WakeSpin { get; set; } = 1.6f;
    [Export] public float WakeRadius { get; set; } = 1.2f;
    [Export] public float WakeBehind { get; set; } = 0.9f;
    /// <summary>Churn added in the wake at a sprint (m/s², random).</summary>
    [Export] public float SprintTurbulence { get; set; } = 2.5f;
    /// <summary>A footfall: spin (m/s), radius (m), and the puff outward (m/s).</summary>
    [Export] public float StepSwirl { get; set; } = 1.4f;
    [Export] public float StepRadius { get; set; } = 1.2f;
    [Export] public float StepPuff { get; set; } = 1.2f;
    /// <summary>The carrier's feet only stir fog if they're within this of its surface, in m.</summary>
    [Export] public float StirHeight { get; set; } = 1.4f;

    [ExportGroup("Ground")]
    /// <summary>Rays and shape queries per physics frame for learning the ground under new cells.</summary>
    [Export] public int SamplesPerFrame { get; set; } = 768;
    /// <summary>Layers the ground and its obstacles are on.</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    /// <summary>A cell is blocked (a trunk, a rock, a wall) if something stands at this height above its ground, this thick (share of a cell).</summary>
    [Export] public float ObstacleHeight { get; set; } = 0.6f;
    [Export] public float ObstacleSize { get; set; } = 0.45f;
    /// <summary>Rays start this far above the carrier (or the last known ground) and look this far down.</summary>
    [Export] public float RayTop { get; set; } = 60.0f;
    [Export] public float RayLength { get; set; } = 160.0f;
    /// <summary>Cells the ground wasn't found under are tried again after this many seconds (chunks still streaming in).</summary>
    [Export] public float RetrySeconds { get; set; } = 1.0f;

    [ExportGroup("Look")]
    /// <summary>Light absorbed per metre at density 1.</summary>
    [Export] public float Density { get; set; } = 1.6f;
    [Export] public Color DayColor { get; set; } = new(0.86f, 0.88f, 0.9f);
    [Export] public Color NightColor { get; set; } = new(0.16f, 0.19f, 0.26f);
    /// <summary>How much brighter the top of the fog is than its foot (light from above).</summary>
    [Export] public float TopLight { get; set; } = 0.35f;
    /// <summary>Size of the wisps (m) and how much they thin and thicken it (0-1).</summary>
    [Export] public float NoiseSize { get; set; } = 6.0f;
    [Export] public float NoiseAmount { get; set; } = 0.65f;
    /// <summary>How far the wisps get carried by the flow before they reset (s), and how ragged the top is (share of the depth).</summary>
    [Export] public float FlowPeriod { get; set; } = 2.0f;
    [Export] public float RaggedTop { get; set; } = 0.45f;
    /// <summary>Ray-march steps at most, the furthest it draws (m), and the finest step inside the fog (m).</summary>
    [Export] public int Steps { get; set; } = 48;
    [Export] public float MaxDistance { get; set; } = 60.0f;
    [Export] public float FineStep { get; set; } = 0.22f;
    /// <summary>Inside the fog the steps grow by this share of the distance (far fog needs less detail).</summary>
    [Export] public float DistanceStep { get; set; } = 0.035f;
    /// <summary>The fog fades out over this much of the patch's edge, in m.</summary>
    [Export] public float EdgeFade { get; set; } = 18.0f;
    /// <summary>The fog thins from this share of MaxDistance to nothing at MaxDistance, so it never ends in a line.</summary>
    [Export] public float FarFade { get; set; } = 0.55f;
    /// <summary>The fog thins toward the camera inside this distance (m), so the view never starts in a white wall.</summary>
    [Export] public float NearClear { get; set; } = 3.5f;
    /// <summary>How much of the layer's height its top thins over (0 a hard top, 1 thinning from the ground up).</summary>
    [Export] public float TopSoftness { get; set; } = 0.7f;
}
