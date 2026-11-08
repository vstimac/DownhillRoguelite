namespace DownhillRoguelite.Inventory;

/// <summary>A copy outside the grid, retaining its wear instead of changing its shared definition.</summary>
public readonly record struct StoredItem(ItemDefinition Definition, float Durability = -1.0f);
