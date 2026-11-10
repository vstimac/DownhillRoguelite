using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>Searches an early, off-trail site with a sampled walkable approach. No terrain RNG is consumed.</summary>
public static class RecoveryPlacement
{
    public static bool TryFind(IRouteQuery route, ulong seed, Vector2 stageSpan, Vector2 offsets,
        float maxSlope, float pathGap, out Vector3 site)
    {
        site = Vector3.Zero;
        float top = route.StartZ;
        float bottom = route.Station(1).Z;
        if (bottom >= top || offsets.X <= 0 || offsets.Y < offsets.X || maxSlope <= 0 || maxSlope >= 89) { return false; }
        SplitMix64 random = new(seed ^ 0xA11CE504ul);
        float phase = random.NextFloat();
        int preferredSide = -1;
        if (random.NextFloat() > 0.5f) { preferredSide = 1; }
        for (int candidate = 0; candidate < 32; candidate++)
        {
            float fraction = Mathf.Lerp(stageSpan.X, stageSpan.Y, (candidate + phase) / 32.0f);
            float z = Mathf.Lerp(top, bottom, fraction);
            Vector3 trail = route.RoutePoint(z);
            for (int side = 0; side < 2; side++)
            {
                int sign = preferredSide;
                if (side == 1) { sign = -sign; }
                for (int offset = 0; offset < 3; offset++)
                {
                    float x = trail.X + sign * Mathf.Lerp(offsets.X, offsets.Y, offset / 2.0f);
                    Vector3 at = new(x, route.GroundHeight(x, z), z);
                    route.NearestPath(at, out float gap);
                    if (gap < pathGap || !Walkable(route, trail, at, maxSlope)) { continue; }
                    site = at;
                    return true;
                }
            }
        }
        return false;
    }

    public static bool Walkable(IRouteQuery route, Vector3 trail, Vector3 at, float maxSlope)
    {
        float maxGrade = Mathf.Tan(Mathf.DegToRad(maxSlope));
        int steps = Mathf.CeilToInt(trail.DistanceTo(at) * 2.0f);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = trail.Lerp(at, i / (float)Mathf.Max(steps, 1));
            float dx = route.GroundHeight(p.X + 0.5f, p.Z) - route.GroundHeight(p.X - 0.5f, p.Z);
            float dz = route.GroundHeight(p.X, p.Z + 0.5f) - route.GroundHeight(p.X, p.Z - 0.5f);
            if (!float.IsFinite(dx) || !float.IsFinite(dz) || dx * dx + dz * dz > maxGrade * maxGrade) { return false; }
        }
        return true;
    }
}
