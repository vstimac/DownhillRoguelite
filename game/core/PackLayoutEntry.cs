using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// One copy of an item as the pack pages see it: which copy, which item, and where it sits.
/// Loose copies (out of the pack, on the mat) have <paramref name="Packed"/> false and no cells.
/// </summary>
/// <param name="InstanceId">The copy's identity; requests name the copy by it.</param>
/// <param name="ItemId">The item's id (ItemDefinition.ItemId).</param>
/// <param name="ItemPath">Where the item's definition lives (res://…), for its model and rules.</param>
/// <param name="Packed">True in the grid, false loose beside it.</param>
/// <param name="Origin">Cell of the lowest x, y and z corner.</param>
/// <param name="Size">Size in cells after rotation.</param>
/// <param name="Orientation">Index into the 24 right-angle orientations.</param>
/// <param name="MassKg">The copy's mass, in kg.</param>
public readonly record struct PackLayoutEntry(
    string InstanceId,
    string ItemId,
    string ItemPath,
    bool Packed,
    Vector3I Origin,
    Vector3I Size,
    int Orientation,
    float MassKg);
