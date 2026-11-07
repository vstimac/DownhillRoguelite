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
    /// <summary>Tree height range, in m.</summary>
    [Export] public Vector2 TreeHeight { get; set; } = new(4.0f, 8.0f);
    /// <summary>How far trees lean, at most, in degrees (windswept circles bend them).</summary>
    [Export] public float TreeLean { get; set; } = 3.0f;
    [Export] public string[] GrassModels { get; set; } = System.Array.Empty<string>();
    /// <summary>Grass tufts per m² on grassy ground.</summary>
    [Export] public float GrassDensity { get; set; }
    [Export] public Vector2 GrassScale { get; set; } = new(3.0f, 5.5f);
    [Export] public string[] FlowerModels { get; set; } = System.Array.Empty<string>();
    /// <summary>Flowers per m² on grassy ground.</summary>
    [Export] public float FlowerDensity { get; set; }

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
