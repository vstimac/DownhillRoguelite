using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Occupancy of a backpack's cell grid. Fitting is a cell lookup: every cell an item would cover
/// must be inside the grid and empty. No physics involved.
/// </summary>
public sealed class BackpackGrid
{
    private readonly PackedItem?[] _cells;
    private readonly List<PackedItem> _items = new();

    public BackpackGrid(Vector3I size)
    {
        Size = size;
        _cells = new PackedItem?[size.X * size.Y * size.Z];
    }

    public Vector3I Size { get; }
    public IReadOnlyList<PackedItem> Items => _items;

    public int FreeCells
    {
        get
        {
            int free = 0;
            foreach (PackedItem? cell in _cells)
            {
                if (cell == null)
                {
                    free++;
                }
            }
            return free;
        }
    }

    public bool InBounds(Vector3I cell)
    {
        return cell.X >= 0 && cell.Y >= 0 && cell.Z >= 0 && cell.X < Size.X && cell.Y < Size.Y && cell.Z < Size.Z;
    }

    public PackedItem? ItemAt(Vector3I cell)
    {
        if (!InBounds(cell))
        {
            return null;
        }
        return _cells[Index(cell)];
    }

    /// <summary>Whether a box of <paramref name="size"/> cells fits with its lowest corner at <paramref name="origin"/>.</summary>
    public bool CanPlace(Vector3I size, Vector3I origin)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            return false;
        }
        Vector3I far = origin + size - Vector3I.One;
        if (!InBounds(origin) || !InBounds(far))
        {
            return false;
        }

        for (int x = origin.X; x <= far.X; x++)
        {
            for (int y = origin.Y; y <= far.Y; y++)
            {
                for (int z = origin.Z; z <= far.Z; z++)
                {
                    if (_cells[Index(new Vector3I(x, y, z))] != null)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>Places the item if it fits and its orientation is allowed; returns null otherwise.</summary>
    public PackedItem? Place(ItemDefinition definition, Vector3I origin, int orientation)
    {
        if (!GridOrientations.Allows(definition.Orientation, orientation))
        {
            return null;
        }

        PackedItem item = new(definition, origin, orientation);
        if (!CanPlace(item.Size, origin))
        {
            return null;
        }

        Fill(item, item);
        _items.Add(item);
        return item;
    }

    public void Remove(PackedItem item)
    {
        if (_items.Remove(item))
        {
            Fill(item, null);
        }
    }

    public void Clear()
    {
        System.Array.Clear(_cells);
        _items.Clear();
    }

    /// <summary>
    /// Where an item dropped in from the open top of column (x, z) comes to rest: the lowest y it
    /// fits with nothing in the way above it. Returns -1 if it doesn't fit even at the top.
    /// </summary>
    /// <summary>
    /// Every height in this column where an item of <paramref name="size"/> fits and rests (on the
    /// pack's floor or on something), from the top down: the gaps under other items too, not just
    /// the first landing from above.
    /// </summary>
    public List<int> RestingHeights(Vector3I size, int x, int z)
    {
        List<int> heights = new();
        for (int y = Size.Y - size.Y; y >= 0; y--)
        {
            Vector3I at = new(x, y, z);
            if (CanPlace(size, at) && (y == 0 || !CanPlace(size, new Vector3I(x, y - 1, z))))
            {
                heights.Add(y);
            }
        }
        return heights;
    }

    public int DropHeight(Vector3I size, int x, int z)
    {
        int rest = -1;
        for (int y = Size.Y - size.Y; y >= 0; y--)
        {
            if (!CanPlace(size, new Vector3I(x, y, z)))
            {
                break;
            }
            rest = y;
        }
        return rest;
    }

    private void Fill(PackedItem item, PackedItem? value)
    {
        Vector3I far = item.Origin + item.Size - Vector3I.One;
        for (int x = item.Origin.X; x <= far.X; x++)
        {
            for (int y = item.Origin.Y; y <= far.Y; y++)
            {
                for (int z = item.Origin.Z; z <= far.Z; z++)
                {
                    _cells[Index(new Vector3I(x, y, z))] = value;
                }
            }
        }
    }

    private int Index(Vector3I cell)
    {
        return (cell.Y * Size.Z + cell.Z) * Size.X + cell.X;
    }
}
