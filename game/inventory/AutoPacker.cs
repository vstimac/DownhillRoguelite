using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Packs items in a given order, each dropped into one of its best spots: lowest first, then the
/// column nearest <c>preferredX</c> (0 = left edge, 0.5 = centre, 1 = right edge), then closest to
/// the back. The order decides what ends up low and what ends up high. Greedy placement leaves
/// gaps the big flat items can't use, so it backtracks: when an item doesn't fit anywhere, the
/// previous items try their next-best spots, within a search budget.
/// </summary>
public static class AutoPacker
{
    // Weights for ranking candidate spots: height dominates, then side, then depth.
    private const float HeightWeight = 1000.0f;
    private const float SideWeight = 10.0f;
    /// <summary>Spots tried per item before backing up.</summary>
    private const int CandidatesPerItem = 4;
    /// <summary>Search steps before giving up and keeping the best partial packing.</summary>
    private const int SearchBudget = 20000;

    private readonly record struct Spot(float Score, Vector3I Origin, int Orientation);

    /// <summary>Packs <paramref name="items"/> in order and returns the ones that didn't fit.</summary>
    public static List<ItemDefinition> Pack(BackpackGrid grid, IEnumerable<ItemDefinition> items, float preferredX)
    {
        List<ItemDefinition> order = new(items);
        int steps = 0;
        if (Search(grid, order, 0, preferredX, ref steps))
        {
            return new List<ItemDefinition>();
        }

        // No complete packing found: fall back to placing greedily what fits.
        foreach (PackedItem placed in new List<PackedItem>(grid.Items))
        {
            if (order.Contains(placed.Definition))
            {
                grid.Remove(placed);
            }
        }
        List<ItemDefinition> unplaced = new();
        foreach (ItemDefinition item in order)
        {
            if (!TryBestSpot(grid, item, preferredX, out Vector3I origin, out int orientation)
                || grid.Place(item, origin, orientation) == null)
            {
                unplaced.Add(item);
            }
        }
        return unplaced;
    }

    /// <summary>The single best spot for an item in the grid as it is now.</summary>
    public static bool TryBestSpot(BackpackGrid grid, ItemDefinition item, float preferredX, out Vector3I origin, out int orientation)
    {
        List<Spot> spots = Candidates(grid, item, preferredX, 1);
        if (spots.Count == 0)
        {
            origin = Vector3I.Zero;
            orientation = GridOrientations.Identity;
            return false;
        }
        origin = spots[0].Origin;
        orientation = spots[0].Orientation;
        return true;
    }

    private static bool Search(BackpackGrid grid, List<ItemDefinition> order, int index, float preferredX, ref int steps)
    {
        if (index == order.Count)
        {
            return true;
        }
        steps++;
        if (steps > SearchBudget)
        {
            return false;
        }

        foreach (Spot spot in Candidates(grid, order[index], preferredX, CandidatesPerItem))
        {
            PackedItem? placed = grid.Place(order[index], spot.Origin, spot.Orientation);
            if (placed == null)
            {
                continue;
            }
            if (Search(grid, order, index + 1, preferredX, ref steps))
            {
                return true;
            }
            grid.Remove(placed);
        }
        return false;
    }

    private static List<Spot> Candidates(BackpackGrid grid, ItemDefinition item, float preferredX, int count)
    {
        List<Spot> spots = new();
        HashSet<Vector3I> seenSizes = new();
        float targetX = preferredX * grid.Size.X;

        for (int o = 0; o < GridOrientations.All.Count; o++)
        {
            if (!GridOrientations.Allows(item.Orientation, o))
            {
                continue;
            }

            // Orientations that give the same box size give the same spots; try each size once.
            Vector3I size = GridOrientations.RotatedSize(item.SizeCells, o);
            if (!seenSizes.Add(size))
            {
                continue;
            }

            for (int x = 0; x + size.X <= grid.Size.X; x++)
            {
                for (int z = 0; z + size.Z <= grid.Size.Z; z++)
                {
                    int y = grid.DropHeight(size, x, z);
                    if (y < 0)
                    {
                        continue;
                    }

                    float centreX = x + size.X * 0.5f;
                    float score = y * HeightWeight + Mathf.Abs(centreX - targetX) * SideWeight + z;
                    spots.Add(new Spot(score, new Vector3I(x, y, z), o));
                }
            }
        }

        spots.Sort((a, b) => a.Score.CompareTo(b.Score));
        if (spots.Count > count)
        {
            spots.RemoveRange(count, spots.Count - count);
        }
        return spots;
    }
}
