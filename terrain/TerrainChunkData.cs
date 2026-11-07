using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// Everything needed to build one chunk, computed off the main thread: the height grid for
/// collision, flat-shaded mesh arrays (positions local to the chunk's corner) and obstacles.
/// </summary>
public sealed class TerrainChunkData
{
    public TerrainChunkData(Vector2I coord, int generation, int gridPoints)
    {
        Coord = coord;
        Generation = generation;
        GridPoints = gridPoints;
        Heights = new float[gridPoints * gridPoints];
    }

    public Vector2I Coord { get; }
    /// <summary>Streamer generation this was built for; results from before a reseed are dropped.</summary>
    public int Generation { get; }
    /// <summary>Points per side of the height grid (cells + 1).</summary>
    public int GridPoints { get; }
    /// <summary>Row-major heights, x fastest: Heights[x + z * GridPoints].</summary>
    public float[] Heights { get; }
    public Vector3[] Vertices { get; set; } = System.Array.Empty<Vector3>();
    public Vector3[] Normals { get; set; } = System.Array.Empty<Vector3>();
    public Color[] Colors { get; set; } = System.Array.Empty<Color>();
    public List<ObstacleData> Obstacles { get; } = new();
    /// <summary>Trees, grass and flowers, by biome.</summary>
    public List<PlantData> Plants { get; } = new();
    /// <summary>Time spent generating on the background thread, in ms.</summary>
    public double BuildMilliseconds { get; set; }
}
