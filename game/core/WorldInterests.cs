using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Shared-world population/streaming interests. Nearby players share one population.</summary>
public static class WorldInterests
{
    public static Vector3 Nearest(Vector3 at, Vector3 local, bool livingOnly = false)
    {
        Vector3 nearest = local;
        float best = at.DistanceSquaredTo(local);
        if (GameEvents.SharedWorldHost)
        {
            foreach (NetCarrier player in GameEvents.OtherCarriers)
            {
                if (livingOnly && player.Down) { continue; }
                float distance = at.DistanceSquaredTo(player.Feet);
                if (distance < best) { best = distance; nearest = player.Feet; }
            }
        }
        return nearest;
    }
    public static Vector3 At(int cursor, Vector3 local)
    {
        if (!GameEvents.SharedWorldHost) { return local; }
        NetCarrier[] others = GameEvents.OtherCarriers;
        int index = cursor % (others.Length + 1);
        if (index > 0 && !others[index - 1].Down) { return others[index - 1].Feet; }
        return local;
    }
}
