using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>One placed obstacle, in world space. Boulders are boxes; trunks are cylinders along their local Y.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Position">Centre of the shape.</param>
/// <param name="Size">Box size (boulder) or (diameter, length, diameter) (trunk).</param>
/// <param name="Rotation">Orientation of the shape.</param>
public readonly record struct ObstacleData(ObstacleKind Kind, Vector3 Position, Vector3 Size, Basis Rotation);
