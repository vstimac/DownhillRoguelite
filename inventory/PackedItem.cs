using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>An item placed in a backpack grid: which item, where its lowest corner is, and how it's turned.</summary>
public sealed class PackedItem
{
    public PackedItem(ItemDefinition definition, Vector3I origin, int orientation)
    {
        Definition = definition;
        Origin = origin;
        Orientation = orientation;
        Size = GridOrientations.RotatedSize(definition.SizeCells, orientation);
    }

    public ItemDefinition Definition { get; }
    /// <summary>Hit points of worn gear while it sits in the pack (-1: new).</summary>
    public float Durability { get; set; } = -1.0f;
    public ItemAppearance Appearance { get; set; }
    public string InstanceId { get; set; } = System.Guid.NewGuid().ToString("N");
    /// <summary>Cell of the item's lowest x, y and z corner.</summary>
    public Vector3I Origin { get; }
    /// <summary>Index into GridOrientations.All.</summary>
    public int Orientation { get; }
    /// <summary>Size in cells after rotation.</summary>
    public Vector3I Size { get; }

    public bool Contains(Vector3I cell)
    {
        return cell.X >= Origin.X && cell.X < Origin.X + Size.X
            && cell.Y >= Origin.Y && cell.Y < Origin.Y + Size.Y
            && cell.Z >= Origin.Z && cell.Z < Origin.Z + Size.Z;
    }
}
