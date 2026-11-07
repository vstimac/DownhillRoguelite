using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// Every tunable value of the endless mountain. Edit terrain/default_terrain.tres in the Inspector.
/// Distances in m, angles in degrees. The mountain descends toward -Z. Don't change values while
/// chunks are generating: the generator reads them from a background thread.
/// </summary>
[GlobalClass]
public partial class TerrainSettings : Resource
{
    /// <summary>Seed used when RunState has none (0).</summary>
    [Export] public ulong DefaultSeed { get; set; } = 1;

    [ExportGroup("Chunks")]
    /// <summary>Cells per chunk side; chunks are ChunkCells × CellSize metres wide.</summary>
    [Export] public int ChunkCells { get; set; } = 64;
    [Export] public float CellSize { get; set; } = 1.0f;

    [ExportGroup("Mountain")]
    /// <summary>Average descent angle of the mountain along -Z.</summary>
    [Export] public float Grade { get; set; } = 20.0f;
    /// <summary>Slow rises and drops along the descent, applied everywhere including the spine.</summary>
    [Export] public float BaseVariationAmplitude { get; set; } = 3.0f;
    [Export] public float BaseVariationWavelength { get; set; } = 200.0f;
    /// <summary>Ridges and hollows across the mountain (not on the spine).</summary>
    [Export] public float MountainAmplitude { get; set; } = 14.0f;
    [Export] public float MountainWavelength { get; set; } = 90.0f;
    [Export] public int MountainOctaves { get; set; } = 3;
    /// <summary>Small uneven ground (not on the spine).</summary>
    [Export] public float BumpAmplitude { get; set; } = 0.5f;
    [Export] public float BumpWavelength { get; set; } = 6.0f;

    [ExportGroup("Spine (guaranteed walkable path)")]
    /// <summary>How far the path wanders sideways.</summary>
    [Export] public float SpineMeanderAmplitude { get; set; } = 18.0f;
    [Export] public float SpineMeanderWavelength { get; set; } = 220.0f;
    /// <summary>Half width of the flat walkable band.</summary>
    [Export] public float SpineHalfWidth { get; set; } = 4.0f;
    /// <summary>Distance over which the band blends into the wild terrain.</summary>
    [Export] public float SpineBlend { get; set; } = 10.0f;
    /// <summary>Gentle unevenness along the path itself, so feet have something to do.</summary>
    [Export] public float SpineBumpAmplitude { get; set; } = 0.12f;
    [Export] public float SpineBumpWavelength { get; set; } = 8.0f;
    /// <summary>Steepest the spine may get (checked by TerrainTests); below the walkable limit.</summary>
    [Export] public float SpineMaxSlope { get; set; } = 30.0f;
    /// <summary>Z where the run starts.</summary>
    [Export] public float SpawnZ { get; set; } = 0.0f;

    [ExportGroup("Summit (M12)")]
    /// <summary>The run starts on the summit: nothing near the start rises above it. Height of the summit above the start, in m.</summary>
    [Export] public float SummitRise { get; set; } = 0.5f;
    /// <summary>The summit plateau runs this far behind the start before the cliff, in m.</summary>
    [Export] public float SummitBack { get; set; } = 10.0f;
    /// <summary>The cliff behind the summit, in degrees: far too steep to climb, a slide into the abyss.</summary>
    [Export] public float SummitCliffSlope { get; set; } = 60.0f;
    /// <summary>Ahead and to the sides, nothing rises above a plane falling from the summit at this angle, in degrees.</summary>
    [Export] public float SummitCapSlope { get; set; } = 12.0f;
    /// <summary>A carrier this far below the summit, behind it, has fallen into the abyss and dies, in m.</summary>
    [Export] public float AbyssDrop { get; set; } = 25.0f;

    [ExportGroup("Stations")]
    /// <summary>Metres of descent between stations (one day's stage).</summary>
    [Export] public float StationDepthInterval { get; set; } = 600.0f;
    /// <summary>Radius of a station's level terrace, in m.</summary>
    [Export] public float ClearingRadius { get; set; } = 8.0f;
    /// <summary>Length of the ramp blending the terrace into the slope, in m.</summary>
    [Export] public float ClearingBlend { get; set; } = 70.0f;
    /// <summary>The terrace keeps a gentle tilt down the mountain, in degrees.</summary>
    [Export] public float ClearingSlope { get; set; } = 10.0f;
    /// <summary>Stations worked out when the mountain is generated; more are added as needed.</summary>
    [Export] public int PrecomputedStations { get; set; } = 16;

    [ExportGroup("Routes (M10)")]
    /// <summary>Chance a stage gets both alternatives (one each side of the main path) instead of one.</summary>
    [Export] public float BothBranchesChance { get; set; } = 0.35f;
    /// <summary>Chance a single alternative is hazardous rather than supply-rich.</summary>
    [Export] public float HazardousChance { get; set; } = 0.5f;
    /// <summary>How far a branch swings away from the main path (min, max), in m.</summary>
    [Export] public Vector2 BranchOffset { get; set; } = new(45.0f, 70.0f);
    /// <summary>Branches leave this far (along Z) below a station and rejoin this far above the next, in m.</summary>
    [Export] public float BranchStationGap { get; set; } = 110.0f;
    /// <summary>Fraction of a branch spent swinging out, and again swinging back.</summary>
    [Export] public float BranchRamp { get; set; } = 0.2f;
    /// <summary>Stages whose branch zone would be shorter than this (along Z) get no branches, in m.</summary>
    [Export] public float MinBranchLength { get; set; } = 400.0f;
    /// <summary>Half width of a hazardous path's band: narrow.</summary>
    [Export] public float HazardHalfWidth { get; set; } = 2.5f;
    /// <summary>Rough, broken ground along a hazardous path.</summary>
    [Export] public float HazardBumpAmplitude { get; set; } = 0.35f;
    [Export] public float HazardBumpWavelength { get; set; } = 4.0f;
    /// <summary>Gap kept between obstacles and a hazardous band (they crowd it), in m.</summary>
    [Export] public float HazardClearance { get; set; } = 0.4f;
    /// <summary>Within a branch zone the safe path winds like a real trail (so the hazardous line is a shortcut): swing and wavelength, in m.</summary>
    [Export] public float SafeWindingAmplitude { get; set; } = 10.0f;
    [Export] public float SafeWindingWavelength { get; set; } = 110.0f;
    [Export] public float SupplyHalfWidth { get; set; } = 4.0f;
    /// <summary>A supply-rich path winds: sideways swing and its wavelength, in m.</summary>
    [Export] public float SupplyMeanderAmplitude { get; set; } = 12.0f;
    [Export] public float SupplyMeanderWavelength { get; set; } = 90.0f;

    [ExportGroup("Surfaces")]
    [Export] public float RockSlope { get; set; } = 38.0f;
    [Export] public float ScreeSlope { get; set; } = 28.0f;
    [Export] public float MudMaxSlope { get; set; } = 12.0f;
    /// <summary>Snow where its noise is above this (-1..1).</summary>
    [Export] public float SnowThreshold { get; set; } = 0.35f;
    [Export] public float MudThreshold { get; set; } = 0.4f;
    [Export] public float SurfaceNoiseWavelength { get; set; } = 25.0f;
    /// <summary>Random brightness change per triangle, for the low-poly faceted look.</summary>
    [Export] public float ColorJitter { get; set; } = 0.07f;
    [Export(PropertyHint.File, "*.tres")] public string RockPath { get; set; } = "res://terrain/surfaces/rock.tres";
    [Export(PropertyHint.File, "*.tres")] public string ScreePath { get; set; } = "res://terrain/surfaces/scree.tres";
    [Export(PropertyHint.File, "*.tres")] public string GrassPath { get; set; } = "res://terrain/surfaces/grass.tres";
    [Export(PropertyHint.File, "*.tres")] public string SnowPath { get; set; } = "res://terrain/surfaces/snow.tres";
    [Export(PropertyHint.File, "*.tres")] public string MudPath { get; set; } = "res://terrain/surfaces/mud.tres";

    [ExportGroup("Obstacles")]
    /// <summary>Boulders per 1000 m² of chunk (off the spine).</summary>
    [Export] public float BoulderDensity { get; set; } = 8.0f;
    [Export] public float TrunkDensity { get; set; } = 1.5f;
    /// <summary>Smallest and largest boulder edge.</summary>
    [Export] public Vector2 BoulderSize { get; set; } = new(0.6f, 2.4f);
    /// <summary>Fraction of a boulder's height sunk into the ground.</summary>
    [Export] public float BoulderSink { get; set; } = 0.3f;
    [Export] public float BoulderMaxTilt { get; set; } = 25.0f;
    [Export] public Vector2 TrunkLength { get; set; } = new(3.0f, 7.0f);
    [Export] public float TrunkRadius { get; set; } = 0.25f;
    /// <summary>Extra gap kept between obstacles and the spine band.</summary>
    [Export] public float SpineClearance { get; set; } = 2.0f;
    [Export(PropertyHint.File, "*.tres")] public string BoulderSurfacePath { get; set; } = "res://terrain/surfaces/rock.tres";
    /// <summary>Models drawn for boulders (fitted to each collision box); empty draws boxes.</summary>
    [Export] public string[] BoulderModels { get; set; } =
    {
        "res://art/props/kenney_nature_kit/GLB format/rock_largeA.glb",
        "res://art/props/kenney_nature_kit/GLB format/rock_largeB.glb",
        "res://art/props/kenney_nature_kit/GLB format/rock_largeC.glb",
        "res://art/props/kenney_nature_kit/GLB format/rock_largeD.glb",
        "res://art/props/kenney_nature_kit/GLB format/rock_largeE.glb",
        "res://art/props/kenney_nature_kit/GLB format/rock_largeF.glb",
        "res://art/props/kenney_nature_kit/GLB format/stone_largeA.glb",
        "res://art/props/kenney_nature_kit/GLB format/stone_largeC.glb",
        "res://art/props/kenney_nature_kit/GLB format/stone_largeE.glb",
    };
    /// <summary>Models drawn for fallen trunks (their long side along the trunk).</summary>
    [Export] public string[] TrunkModels { get; set; } =
    {
        "res://art/props/kenney_nature_kit/GLB format/log.glb",
        "res://art/props/kenney_nature_kit/GLB format/log_large.glb",
    };
    [Export(PropertyHint.File, "*.tres")] public string TrunkSurfacePath { get; set; } = "res://terrain/surfaces/wood.tres";

    public float ChunkSize => ChunkCells * CellSize;
}
