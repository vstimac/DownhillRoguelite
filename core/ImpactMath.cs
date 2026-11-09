using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Incoming normal speed removed by a collision, in m/s; excludes sliding and rebound.</summary>
public static class ImpactMath
{
    public static float SpeedLost(Vector3 before, Vector3 after, Vector3 surfaceNormal)
    {
        Vector3 normal = surfaceNormal.Normalized();
        float incoming = Mathf.Max(0.0f, -before.Dot(normal));
        float remaining = Mathf.Max(0.0f, -after.Dot(normal));
        return Mathf.Max(0.0f, incoming - remaining);
    }
}
