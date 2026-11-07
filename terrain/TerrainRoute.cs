using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>Exposes the terrain's paths (the spine and each stage's branches) to other systems as the run's route (RunState.Route).</summary>
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

    public Vector3 Station(int index)
    {
        Vector3 centre = _generator.Station(index);
        return new Vector3(centre.X, _generator.Height(centre.X, centre.Z), centre.Z);
    }

    public int NextStationIndex(float z)
    {
        return _generator.NextStationIndex(z);
    }

    public int StageAt(float z)
    {
        return _generator.NextStationIndex(z) - 1;
    }

    public int PathCount(int stage)
    {
        return 1 + _generator.Branches(Mathf.Max(stage, 0)).Length;
    }

    public RouteKind PathKind(int stage, int path)
    {
        RouteBranch[] branches = _generator.Branches(Mathf.Max(stage, 0));
        if (path <= 0 || path > branches.Length)
        {
            return RouteKind.Safe;
        }
        return branches[path - 1].Kind;
    }

    public Vector3 PathPoint(int stage, int path, float z)
    {
        RouteBranch[] branches = _generator.Branches(Mathf.Max(stage, 0));
        float x = _generator.SpineX(z);
        if (path > 0 && path <= branches.Length)
        {
            x = _generator.BranchX(branches[path - 1], z);
        }
        return new Vector3(x, _generator.Height(x, z), z);
    }

    public Vector2 BranchZone(int stage)
    {
        int s = Mathf.Max(stage, 0);
        float top = StartZ;
        if (s > 0)
        {
            top = _generator.Station(s).Z;
        }
        float bottom = _generator.Station(s + 1).Z;
        float gap = _generator.Settings.BranchStationGap;
        return new Vector2(top - gap, bottom + gap);
    }

    public int NearestPath(Vector3 position, out float distance)
    {
        int stage = StageAt(position.Z);
        int count = PathCount(stage);
        int best = 0;
        distance = float.MaxValue;
        for (int path = 0; path < count; path++)
        {
            float d = Mathf.Abs(position.X - PathPoint(stage, path, position.Z).X);
            if (d < distance)
            {
                distance = d;
                best = path;
            }
        }
        return best;
    }
}
