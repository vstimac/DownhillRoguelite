using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>Per-copy cosmetic condition. Zero tint alpha means use the garment's authored colour.</summary>
public readonly record struct ItemAppearance(float Wetness = 0, float Dirt = 0, int Variant = 0, Color Tint = default);
