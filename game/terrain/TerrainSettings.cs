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
    /// <summary>Continuous lighting and vertex surface colours; does not change the height grid or collision.</summary>
    [Export] public bool SmoothShading { get; set; } = true;

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
    /// <summary>A level pad on the summit plateau for the old car and its bench: this far to the side of the route (negative: left), in m. Its blend keeps clear of the path and the spawn (more than SpineHalfWidth + radius + blend).</summary>
    [Export] public float SummitPadSide { get; set; } = -12.0f;
    /// <summary>...and this far behind the start (on the plateau, before the cliff), in m.</summary>
    [Export] public float SummitPadBehind { get; set; } = 5.0f;
    /// <summary>Radius of the pad's level ground, in m (0: no pad). Nothing grows or lies on it.</summary>
    [Export] public float SummitPadRadius { get; set; } = 4.5f;
    /// <summary>Width of the ring blending the pad into the ground around it, in m.</summary>
    [Export] public float SummitPadBlend { get; set; } = 3.0f;

    [ExportGroup("Stations")]
    /// <summary>Metres of descent between stations (one day's stage).</summary>
    [Export] public float StationDepthInterval { get; set; } = 350.0f;
    /// <summary>Radius of a station's level terrace, in m (wide enough for the fire, shelter and graves).</summary>
    [Export] public float ClearingRadius { get; set; } = 11.0f;
    /// <summary>Length of the ramp blending the terrace into the slope, in m.</summary>
    [Export] public float ClearingBlend { get; set; } = 70.0f;
    /// <summary>The ramps in and out of the level terrace keep this gentle tilt down the mountain, in degrees.</summary>
    [Export] public float ClearingSlope { get; set; } = 14.0f;
    /// <summary>Stations worked out when the mountain is generated; more are added as needed.</summary>
    [Export] public int PrecomputedStations { get; set; } = 16;

    [ExportGroup("Routes (M10)")]
    /// <summary>Chance a stage gets both alternatives (one each side of the main path) instead of one.</summary>
    [Export] public float BothBranchesChance { get; set; } = 0.6f;
    /// <summary>Chance a single alternative is hazardous rather than supply-rich.</summary>
    [Export] public float HazardousChance { get; set; } = 0.5f;
    /// <summary>How far a branch swings away from the main path (min, max), in m.</summary>
    [Export] public Vector2 BranchOffset { get; set; } = new(45.0f, 70.0f);
    /// <summary>
    /// Chance, per side, of a second, wider path beyond the first: the long way round (supply-rich),
    /// leaving and rejoining with the others, so the trail forks once more just past the split.
    /// </summary>
    [Export] public float OuterBranchChance { get; set; } = 0.45f;
    /// <summary>How far an outer path swings from the main path (min, max), in m: clear of the inner paths' bands.</summary>
    [Export] public Vector2 OuterBranchOffset { get; set; } = new(130.0f, 155.0f);
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
    /// <summary>
    /// How far either side of SnowThreshold (in the snow noise's own units) the snow thins out to
    /// nothing or deepens to full, for the ground's look only: wider, and a patch's edge is a broader,
    /// more ragged band. Where the carrier slips is still decided at the threshold itself.
    /// </summary>
    [Export] public float SnowEdgeBand { get; set; } = 0.09f;
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

    private const string Kit = "res://art/props/kenney_nature_kit/GLB format/";

    /// <summary>The land circle by circle (one biome per circle, 1050 m of descent), blended over BiomeBlend metres.</summary>
    [Export] public Biome[] Biomes { get; set; } = DefaultBiomes();
    /// <summary>Width of the band where one biome turns into the next, in m of depth.</summary>
    [Export] public float BiomeBlend { get; set; } = 240.0f;
    /// <summary>Grass and flowers are drawn this far from the camera, in m.</summary>
    [Export] public float GrassViewDistance { get; set; } = 45.0f;
    /// <summary>Grass blades' colour before the biome's tint (a little deeper than the grassy ground).</summary>
    [Export] public Color GrassColor { get; set; } = new(0.5f, 0.62f, 0.4f);
    /// <summary>Trees keep this far from stations' fires, in m (on top of the clearing).</summary>
    [Export] public float TreeStationGap { get; set; } = 6.0f;
    /// <summary>Trees don't grow on ground steeper than this, in degrees.</summary>
    [Export] public float TreeMaxSlope { get; set; } = 32.0f;
    /// <summary>How far a tree's trunk sinks into the ground, as a share of its height (0.10 = a tenth).</summary>
    [Export] public float TreeSink { get; set; } = 0.10f;

    [ExportGroup("Forest detail")]
    [Export] public float PlantBatchSize { get; set; } = 16.0f;
    /// <summary>Shared runtime foliage budget, leaving room for physics, rendering and collection pauses.</summary>
    [Export] public double PlantBuildBudgetMilliseconds { get; set; } = 1.0;
    /// <summary>Maximum grouping/transform work before the shared plant time budget is checked again.</summary>
    [Export(PropertyHint.Range, "1,512,1")] public int PlantWorkItemsPerSlice { get; set; } = 128;
    /// <summary>The same budget behind the loading screen, where a long frame doesn't matter.</summary>
    [Export] public double LoadingPlantBudgetMilliseconds { get; set; } = 40.0;
    [Export] public float TreeViewDistance { get; set; } = 220.0f;
    /// <summary>Plant view distances are multiplied by this on Low, Medium and High quality (Options).</summary>
    [Export] public Vector3 QualityViewScale { get; set; } = new(0.55f, 0.8f, 1.0f);
    /// <summary>Trees fade out over this last stretch of their view distance instead of popping away, in m.</summary>
    [Export] public float TreeFadeMargin { get; set; } = 30.0f;
    [Export] public float ForestClusterWavelength { get; set; } = 45.0f;
    [Export] public float PebbleDensity { get; set; } = 0.12f;
    [Export] public Vector2 PebbleHeight { get; set; } = new(0.025f, 0.09f);
    [Export] public float PebbleViewDistance { get; set; } = 30.0f;
    [Export] public float DetailMaxSlope { get; set; } = 40.0f;
    [Export] public string[] PebbleModels { get; set; } =
    {
        Kit + "stone_smallA.glb", Kit + "stone_smallB.glb", Kit + "stone_smallC.glb",
    };

    [ExportGroup("Ground cover")]
    /// <summary>Shrubs, rocks and debris are batched in cells this wide (sparser than grass, so fewer draws).</summary>
    [Export] public float GroundCoverBatchSize { get; set; } = 64.0f;
    [Export] public float ShrubViewDistance { get; set; } = 80.0f;
    [Export] public float RockViewDistance { get; set; } = 70.0f;
    [Export] public float DebrisViewDistance { get; set; } = 45.0f;
    /// <summary>Wavelength of the thickets shrubs grow in and the meadows flowers gather in, in m.</summary>
    [Export] public float ThicketWavelength { get; set; } = 28.0f;
    /// <summary>Loose rock height range, in m (a few grow into knee-high outcrops).</summary>
    [Export] public Vector2 RockHeight { get; set; } = new(0.12f, 0.7f);
    /// <summary>Every circle's shrub density (Biome.ShrubDensity) times this: how bushy the whole mountain is.</summary>
    [Export] public float ShrubScatter { get; set; } = 1.6f;
    /// <summary>Every circle's loose-rock density (Biome.RockDensity) times this: how stony the whole mountain is.</summary>
    [Export] public float RockScatter { get; set; } = 3.0f;
    /// <summary>Loose rocks are this many times as thick on scree and rocky ground.</summary>
    [Export] public float RockySurfaceRockFactor { get; set; } = 5.0f;
    /// <summary>Broken pieces lying round each boulder's foot (most and least).</summary>
    [Export] public Vector2I BoulderFragments { get; set; } = new(1, 4);
    [Export] public string[] RockModels { get; set; } =
    {
        Kit + "rock_smallA.glb", Kit + "rock_smallC.glb", Kit + "rock_smallFlatA.glb", Kit + "rock_tallB.glb", Kit + "stone_smallF.glb",
    };
    /// <summary>The grass carpet (terrain/grass_carpet.gdshader): clumps this far apart, in m, ...</summary>
    [Export] public float CarpetSpacing { get; set; } = 0.36f;
    /// <summary>...their tallest blades, in m...</summary>
    [Export] public float CarpetBladeHeight { get; set; } = 0.24f;
    /// <summary>...and how far from the camera they are drawn, in m (scaled by QualityViewScale; 0: no carpet).</summary>
    [Export] public float CarpetViewDistance { get; set; } = 30.0f;
    /// <summary>Fallen-log length range, in m (solid, vaultable, like the obstacles' trunks).</summary>
    [Export] public Vector2 FallenLogLength { get; set; } = new(2.5f, 5.5f);

    [ExportGroup("Roadside and outcrops (second round)")]
    /// <summary>Bushes beside the paths' edges: this many times the biome's ShrubDensity, over the band below (0: none).</summary>
    [Export] public float RoadsideShrubScatter { get; set; } = 2.5f;
    /// <summary>Loose stones beside the paths' edges: this many times the biome's RockDensity, over the band below (0: none).</summary>
    [Export] public float RoadsideRockScatter { get; set; } = 2.0f;
    /// <summary>How far from the path's edge the roadside cover reaches, in m (range: nearest and furthest).</summary>
    [Export] public Vector2 RoadsideReach { get; set; } = new(0.8f, 7.0f);
    /// <summary>Rock outcrops tried per 1000 m² of ground; a try only lands where the slope is inside OutcropSlope.</summary>
    [Export] public float OutcropDensity { get; set; } = 4.0f;
    /// <summary>Ground slope, in degrees, where outcrops may stand (range).</summary>
    [Export] public Vector2 OutcropSlope { get; set; } = new(14.0f, 38.0f);
    /// <summary>Height of an outcrop's main rock, in m (range; a few are knee-high to waist-high).</summary>
    [Export] public Vector2 OutcropHeight { get; set; } = new(0.8f, 1.8f);
    /// <summary>Smaller stones lying round an outcrop's foot (most and least).</summary>
    [Export] public Vector2I OutcropStones { get; set; } = new(2, 4);

    [ExportGroup("Mountain relief (third round)")]
    /// <summary>How high the ground climbs away from the paths into flanking ridges, in m (times the circle's Relief).</summary>
    [Export] public float FlankHeight { get; set; } = 34.0f;
    /// <summary>The flanks start rising this far from the nearest path's centre, in m...</summary>
    [Export] public float FlankStart { get; set; } = 15.0f;
    /// <summary>...and reach their full height this much further out, in m.</summary>
    [Export] public float FlankRise { get; set; } = 42.0f;
    /// <summary>Spacing of the ridge crests and gullies along the flanks, in m.</summary>
    [Export] public float RidgeWavelength { get; set; } = 75.0f;
    /// <summary>Rough crags on the flanks (sharp small ridges), in m.</summary>
    [Export] public float CragAmplitude { get; set; } = 4.0f;
    [Export] public float CragWavelength { get; set; } = 16.0f;
    /// <summary>Along the descent the valley opens and closes over this many metres (open stretches give views).</summary>
    [Export] public float ValleyWavelength { get; set; } = 420.0f;
    /// <summary>Bare earth and gravel patches: their size, in m, and how much of the open ground they take (0 none, 1 most).</summary>
    [Export] public float BareWavelength { get; set; } = 22.0f;
    [Export(PropertyHint.Range, "0,1")] public float BareShare { get; set; } = 0.5f;
    /// <summary>A worn path: the colour of its trodden earth and how strongly it shows.</summary>
    [Export] public Color PathColour { get; set; } = new(0.5f, 0.4f, 0.28f);
    [Export(PropertyHint.Range, "0,1")] public float PathWear { get; set; } = 0.75f;
    /// <summary>Bare earth (low, warm) and gravel (high, grey): the colours bare patches take.</summary>
    [Export] public Color BareEarthColour { get; set; } = new(0.42f, 0.33f, 0.23f);
    [Export] public Color GravelColour { get; set; } = new(0.47f, 0.46f, 0.44f);
    /// <summary>Boulder groups round one big "hero" rock, per 1000 m² (times the circle's Relief).</summary>
    [Export] public float BoulderClusterDensity { get; set; } = 0.75f;
    /// <summary>The hero rock's size range, in m, and the smaller rocks round it (most and least).</summary>
    [Export] public Vector2 HeroBoulderSize { get; set; } = new(3.5f, 7.0f);
    [Export] public Vector2I ClusterBoulders { get; set; } = new(3, 6);
    /// <summary>Share of the groups (and the lone trees) set within sight of the main path, this far from its centre, in m.</summary>
    [Export(PropertyHint.Range, "0,1")] public float GroupNearPathShare { get; set; } = 0.6f;
    [Export] public Vector2 GroupSightDistance { get; set; } = new(11.0f, 42.0f);
    /// <summary>Share of hero rocks that are tall standing spires instead, and their height in m.</summary>
    [Export(PropertyHint.Range, "0,1")] public float SpireShare { get; set; } = 0.3f;
    [Export] public Vector2 SpireHeight { get; set; } = new(6.0f, 11.0f);
    /// <summary>Share of the scattered boulders kept alongside the groups (the rest gather into them).</summary>
    [Export(PropertyHint.Range, "0,1")] public float ScatteredBoulderShare { get; set; } = 0.55f;
    /// <summary>A lone dead tree on open ground, chance per chunk, and its height range in m.</summary>
    [Export(PropertyHint.Range, "0,1")] public float LoneTreeChance { get; set; } = 0.6f;
    [Export] public Vector2 LoneTreeHeight { get; set; } = new(9.0f, 14.0f);

    [ExportGroup("Flank trees and summit snow (fourth round)")]
    /// <summary>Tree tries per 1000 m² on the valley's flanks (kept by the circle's own tree density, in groves).</summary>
    [Export] public float FlankTreeDensity { get; set; } = 12.0f;
    /// <summary>Flank trees grow only up to this slope, in degrees (the gentler parts and the crests).</summary>
    [Export] public float FlankTreeMaxSlope { get; set; } = 31.0f;
    /// <summary>Size of the groves along the flanks, in m.</summary>
    [Export] public float FlankGroveWavelength { get; set; } = 34.0f;
    /// <summary>How much lower the snow line lies on the flanks near the summit (on the snow noise's -1..1 scale; 0 none).</summary>
    [Export] public float SummitSnow { get; set; } = 0.45f;
    /// <summary>Summit snow at full strength above the first depth, gone below the second, in m.</summary>
    [Export] public Vector2 SummitSnowDepth { get; set; } = new(250.0f, 1000.0f);

    public static Biome[] DefaultBiomes()
    {
        Biome[] biomes = new[]
        {
            ForestBiome(),
            Biome.Make("Lust", 1050.0f, new Color(1.0f, 0.96f, 0.86f), Trees("broadleaf_green_0", "broadleaf_green_1", "broadleaf_green_2"), 2.0f, new Vector2(4.0f, 7.0f), 14.0f,
                new[] { Kit + "grass_large.glb", Kit + "grass_leafs.glb" }, 0.5f, new[] { Kit + "flower_redA.glb", Kit + "flower_redB.glb", Kit + "flower_redC.glb" }, 0.04f),
            Biome.Make("Gluttony", 2100.0f, new Color(0.78f, 0.9f, 0.72f), Trees("broadleaf_dark_0", "broadleaf_dark_1", "broadleaf_dark_2"), 6.0f, new Vector2(5.0f, 9.0f), 4.0f,
                new[] { Kit + "grass_leafs.glb", Kit + "grass_leafsLarge.glb" }, 0.55f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Greed", 3150.0f, new Color(1.06f, 0.95f, 0.78f), Trees("broadleaf_fall_0", "broadleaf_fall_1", "broadleaf_fall_2"), 3.0f, new Vector2(4.5f, 8.0f), 3.0f,
                new[] { Kit + "grass.glb", Kit + "grass_large.glb" }, 0.25f, new[] { Kit + "flower_yellowA.glb", Kit + "flower_yellowB.glb", Kit + "flower_yellowC.glb" }, 0.04f),
            Biome.Make("Wrath", 4200.0f, new Color(0.7f, 0.78f, 0.7f), Trees("snag_0", "snag_1", "broadleaf_dark_1"), 3.0f, new Vector2(4.0f, 7.0f), 8.0f,
                new[] { Kit + "grass_leafs.glb", Kit + "grass_leafsLarge.glb" }, 0.3f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Heresy", 5250.0f, new Color(1.1f, 0.94f, 0.8f), Trees("broadleaf_fall_2", "snag_0", "snag_1"), 0.8f, new Vector2(3.5f, 6.0f), 6.0f,
                new[] { Kit + "grass.glb", Kit + "grass_large.glb" }, 0.08f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Violence", 6300.0f, new Color(1.05f, 0.85f, 0.76f), Trees("snag_0", "snag_1"), 0.3f, new Vector2(3.0f, 5.0f), 10.0f,
                new[] { Kit + "grass.glb", Kit + "grass_large.glb" }, 0.05f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Fraud", 7350.0f, new Color(0.86f, 0.86f, 0.95f), Trees("pine_0", "pine_1", "pine_2"), 2.0f, new Vector2(3.5f, 7.0f), 4.0f,
                new[] { Kit + "grass.glb", Kit + "grass_large.glb" }, 0.2f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Treachery", 8400.0f, new Color(0.9f, 0.95f, 1.1f), System.Array.Empty<string>(), 0.0f, new Vector2(3.0f, 5.0f), 0.0f,
                System.Array.Empty<string>(), 0.0f, System.Array.Empty<string>(), 0.0f),
            Biome.Make("Beyond", 9450.0f, new Color(0.86f, 0.8f, 0.96f), new[] { Kit + "tree_blocks_dark.glb", Kit + "tree_blocks.glb", Kit + "tree_plateau_dark.glb" }, 1.5f, new Vector2(4.0f, 10.0f), 0.0f,
                new[] { Kit + "grass.glb", Kit + "grass_large.glb" }, 0.1f, System.Array.Empty<string>(), 0.0f),
        };
        foreach (Biome biome in biomes)
        {
            // Leaves drift down where broadleaf trees grow: thick in the autumn circles.
            switch (biome.Name)
            {
                case "Limbo":
                    biome.Fireflies = 0.6f;
                    break;
                case "Lust":
                    biome.Fireflies = 1.0f;
                    biome.LeafFall = 0.15f;
                    biome.LeafColour = new Color(0.45f, 0.6f, 0.22f);
                    break;
                case "Gluttony":
                    biome.Fireflies = 0.7f;
                    biome.LeafFall = 0.25f;
                    biome.LeafColour = new Color(0.4f, 0.48f, 0.18f);
                    break;
                case "Greed":
                    biome.Fireflies = 0.3f;
                    biome.LeafFall = 1.0f;
                    biome.LeafColour = new Color(0.82f, 0.45f, 0.14f);
                    break;
                case "Heresy":
                    biome.LeafFall = 0.5f;
                    biome.LeafColour = new Color(0.72f, 0.3f, 0.12f);
                    break;
            }
            ApplyGroundCover(biome);
            ApplyRelief(biome);
            // Every circle grows the generated grass tufts (they bend in the wind), keeping its own
            // leafy plants for variety; all ground plants are then fitted in metres.
            if (biome.GrassModels.Length > 0 && !biome.GroundPlantsInMetres)
            {
                System.Collections.Generic.List<string> grass = new(Trees("grass_0", "grass_1", "grass_2"));
                foreach (string model in biome.GrassModels)
                {
                    if (model.Contains("grass_leafs"))
                    {
                        grass.Add(model);
                    }
                }
                biome.GrassModels = grass.ToArray();
                biome.GroundPlantsInMetres = true;
                biome.GrassScale = new Vector2(0.12f, 0.4f);
            }
            // The generated pines have slender trunks (as in the first circle's forest).
            if (biome.TreeModels.Length > 0 && System.Array.TrueForAll(biome.TreeModels, model => model.Contains("/pine_")))
            {
                biome.TrunkRadiusPerMetre = 0.0125f;
            }
        }
        return biomes;
    }

    /// <summary>
    /// How each circle's land is shaped (the third round): the high circles are alpine, rocky and
    /// steep-flanked; the green ones gentler and grassier; the low ones darker, rougher and denser.
    /// </summary>
    private static void ApplyRelief(Biome biome)
    {
        switch (biome.Name)
        {
            case "Limbo": biome.Relief = 1.25f; biome.Stoniness = 0.8f; break;
            case "Lust": biome.Relief = 1.0f; biome.Stoniness = 0.5f; break;
            case "Gluttony": biome.Relief = 0.8f; biome.Stoniness = 0.25f; break;
            case "Greed": biome.Relief = 0.95f; biome.Stoniness = 0.45f; break;
            case "Wrath":
                biome.Relief = 1.15f; biome.Stoniness = 0.6f;
                biome.TreeDensity = 4.0f;
                break;
            case "Heresy":
                biome.Relief = 1.05f; biome.Stoniness = 0.65f;
                biome.GroundTint = new Color(0.86f, 0.72f, 0.6f);
                biome.TreeDensity = 2.0f;
                break;
            case "Violence":
                biome.Relief = 1.25f; biome.Stoniness = 0.8f;
                biome.GroundTint = new Color(0.8f, 0.62f, 0.54f);
                biome.TreeDensity = 1.6f;
                break;
            case "Fraud":
                biome.Relief = 1.1f; biome.Stoniness = 0.55f;
                biome.GroundTint = new Color(0.66f, 0.68f, 0.74f);
                biome.TreeDensity = 4.0f;
                break;
            case "Treachery":
                biome.Relief = 1.4f; biome.Stoniness = 0.9f;
                biome.GroundTint = new Color(0.76f, 0.8f, 0.92f);
                break;
            case "Beyond": biome.Relief = 0.8f; biome.Stoniness = 0.4f; break;
        }
    }

    /// <summary>
    /// Each circle's ground cover: what grows under its trees and between them, what lies at the
    /// path's edge, how rocky it is and how many meadows it has. High, cold circles are rocky with
    /// dwarf pines; green ones have bushes, mushrooms and flowers; burnt and broken ones have stumps
    /// and stones; the lost carriers' things turn up by the path all the way down.
    /// </summary>
    private static void ApplyGroundCover(Biome biome)
    {
        const string survival = "res://art/items/kenney_survival_kit/GLB format/";
        const string graveyard = "res://art/props/kenney_graveyard_kit/GLB format/";
        string[] lostGear = { survival + "box-open.glb", survival + "bucket.glb", survival + "resource-wood.glb", survival + "bedroll-packed.glb", Kit + "stump_square.glb" };
        switch (biome.Name)
        {
            case "Limbo":
                biome.ShrubColour = new Color(0.2f, 0.3f, 0.23f);
                biome.ShrubModels = new[] { Kit + "tree_pineGroundA.glb", Kit + "tree_pineSmallB.glb", Kit + "plant_bushSmall.glb" };
                biome.ShrubDensity = 10.0f;
                biome.ShrubHeight = new Vector2(0.4f, 1.6f);
                biome.DebrisModels = new[] { Kit + "stump_old.glb", Kit + "mushroom_tanGroup.glb", Kit + "log.glb", Kit + "plant_flatShort.glb" };
                biome.DebrisDensity = 7.0f;
                biome.WaysideModels = new[] { Kit + "stump_round.glb", survival + "resource-wood.glb", graveyard + "cross-wood.glb", survival + "bucket.glb" };
                biome.WaysideDensity = 3.0f;
                biome.RockDensity = 9.0f;
                biome.MeadowFlowerDensity = 0.12f;
                biome.FallenLogDensity = 0.5f;
                break;
            case "Lust":
                biome.ShrubColour = new Color(0.33f, 0.46f, 0.22f);
                biome.ShrubModels = new[] { Kit + "plant_bush.glb", Kit + "plant_bushDetailed.glb", Kit + "plant_flatTall.glb" };
                biome.ShrubDensity = 14.0f;
                biome.DebrisModels = new[] { Kit + "mushroom_redGroup.glb", Kit + "stump_round.glb", Kit + "plant_flatShort.glb" };
                biome.DebrisDensity = 6.0f;
                biome.WaysideModels = lostGear;
                biome.WaysideDensity = 2.5f;
                biome.RockDensity = 5.0f;
                biome.MeadowFlowerDensity = 0.3f;
                biome.FallenLogDensity = 0.4f;
                break;
            case "Gluttony":
                biome.ShrubColour = new Color(0.22f, 0.34f, 0.18f);
                biome.ShrubModels = new[] { Kit + "plant_bushLarge.glb", Kit + "plant_bushDetailed.glb", Kit + "plant_flatTall.glb" };
                biome.ShrubDensity = 22.0f;
                biome.ShrubHeight = new Vector2(0.6f, 1.8f);
                biome.DebrisModels = new[] { Kit + "mushroom_redGroup.glb", Kit + "mushroom_tanTall.glb", Kit + "stump_oldTall.glb", Kit + "plant_flatShort.glb" };
                biome.DebrisDensity = 12.0f;
                biome.WaysideModels = lostGear;
                biome.WaysideDensity = 2.5f;
                biome.RockDensity = 4.0f;
                biome.MeadowFlowerDensity = 0.08f;
                biome.FallenLogDensity = 1.0f;
                biome.FlowerModels = new[] { Kit + "flower_purpleA.glb", Kit + "flower_purpleC.glb" };
                break;
            case "Greed":
                biome.ShrubColour = new Color(0.62f, 0.38f, 0.16f);
                biome.ShrubModels = new[] { Kit + "plant_bush.glb", Kit + "plant_bushTriangle.glb" };
                biome.ShrubDensity = 10.0f;
                biome.DebrisModels = new[] { Kit + "stump_round.glb", Kit + "mushroom_tanGroup.glb", Kit + "log.glb" };
                biome.DebrisDensity = 7.0f;
                biome.WaysideModels = lostGear;
                biome.WaysideDensity = 3.0f;
                biome.RockDensity = 6.0f;
                biome.MeadowFlowerDensity = 0.25f;
                biome.FallenLogDensity = 0.8f;
                break;
            case "Wrath":
                biome.ShrubColour = new Color(0.3f, 0.34f, 0.22f);
                biome.ShrubModels = new[] { Kit + "plant_bushTriangle.glb", Kit + "plant_bushSmall.glb" };
                biome.ShrubDensity = 7.0f;
                biome.DebrisModels = new[] { Kit + "stump_old.glb", Kit + "log.glb" };
                biome.DebrisDensity = 5.0f;
                biome.WaysideModels = new[] { graveyard + "debris-wood.glb", graveyard + "cross-wood.glb", survival + "box-open.glb", Kit + "stump_old.glb" };
                biome.WaysideDensity = 3.0f;
                biome.RockDensity = 12.0f;
                biome.MeadowFlowerDensity = 0.1f;
                biome.FallenLogDensity = 0.8f;
                biome.FlowerModels = new[] { Kit + "flower_redA.glb", Kit + "flower_yellowB.glb" };
                break;
            case "Heresy":
            case "Violence":
                biome.ShrubColour = new Color(0.42f, 0.3f, 0.2f);
                biome.ShrubModels = new[] { Kit + "plant_bushTriangle.glb", Kit + "plant_bushSmall.glb" };
                biome.ShrubDensity = 4.0f;
                biome.ShrubHeight = new Vector2(0.4f, 0.9f);
                biome.DebrisModels = new[] { Kit + "stump_oldTall.glb", Kit + "stump_old.glb", Kit + "log.glb" };
                biome.DebrisDensity = 6.0f;
                biome.WaysideModels = new[] { graveyard + "debris-wood.glb", graveyard + "gravestone-debris.glb", graveyard + "cross-wood.glb", graveyard + "shovel.glb" };
                biome.WaysideDensity = 3.5f;
                biome.RockDensity = 16.0f;
                biome.FallenLogDensity = 0.6f;
                break;
            case "Fraud":
                biome.ShrubColour = new Color(0.2f, 0.28f, 0.25f);
                biome.ShrubModels = new[] { Kit + "tree_pineGroundB.glb", Kit + "tree_pineSmallD.glb", Kit + "plant_bushSmall.glb" };
                biome.ShrubDensity = 9.0f;
                biome.ShrubHeight = new Vector2(0.4f, 1.6f);
                biome.DebrisModels = new[] { Kit + "stump_old.glb", Kit + "mushroom_tan.glb", Kit + "log.glb" };
                biome.DebrisDensity = 5.0f;
                biome.WaysideModels = lostGear;
                biome.WaysideDensity = 3.0f;
                biome.RockDensity = 12.0f;
                biome.MeadowFlowerDensity = 0.08f;
                biome.FallenLogDensity = 0.5f;
                biome.FlowerModels = new[] { Kit + "flower_purpleB.glb", Kit + "flower_yellowA.glb" };
                break;
            case "Treachery":
                biome.DebrisModels = new[] { graveyard + "debris.glb" };
                biome.DebrisDensity = 1.5f;
                biome.WaysideModels = new[] { graveyard + "gravestone-debris.glb", survival + "box-open.glb" };
                biome.WaysideDensity = 2.0f;
                biome.RockDensity = 18.0f;
                break;
            case "Beyond":
                biome.ShrubColour = new Color(0.42f, 0.33f, 0.5f);
                biome.ShrubModels = new[] { Kit + "plant_bushLargeTriangle.glb", Kit + "plant_bushTriangle.glb" };
                biome.ShrubDensity = 8.0f;
                biome.DebrisModels = new[] { Kit + "mushroom_redTall.glb", Kit + "mushroom_tanTall.glb" };
                biome.DebrisDensity = 4.0f;
                biome.WaysideModels = lostGear;
                biome.WaysideDensity = 2.0f;
                biome.RockDensity = 8.0f;
                break;
        }
    }

    public float ChunkSize => ChunkCells * CellSize;

    /// <summary>Generated trees (tools/forest/generate_forest.gd: pines, broadleaf, snags), by name.</summary>
    private static string[] Trees(params string[] names)
    {
        string[] paths = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            paths[i] = "res://art/environment/forest/" + names[i] + ".tscn";
        }
        return paths;
    }

    private static Biome ForestBiome()
    {
        const string forest = "res://art/environment/forest/";
        Biome biome = Biome.Make("Limbo", 0, new Color(0.88f, 0.94f, 0.83f),
            new[] { forest + "pine_0.tscn", forest + "pine_1.tscn", forest + "pine_2.tscn" },
            22, new Vector2(10, 18), 2,
            new[] { forest + "grass_0.tscn", forest + "grass_1.tscn", forest + "grass_2.tscn" },
            0.7f, new[] { Kit + "flower_purpleA.glb", Kit + "flower_purpleB.glb" }, 0.015f);
        biome.GroundPlantsInMetres = true;
        biome.GrassScale = new Vector2(0.12f, 0.4f);
        biome.TrunkRadiusPerMetre = 0.0125f;
        biome.ClusterTrees = true;
        biome.SaplingShare = 0.15f;
        return biome;
    }
}
