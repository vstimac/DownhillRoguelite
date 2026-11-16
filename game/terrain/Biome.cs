using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// How the land looks over one stretch of the descent (one per circle): a tint over the ground's
/// colours, the trees that grow there (models, how many, how tall, how much the wind bends them),
/// the grass and the flowers. Biomes start at a depth below the summit; the generator blends one into
/// the next over TerrainSettings.BiomeBlend metres, the colours smoothly and the plants in
/// interleaving patches.
/// </summary>
[GlobalClass]
public partial class Biome : Resource
{
    [Export] public string Name { get; set; } = "";
    /// <summary>Where it begins, in metres below the summit.</summary>
    [Export] public float FromDepth { get; set; }
    /// <summary>Multiplies the ground's grass and mud colours (snow and rock get half of it).</summary>
    [Export] public Color GroundTint { get; set; } = Colors.White;
    [Export] public string[] TreeModels { get; set; } = System.Array.Empty<string>();
    /// <summary>Trees per 1000 m² on open ground.</summary>
    [Export] public float TreeDensity { get; set; }
    [Export] public bool ClusterTrees { get; set; }
    [Export(PropertyHint.Range, "0,1")] public float SaplingShare { get; set; }
    [Export] public Vector2 SaplingHeight { get; set; } = new(2.5f, 5.0f);
    [Export] public float TrunkRadiusPerMetre { get; set; } = 0.035f;
    /// <summary>Tree height range, in m.</summary>
    [Export] public Vector2 TreeHeight { get; set; } = new(4.0f, 8.0f);
    /// <summary>How far trees lean, at most, in degrees (windswept circles bend them).</summary>
    [Export] public float TreeLean { get; set; } = 3.0f;
    [Export] public string[] GrassModels { get; set; } = System.Array.Empty<string>();
    /// <summary>Grass tufts per m² on grassy ground.</summary>
    [Export] public float GrassDensity { get; set; }
    [Export] public Vector2 GrassScale { get; set; } = new(3.0f, 5.5f);
    /// <summary>GrassScale is height in metres, fitted against mesh bounds; false retains legacy asset scaling.</summary>
    [Export] public bool GroundPlantsInMetres { get; set; }
    [Export] public string[] FlowerModels { get; set; } = System.Array.Empty<string>();
    /// <summary>Flowers per m² on grassy ground.</summary>
    [Export] public float FlowerDensity { get; set; }
    /// <summary>How thick leaves fall round the carrier (0 none, 1 an autumn wood; environment/FallingLeaves).</summary>
    [Export(PropertyHint.Range, "0,1")] public float LeafFall { get; set; }
    [Export] public Color LeafColour { get; set; } = new(0.8f, 0.45f, 0.15f);
    /// <summary>How many fireflies come out on dry nights (0 none, 1 a meadow full; environment/Fireflies).</summary>
    [Export(PropertyHint.Range, "0,1")] public float Fireflies { get; set; }

    /// <summary>How mountainous the circle is: scales the flanking ridges, the boulder groups and the bare ground (1 as set in TerrainSettings).</summary>
    [Export] public float Relief { get; set; } = 1.0f;
    /// <summary>How much bare stone shows instead of grass (0 a green circle, 1 a stony one).</summary>
    [Export(PropertyHint.Range, "0,1")] public float Stoniness { get; set; } = 0.5f;

    [ExportGroup("Ground cover")]
    /// <summary>Bushes and dwarf trees (no collision), per 1000 m², grown in thickets.</summary>
    [Export] public string[] ShrubModels { get; set; } = System.Array.Empty<string>();
    [Export] public float ShrubDensity { get; set; }
    /// <summary>Shrub height range, in m.</summary>
    [Export] public Vector2 ShrubHeight { get; set; } = new(0.5f, 1.3f);
    /// <summary>Shrubs' foliage colour, so the art pack's bushes match the circle's trees and grass.</summary>
    [Export] public Color ShrubColour { get; set; } = new(0.3f, 0.42f, 0.24f);
    /// <summary>Forest-floor debris (stumps, mushrooms, branches), per 1000 m², most of it at trees' feet.</summary>
    [Export] public string[] DebrisModels { get; set; } = System.Array.Empty<string>();
    [Export] public float DebrisDensity { get; set; }
    /// <summary>Things lying by the path's edge (stumps, cut wood, lost gear), per 100 m of path and side.</summary>
    [Export] public string[] WaysideModels { get; set; } = System.Array.Empty<string>();
    [Export] public float WaysideDensity { get; set; }
    /// <summary>Loose rocks on open ground, per 1000 m² (scree and rocky slopes get several times more).</summary>
    [Export] public float RockDensity { get; set; } = 6.0f;
    /// <summary>Flowers per m² inside meadow patches (on top of FlowerDensity, which is spread thin everywhere).</summary>
    [Export] public float MeadowFlowerDensity { get; set; }
    /// <summary>Fallen, solid logs per 1000 m² where trees grow.</summary>
    [Export] public float FallenLogDensity { get; set; }

    public static Biome Make(string name, float from, Color tint, string[] trees, float treeDensity, Vector2 treeHeight, float lean,
        string[] grass, float grassDensity, string[] flowers, float flowerDensity)
    {
        return new Biome
        {
            Name = name,
            FromDepth = from,
            GroundTint = tint,
            TreeModels = trees,
            TreeDensity = treeDensity,
            TreeHeight = treeHeight,
            TreeLean = lean,
            GrassModels = grass,
            GrassDensity = grassDensity,
            FlowerModels = flowers,
            FlowerDensity = flowerDensity,
        };
    }
}
