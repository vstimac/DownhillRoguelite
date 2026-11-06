using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Implemented by colliders whose surface varies from point to point (terrain chunks), where one
/// SurfaceType in metadata isn't enough. Use SurfaceLookup.Find rather than calling this directly.
/// </summary>
public interface ISurfaceQuery
{
    /// <summary>The surface at a world-space point on this collider, or null if it has none.</summary>
    SurfaceType? SurfaceAt(Vector3 worldPoint);
}
