using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>A straight route with an optional flat approach, for analytical travel-time checks.</summary>
public sealed class MathTestRoute : IRouteQuery
{
    public float Length { get; init; } = 100.0f;
    public float RisePerMetre { get; init; }
    public float FlatApproach { get; init; }
    public float StartZ => 0.0f;
    public Vector3 SummitPad => new(-12.0f, 0.0f, 5.0f);
    public float SummitPadRadius => 0.0f;
    public Vector3 RoutePoint(float z) => new(0.0f, Mathf.Max(0.0f, -z - FlatApproach) * RisePerMetre, z);
    public Vector3 Station(int index) => RoutePoint(-Length * index);
    public int NextStationIndex(float z) => 1;
    public int StageAt(float z) => 0;
    public int PathCount(int stage) => 1;
    public RouteKind PathKind(int stage, int path) => RouteKind.Safe;
    public Vector3 PathPoint(int stage, int path, float z) => RoutePoint(z);
    public Vector2 BranchZone(int stage) => new(0.0f, -Length);
    public int NearestPath(Vector3 position, out float distance)
    {
        distance = Mathf.Abs(position.X);
        return 0;
    }
    public float GroundHeight(float x, float z) => RoutePoint(z).Y;
}
