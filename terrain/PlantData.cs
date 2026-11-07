using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>One generated plant: kind, model path, where it stands on the ground, its turn, size and lean.</summary>
public readonly record struct PlantData(PlantKind Kind, string Model, Vector3 Position, float Yaw, float Scale, Vector2 Lean);
