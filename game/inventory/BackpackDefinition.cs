using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// A backpack: a 3D grid of cells and where it sits on the carrier's back. Grid axes: x across
/// (to the carrier's right when worn), y up, z away from the back (z = 0 lies against the back).
/// </summary>
[GlobalClass]
public partial class BackpackDefinition : Resource
{
    [Export] public string DisplayName { get; set; } = "Backpack";
    [Export] public Vector3I GridSize { get; set; } = new(6, 12, 4);
    /// <summary>Edge length of one cell, in m.</summary>
    [Export] public float CellSize { get; set; } = 0.05f;
    /// <summary>Mass of the empty pack, in kg; it sits at the middle of the grid.</summary>
    [Export] public float EmptyMass { get; set; } = 1.5f;

    [ExportGroup("Mount (carrier space: origin at the feet, Y up, Z forward)")]
    /// <summary>Height of the pack's bottom above the feet when worn, in m.</summary>
    [Export] public float MountBottomHeight { get; set; } = 0.85f;
    /// <summary>Distance behind the body's centre line where the pack's back face sits, in m.</summary>
    [Export] public float MountBackOffset { get; set; } = 0.12f;
    /// <summary>Sideways offset of the pack's centre when worn, in m.</summary>
    [Export] public float MountCenterX { get; set; } = 0.0f;

    /// <summary>Grid extent in metres.</summary>
    public Vector3 SizeMeters => (Vector3)GridSize * CellSize;
}
