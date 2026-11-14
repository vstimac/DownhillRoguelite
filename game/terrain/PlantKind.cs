namespace DownhillRoguelite.Terrain;

/// <summary>
/// What a plant is (trees stand in the way; everything else is only seen). Shrubs, rocks and
/// debris are the ground cover: bushes and dwarf pines, loose rocks and scree, stumps, mushrooms
/// and things left by the path. They are drawn further than grass and batched in larger cells.
/// </summary>
public enum PlantKind
{
    Tree,
    Grass,
    Flower,
    Pebble,
    Shrub,
    Rock,
    Debris,
}
