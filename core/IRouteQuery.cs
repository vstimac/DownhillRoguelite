using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The walkable routes down the world, for systems that place things along them (supplies,
/// stations, cairns, estimates). Terrain provides it through RunState.Route.
///
/// The run is cut into stages: stage s runs from station s (the start, for stage 0) down to station
/// s + 1. Each stage has one or more paths: path 0 is the main, safe path (RoutePoint); the others
/// branch off below the stage's top station and rejoin above its bottom station (M10).
/// </summary>
public interface IRouteQuery
{
    /// <summary>Z where the run starts; the route descends toward -Z.</summary>
    float StartZ { get; }

    /// <summary>The main path's point (on the ground) at <paramref name="z"/>.</summary>
    Vector3 RoutePoint(float z);

    /// <summary>Centre of station <paramref name="index"/> on the route (1 = first below the start).</summary>
    Vector3 Station(int index);

    /// <summary>Index of the first station downhill of <paramref name="z"/>.</summary>
    int NextStationIndex(float z);

    /// <summary>The stage at <paramref name="z"/>: the one ending at NextStationIndex(z).</summary>
    int StageAt(float z);

    /// <summary>Paths in a stage, the main path included (at least 1).</summary>
    int PathCount(int stage);

    /// <summary>What kind a path of a stage is (path 0 is always Safe).</summary>
    RouteKind PathKind(int stage, int path);

    /// <summary>A path's point (on the ground) at <paramref name="z"/>; outside its branch it is the main path.</summary>
    Vector3 PathPoint(int stage, int path, float z);

    /// <summary>Z where a stage's branches leave the main path, and where they rejoin it.</summary>
    Vector2 BranchZone(int stage);

    /// <summary>The path of the stage at the position's z that is closest sideways, and how far away it is, in m.</summary>
    int NearestPath(Vector3 position, out float distance);
}
