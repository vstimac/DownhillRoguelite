using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Finds the SurfaceType where something touched a collider: per point for terrain, from metadata otherwise.</summary>
public static class SurfaceLookup
{
    public static SurfaceType? Find(GodotObject? collider, Vector3 worldPoint)
    {
        if (collider == null)
        {
            return null;
        }
        if (collider is ISurfaceQuery query)
        {
            return query.SurfaceAt(worldPoint);
        }
        if (!collider.HasMeta(SurfaceType.MetaKey))
        {
            return null;
        }

        // Dispose the Variant now: undisposed, it holds the resource until the GC runs.
        using Variant meta = collider.GetMeta(SurfaceType.MetaKey);
        return meta.AsGodotObject() as SurfaceType;
    }
}
