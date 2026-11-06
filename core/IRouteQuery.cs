using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The guaranteed walkable route down the world, for systems that place things along it (supplies,
/// later shelters and wildlife). Terrain provides it through RunState.Route.
/// </summary>
public interface IRouteQuery
{
    /// <summary>Z where the run starts; the route descends toward -Z.</summary>
    float StartZ { get; }

    /// <summary>The route's point (on the ground) at <paramref name="z"/>.</summary>
    Vector3 RoutePoint(float z);
}
