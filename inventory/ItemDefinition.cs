using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// A kind of item that can go in the backpack. Shapes are boxes measured in grid cells for now
/// (see docs/inventory-design.md); irregular shapes come later.
/// </summary>
[GlobalClass]
public partial class ItemDefinition : Resource
{
    [Export] public string ItemId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public float MassKg { get; set; } = 1.0f;

    /// <summary>Size in grid cells in the item's default orientation (x across, y up, z deep).</summary>
    [Export] public Vector3I SizeCells { get; set; } = Vector3I.One;

    /// <summary>Centre of mass relative to the box centre, in m, in the default orientation.</summary>
    [Export] public Vector3 CenterOfMassOffset { get; set; } = Vector3.Zero;

    [Export] public ItemTags Tags { get; set; } = ItemTags.None;
    [Export] public OrientationRule Orientation { get; set; } = OrientationRule.Any;

    /// <summary>Colour of the placeholder box until real models exist.</summary>
    [Export] public Color DebugColor { get; set; } = new(0.6f, 0.6f, 0.6f);

    public int Volume => SizeCells.X * SizeCells.Y * SizeCells.Z;
}
