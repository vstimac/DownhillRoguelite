using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// The pack's geometry for whoever draws it or points into it, in pack space: the grid centred on
/// x and z, its bottom at y = 0, open at the top, in metres. Pure maths, so pointing at items
/// works the same with or without a renderer.
/// </summary>
public static class PackGeometry
{
    /// <summary>A point in grid metres (from the lowest corner) to pack space.</summary>
    public static Vector3 GridToPack(PackLayout layout, Vector3 gridMeters)
    {
        Vector3 size = layout.SizeMeters;
        return gridMeters - new Vector3(size.X * 0.5f, 0.0f, size.Z * 0.5f);
    }

    /// <summary>Box in pack space for a block of cells, shrunk by <paramref name="inset"/> metres on every side.</summary>
    public static Aabb CellBox(PackLayout layout, Vector3I origin, Vector3I size, float inset)
    {
        Vector3 shrink = Vector3.One * inset;
        return new Aabb(GridToPack(layout, (Vector3)origin * layout.CellSize) + shrink, (Vector3)size * layout.CellSize - shrink * 2.0f);
    }

    /// <summary>Where a ray crosses the horizontal plane at <paramref name="height"/> metres.</summary>
    public static bool PlaneHit(float height, Vector3 from, Vector3 direction, out Vector3 point)
    {
        point = Vector3.Zero;
        if (Mathf.IsZeroApprox(direction.Y))
        {
            return false;
        }
        float t = (height - from.Y) / direction.Y;
        point = from + direction * t;
        return true;
    }

    /// <summary>The grid column (x, z) where a box of <paramref name="size"/> cells centred on a pack-space point would stand, kept inside the pack.</summary>
    public static Vector2I ColumnAt(PackLayout layout, Vector3 point, Vector3I size)
    {
        Vector3 grid = point + new Vector3(layout.SizeMeters.X * 0.5f, 0.0f, layout.SizeMeters.Z * 0.5f);
        int x = Mathf.RoundToInt(grid.X / layout.CellSize - size.X * 0.5f);
        int z = Mathf.RoundToInt(grid.Z / layout.CellSize - size.Z * 0.5f);
        return new Vector2I(
            Mathf.Clamp(x, 0, Mathf.Max(0, layout.GridSize.X - size.X)),
            Mathf.Clamp(z, 0, Mathf.Max(0, layout.GridSize.Z - size.Z)));
    }

    /// <summary>
    /// The packed copies a ray passes through, nearest first. Copies whose lowest cell is at or above
    /// <paramref name="cutHeight"/> (cut away to see underneath) and the copy <paramref name="ignoreId"/> are skipped.
    /// </summary>
    public static List<PackLayoutEntry> EntriesAlong(PackLayout layout, Vector3 from, Vector3 direction, int cutHeight, string ignoreId)
    {
        List<(float Distance, PackLayoutEntry Entry)> hits = new();
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (!entry.Packed || entry.Origin.Y >= cutHeight || entry.InstanceId == ignoreId)
            {
                continue;
            }
            if (RayHits(CellBox(layout, entry.Origin, entry.Size, 0.0f), from, direction, out float distance))
            {
                hits.Add((distance, entry));
            }
        }
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        List<PackLayoutEntry> entries = new();
        foreach ((float _, PackLayoutEntry entry) in hits)
        {
            entries.Add(entry);
        }
        return entries;
    }

    /// <summary>
    /// The layout's occupancy as a grid to test fits against, leaving out one copy (the one in the
    /// hand, whose old place is free to drop it back into).
    /// </summary>
    public static BackpackGrid Occupancy(PackLayout layout, string ignoreId)
    {
        BackpackGrid grid = new(layout.GridSize);
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (!entry.Packed || entry.InstanceId == ignoreId)
            {
                continue;
            }
            if (ItemText.AtPath(entry.ItemPath) is ItemDefinition item)
            {
                grid.Place(item, entry.Origin, entry.Orientation);
            }
        }
        return grid;
    }

    /// <summary>
    /// How to lay a model of <paramref name="extent"/> metres into a block of <paramref name="block"/>
    /// metres so it fills as much of it as it can: which right-angle turn of the model (an index into
    /// GridOrientations.All; a long model is laid along a long block) and the scale along each of the
    /// model's own axes, stretched at most <paramref name="maxStretch"/> beyond its proportions. The
    /// model's own pose is kept unless a turn fills at least <paramref name="turnGain"/> times as much;
    /// <paramref name="uprightOnly"/> keeps its up pointing up. Returns the share of the block filled.
    /// </summary>
    public static float FillBlock(Vector3 extent, Vector3 block, bool uprightOnly, float maxStretch, float turnGain, out int turn, out Vector3 scale)
    {
        turn = GridOrientations.Identity;
        scale = Vector3.One;
        float best = -1.0f;
        float blockVolume = Mathf.Max(block.X * block.Y * block.Z, 1e-9f);
        for (int candidate = 0; candidate < GridOrientations.All.Count; candidate++)
        {
            if (uprightOnly && !GridOrientations.IsUpright(candidate))
            {
                continue;
            }
            // The block as the turned model sees it: its own axes against the block's.
            Vector3 room = (GridOrientations.All[candidate].Inverse() * block).Abs();
            float fit = Mathf.Min(room.X / extent.X, Mathf.Min(room.Y / extent.Y, room.Z / extent.Z));
            float most = fit * Mathf.Max(maxStretch, 1.0f);
            Vector3 stretched = new(Mathf.Min(room.X / extent.X, most), Mathf.Min(room.Y / extent.Y, most), Mathf.Min(room.Z / extent.Z, most));
            float filled = stretched.X * extent.X * stretched.Y * extent.Y * stretched.Z * extent.Z / blockVolume;
            float needed = best * Mathf.Max(turnGain, 1.0f);
            if (candidate == GridOrientations.Identity)
            {
                needed = -1.0f;
            }
            if (filled > needed)
            {
                best = filled;
                turn = candidate;
                scale = stretched;
            }
        }
        return best;
    }

    /// <summary>Slab test: whether a ray hits a box, and how far along the ray.</summary>
    public static bool RayHits(Aabb box, Vector3 from, Vector3 direction, out float distance)
    {
        float near = float.MinValue;
        float far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = from[axis];
            float dir = direction[axis];
            float min = box.Position[axis];
            float max = box.End[axis];
            if (Mathf.IsZeroApprox(dir))
            {
                if (origin < min || origin > max)
                {
                    distance = 0.0f;
                    return false;
                }
                continue;
            }
            float t1 = (min - origin) / dir;
            float t2 = (max - origin) / dir;
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
        }
        distance = near;
        return far >= near && far >= 0.0f;
    }
}
