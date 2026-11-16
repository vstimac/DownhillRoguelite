namespace DownhillRoguelite.Terrain;

/// <summary>Ground kinds the generator assigns; TerrainSettings maps each to a SurfaceType resource.</summary>
public enum TerrainSurface
{
    Rock,
    Scree,
    Grass,
    Snow,
    Mud,
    /// <summary>
    /// Bare earth: a worn path's trodden middle, bare patches, a terrace with no grass on it. Only
    /// underfoot (footsteps, grip): the ground's look comes from the other kinds plus wear and bareness.
    /// </summary>
    Dirt,
}
