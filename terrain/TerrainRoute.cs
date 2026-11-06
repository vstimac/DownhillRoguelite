using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>Exposes the terrain's walkable spine to other systems as the run's route (RunState.Route).</summary>
public sealed class TerrainRoute : IRouteQuery
{
    private readonly TerrainGenerator _generator;

    public TerrainRoute(TerrainGenerator generator)
    {
        _generator = generator;
    }

    public float StartZ => _generator.Settings.SpawnZ;

    public Vector3 RoutePoint(float z)
    {
        float x = _generator.SpineX(z);
        return new Vector3(x, _generator.Height(x, z), z);
    }
}
